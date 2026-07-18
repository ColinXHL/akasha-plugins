using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Recognition;
using OpenCvSharp;

namespace AkashaAutomation.BetterGiPort.Compatibility.AutoPick;

public sealed class BetterGiAutoPickRecognizer : IDisposable
{
    public const double DefaultThreshold = 0.8;
    private readonly ITemplateMatcher _templateMatcher;
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly Dictionary<TemplateKey, CapturedFrame> _templates = [];
    private readonly object _gate = new();
    private bool _disposed;

    public BetterGiAutoPickRecognizer(
        ITemplateMatcher templateMatcher,
        IAssetPathResolver assetPathResolver)
    {
        _templateMatcher = templateMatcher;
        _assetPathResolver = assetPathResolver;
    }

    public RecognitionResult FindInteraction(CapturedFrame frame, string pickKey)
    {
        var normalizedKey = NormalizePickKey(pickKey);
        return Match(
            frame,
            PickKeyAsset(normalizedKey),
            InteractionSearchRegion(frame.Size),
            DefaultThreshold);
    }

    public bool HasSpecialL(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoPickKeyL, SpecialLSearchRegion(frame.Size), DefaultThreshold).IsMatch;

    public BetterGiExcludeIconResult FindExcludeIcon(
        CapturedFrame frame,
        RegionOfInterest interactionRegion,
        int itemIconLeftOffset,
        int itemTextLeftOffset)
    {
        var iconRegion = IconRegion(frame.Size, interactionRegion, itemIconLeftOffset, itemTextLeftOffset);
        if (Match(frame, BetterGiAssetPaths.AutoPickChatIcon, iconRegion, DefaultThreshold).IsMatch)
        {
            return new(true, "chat_icon", iconRegion);
        }

        if (Match(frame, BetterGiAssetPaths.AutoPickSettingsIcon, iconRegion, DefaultThreshold).IsMatch)
        {
            return new(true, "settings_icon", iconRegion);
        }

        return new(false, null, iconRegion);
    }

    public static RegionOfInterest TextRegion(
        CaptureSize size,
        RegionOfInterest interactionRegion,
        int itemTextLeftOffset,
        int itemTextRightOffset)
    {
        var scale = AssetScale(size);
        var x = interactionRegion.X + (int)(itemTextLeftOffset * scale);
        var width = (int)((itemTextRightOffset - itemTextLeftOffset) * scale);
        if (x < 0 || width <= 0 || x + width > size.Width || interactionRegion.Bottom > size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(interactionRegion), "AutoPick text ROI is outside the captured frame.");
        }

        return new RegionOfInterest(x, interactionRegion.Y, width, interactionRegion.Height);
    }

    public static ushort ToVirtualKey(string pickKey) => NormalizePickKey(pickKey)[0];

    public static string NormalizePickKey(string pickKey)
    {
        var normalized = string.IsNullOrWhiteSpace(pickKey) ? "F" : pickKey.Trim().ToUpperInvariant();
        if (normalized is not ("E" or "F" or "G"))
        {
            throw new ArgumentOutOfRangeException(nameof(pickKey), "Phase 4 supports BetterGI's E, F and G interaction templates.");
        }

        return normalized;
    }

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
        double threshold)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _templateMatcher.Match(frame, GetTemplate(assetPath, frame.Size), searchRegion, threshold);
        }
    }

    private CapturedFrame GetTemplate(string assetPath, CaptureSize size)
    {
        var key = new TemplateKey(assetPath, size.Width, size.Height);
        if (_templates.TryGetValue(key, out var cached))
        {
            return cached;
        }

        using var original = Cv2.ImRead(_assetPathResolver.Resolve(assetPath), ImreadModes.Color);
        if (original.Empty())
        {
            throw new InvalidDataException($"Unable to load BetterGI AutoPick template '{assetPath}'.");
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

    private static RegionOfInterest InteractionSearchRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, (int)(1090 * scale), (int)(330 * scale), (int)(60 * scale), (int)(420 * scale));
    }

    private static RegionOfInterest SpecialLSearchRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(size, size.Width - (int)(110 * scale), (int)(550 * scale), (int)(70 * scale), (int)(100 * scale));
    }

    private static RegionOfInterest IconRegion(
        CaptureSize size,
        RegionOfInterest interactionRegion,
        int itemIconLeftOffset,
        int itemTextLeftOffset)
    {
        var scale = AssetScale(size);
        return Clamp(
            size,
            interactionRegion.X + (int)(itemIconLeftOffset * scale),
            interactionRegion.Y,
            (int)((itemTextLeftOffset - itemIconLeftOffset) * scale),
            interactionRegion.Height);
    }

    private static RegionOfInterest Clamp(CaptureSize size, int x, int y, int width, int height) =>
        new RegionOfInterest(Math.Max(0, x), Math.Max(0, y), Math.Max(1, width), Math.Max(1, height)).Clamp(size);

    private static double AssetScale(CaptureSize size) => (double)size.Height / 1080;

    private static string PickKeyAsset(string pickKey) => pickKey switch
    {
        "E" => BetterGiAssetPaths.AutoPickKeyE,
        "F" => BetterGiAssetPaths.AutoPickKeyF,
        "G" => BetterGiAssetPaths.AutoPickKeyG,
        _ => throw new ArgumentOutOfRangeException(nameof(pickKey)),
    };

    private readonly record struct TemplateKey(string AssetPath, int Width, int Height);
}

public sealed record BetterGiExcludeIconResult(
    bool IsExcludeIcon,
    string? Kind,
    RegionOfInterest SearchRegion);
