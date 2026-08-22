using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Recognition;
using OpenCvSharp;

namespace AkashaAutomation.BetterGiPort.Compatibility.QuickTeleport;

public sealed class BetterGiQuickTeleportRecognizer : IGameUiContextDetector, IDisposable
{
    public const double DefaultThreshold = 0.8;
    private const double ExpectedAspectRatio = 16d / 9d;
    private const double AspectRatioTolerance = 0.02;
    private static readonly IReadOnlyList<string> CandidateIconAssets =
    [
        BetterGiAssetPaths.QuickTeleportWaypoint,
        BetterGiAssetPaths.QuickTeleportStatueOfTheSeven,
        BetterGiAssetPaths.QuickTeleportDomain,
        BetterGiAssetPaths.QuickTeleportDomain2,
        BetterGiAssetPaths.QuickTeleportObsidianTotemPole,
        BetterGiAssetPaths.QuickTeleportPortableWaypoint,
        BetterGiAssetPaths.QuickTeleportMansion,
        BetterGiAssetPaths.QuickTeleportSubSpaceWaypoint,
        BetterGiAssetPaths.QuickTeleportNodKraiMeetingPoint,
        BetterGiAssetPaths.QuickTeleportTabletOfTona,
        BetterGiAssetPaths.QuickTeleportMarkTransPointMoonTower,
    ];

    private readonly ITemplateMatcher _templateMatcher;
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly IOcrEngine _ocrEngine;
    private readonly Dictionary<TemplateKey, CapturedFrame> _templates = [];
    private readonly object _gate = new();
    private bool _disposed;

    public BetterGiQuickTeleportRecognizer(
        ITemplateMatcher templateMatcher,
        IAssetPathResolver assetPathResolver,
        IOcrEngine ocrEngine)
    {
        _templateMatcher = templateMatcher;
        _assetPathResolver = assetPathResolver;
        _ocrEngine = ocrEngine;
    }

    public string Id => "bettergi.big-map";

    public int Priority => 90;

    public ValueTask<GameUiCategory?> DetectAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupportedCapture(frame.Size))
        {
            return ValueTask.FromResult<GameUiCategory?>(null);
        }

        var isBigMap = Match(
                frame,
                BetterGiAssetPaths.QuickTeleportMapScaleButton,
                MapScaleButtonRegion(frame.Size)).IsMatch ||
            Match(
                frame,
                BetterGiAssetPaths.QuickTeleportMapSettingsButton,
                MapSettingsButtonRegion(frame.Size)).IsMatch;
        return ValueTask.FromResult<GameUiCategory?>(isBigMap ? GameUiCategory.BigMap : null);
    }

    public RecognitionResult FindTeleportButton(CapturedFrame frame) =>
        Match(
            frame,
            BetterGiAssetPaths.QuickTeleportButton,
            TeleportButtonRegion(frame.Size));

    public bool IsMapSelectionIdle(CapturedFrame frame) =>
        Match(
            frame,
            BetterGiAssetPaths.QuickTeleportMapCloseButton,
            MapCloseButtonRegion(frame.Size)).IsMatch ||
        Match(
            frame,
            BetterGiAssetPaths.QuickTeleportMapChoose,
            MapChooseRegion(frame.Size)).IsMatch;

    public async ValueTask<QuickTeleportCandidate?> FindFirstValidCandidateAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSupportedCapture(frame.Size))
        {
            return null;
        }

        using var greyFrame = CreateGreyFrame(frame);
        var iconRegion = MapChooseIconRegion(frame.Size);
        var matches = new List<(string AssetPath, RecognitionResult Match)>();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (var assetPath in CandidateIconAssets)
            {
                var template = GetTemplate(assetPath, frame.Size, grey: true);
                matches.AddRange(
                    _templateMatcher
                        .MatchAll(greyFrame, template, iconRegion, DefaultThreshold, maximumMatches: 16)
                        .Select(match => (assetPath, match)));
            }
        }

        foreach (var entry in matches
                     .Where(entry => entry.Match.Region is not null)
                     .OrderBy(entry => entry.Match.Region!.Value.Y)
                     .ThenByDescending(entry => entry.Match.Confidence))
        {
            var icon = entry.Match.Region!.Value;
            var textRegion = CandidateTextRegion(frame.Size, icon);
            var text = await RecognizeWhiteTextAsync(frame, textRegion, cancellationToken).ConfigureAwait(false);
            if (text.Length <= 1)
            {
                continue;
            }

            return new QuickTeleportCandidate(
                Path.GetFileNameWithoutExtension(entry.AssetPath),
                text,
                icon,
                textRegion,
                entry.Match.Confidence);
        }

        return null;
    }

    public static bool IsSupportedCapture(CaptureSize size) =>
        size.Height > 0 &&
        Math.Abs((double)size.Width / size.Height - ExpectedAspectRatio) <= AspectRatioTolerance;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var template in _templates.Values)
            {
                template.Dispose();
            }

            _templates.Clear();
        }
    }

    private RecognitionResult Match(
        CapturedFrame frame,
        string assetPath,
        RegionOfInterest searchRegion,
        double threshold = DefaultThreshold)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _templateMatcher.Match(
                frame,
                GetTemplate(assetPath, frame.Size, grey: false),
                searchRegion,
                threshold);
        }
    }

    private CapturedFrame GetTemplate(string assetPath, CaptureSize size, bool grey)
    {
        var key = new TemplateKey(assetPath, size.Width, size.Height, grey);
        if (_templates.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var original = Cv2.ImRead(
            _assetPathResolver.Resolve(assetPath),
            grey ? ImreadModes.Grayscale : ImreadModes.Color);
        if (original.Empty())
        {
            throw new InvalidDataException($"Unable to load BetterGI QuickTeleport template '{assetPath}'.");
        }

        var scale = AssetScale(size);
        using var resized = new Mat();
        Cv2.Resize(
            original,
            resized,
            new Size(
                Math.Max(1, (int)(original.Width * scale)),
                Math.Max(1, (int)(original.Height * scale))),
            interpolation: scale > 1 ? InterpolationFlags.Linear : InterpolationFlags.Area);
        var template = CapturedFrame.TakeOwnership(resized.Clone(), 0, DateTimeOffset.UnixEpoch, assetPath);
        _templates.Add(key, template);
        return template;
    }

    private async ValueTask<string> RecognizeWhiteTextAsync(
        CapturedFrame frame,
        RegionOfInterest textRegion,
        CancellationToken cancellationToken)
    {
        using var crop = frame.CloneRegion(textRegion, "quick-teleport-candidate-text");
        using var filtered = crop.UseImage(source =>
        {
            using var hls = new Mat();
            using var mask = new Mat();
            Cv2.CvtColor(source, hls, ColorConversionCodes.BGR2HLS);
            Cv2.InRange(hls, new Scalar(0, 245, 0), new Scalar(180, 255, 15), mask);
            var bgr = new Mat();
            Cv2.CvtColor(mask, bgr, ColorConversionCodes.GRAY2BGR);
            return CapturedFrame.TakeOwnership(
                bgr,
                frame.Sequence,
                frame.CapturedAtUtc,
                "quick-teleport-white-text");
        });
        var localRegion = new RegionOfInterest(0, 0, filtered.Size.Width, filtered.Size.Height);
        var result = await _ocrEngine
            .RecognizeSingleLineAsync(filtered, localRegion, cancellationToken)
            .ConfigureAwait(false);
        return string.Concat(result.Text.Where(character => !char.IsWhiteSpace(character))).Trim();
    }

    private static CapturedFrame CreateGreyFrame(CapturedFrame frame) =>
        frame.UseImage(source =>
        {
            var grey = new Mat();
            Cv2.CvtColor(source, grey, ColorConversionCodes.BGR2GRAY);
            return CapturedFrame.TakeOwnership(grey, frame.Sequence, frame.CapturedAtUtc, "quick-teleport-grey");
        });

    private static RegionOfInterest TeleportButtonRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, (int)(1440 * scale), size.Height - (int)(120 * scale), (int)(100 * scale), (int)(120 * scale));
    }

    private static RegionOfInterest MapScaleButtonRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, (int)(30 * scale), (int)(440 * scale), (int)(40 * scale), (int)(200 * scale));
    }

    private static RegionOfInterest MapCloseButtonRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, size.Width - (int)(107 * scale), (int)(19 * scale), (int)(58 * scale), (int)(58 * scale));
    }

    private static RegionOfInterest MapSettingsButtonRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, (int)(25 * scale), (int)(990 * scale), (int)(58 * scale), (int)(62 * scale));
    }

    private static RegionOfInterest MapChooseRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, size.Width - (int)(480 * scale), 0, (int)(100 * scale), (int)(70 * scale));
    }

    private static RegionOfInterest MapChooseIconRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(
            size,
            (int)(1270 * scale),
            (int)(100 * scale),
            (int)(50 * scale),
            size.Height - (int)(200 * scale));
    }

    private static RegionOfInterest CandidateTextRegion(CaptureSize size, RegionOfInterest icon)
    {
        var x = Math.Min(icon.Right, size.Width - 1);
        var y = Math.Max(0, icon.Y - 8);
        return Clamp(size, x, y, 200, icon.Height + 16);
    }

    private static RegionOfInterest Clamp(CaptureSize size, int x, int y, int width, int height) =>
        new RegionOfInterest(
                Math.Clamp(x, 0, size.Width - 1),
                Math.Clamp(y, 0, size.Height - 1),
                Math.Max(1, width),
                Math.Max(1, height))
            .Clamp(size);

    private static double AssetScale(CaptureSize size) => size.Height / 1080d;

    private readonly record struct TemplateKey(string AssetPath, int Width, int Height, bool Grey);
}

public sealed record QuickTeleportCandidate(
    string Kind,
    string Text,
    RegionOfInterest IconRegion,
    RegionOfInterest ClickRegion,
    double Confidence);
