using System.Text.RegularExpressions;
using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Recognition;
using OpenCvSharp;

namespace AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;

public sealed partial class BetterGiAutoDialogueRecognizer : IGameUiContextClassifier, IDisposable
{
    public const double DefaultThreshold = 0.8;
    private readonly ITemplateMatcher _templateMatcher;
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly IOcrEngine _ocrEngine;
    private readonly Dictionary<TemplateKey, CapturedFrame> _templates = [];
    private readonly object _gate = new();
    private bool _disposed;

    public BetterGiAutoDialogueRecognizer(
        ITemplateMatcher templateMatcher,
        IAssetPathResolver assetPathResolver,
        IOcrEngine ocrEngine)
    {
        _templateMatcher = templateMatcher;
        _assetPathResolver = assetPathResolver;
        _ocrEngine = ocrEngine;
    }

    public ValueTask<GameUiCategory> ClassifyAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var talk = Match(frame, BetterGiAssetPaths.AutoSkipStopAuto, TopLeft(frame.Size)).IsMatch ||
                   Match(frame, BetterGiAssetPaths.AutoSkipDisabledUi, TopLeftWide(frame.Size)).IsMatch;
        return ValueTask.FromResult(talk ? GameUiCategory.Talk : GameUiCategory.Unknown);
    }

    public async ValueTask<IReadOnlyList<DialogueOptionCandidate>> FindDialogueOptionsAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var scale = AssetScale(frame.Size);
        var optionIcons = MatchAll(
            frame,
            BetterGiAssetPaths.AutoSkipOptionIcon,
            OptionSearchRegion(frame.Size),
            DefaultThreshold);
        if (optionIcons.Count == 0)
        {
            return [];
        }

        var lowest = optionIcons.OrderByDescending(region => region.Y).First();
        var left = Math.Clamp(lowest.Right + (int)(8 * scale), 0, frame.Size.Width - 1);
        var top = Math.Clamp(frame.Size.Height / 12, 0, frame.Size.Height - 1);
        var right = Math.Min(frame.Size.Width, left + (int)(535 * scale));
        var bottom = Math.Min(frame.Size.Height, lowest.Bottom + (int)(30 * scale));
        if (right <= left || bottom <= top)
        {
            return [];
        }

        var ocr = await _ocrEngine
            .RecognizeAsync(frame, new RegionOfInterest(left, top, right - left, bottom - top), cancellationToken)
            .ConfigureAwait(false);
        var orderedOcrRegions = ocr.Regions
            .OrderBy(region => region.Region.Y)
            .Where(region => IsUsefulText(region.Text))
            .ToArray();
        var regions = orderedOcrRegions
            .Where((region, index) =>
                index == orderedOcrRegions.Length - 1 ||
                orderedOcrRegions[index + 1].Region.Y - region.Region.Y <= 150 * scale)
            .Select(region => new DialogueOptionCandidate(
                BetterGiAutoSkipRules.NormalizeOcrText(region.Text),
                region.Region,
                IsOrange(frame, region.Region)))
            .Where(candidate => candidate.Text.Length > 0)
            .ToArray();
        if (regions.Length > 0)
        {
            return regions;
        }

        // BetterGI still chooses by bubble position when Paddle returns no
        // usable text. Top-to-bottom order preserves First/Last semantics.
        return optionIcons
            .OrderBy(region => region.Y)
            .Select(region => new DialogueOptionCandidate(string.Empty, region))
            .ToArray();
    }

    public RecognitionResult FindDialogueInteraction(CapturedFrame frame, string interactionKey)
    {
        var normalizedKey = BetterGiAutoPickRecognizer.NormalizePickKey(interactionKey);
        var assetPath = normalizedKey switch
        {
            "E" => BetterGiAssetPaths.AutoPickKeyE,
            "F" => BetterGiAssetPaths.AutoPickKeyF,
            "G" => BetterGiAssetPaths.AutoPickKeyG,
            _ => throw new ArgumentOutOfRangeException(nameof(interactionKey)),
        };
        return Match(frame, assetPath, DialogueInteractionSearchRegion(frame.Size));
    }

    public DialogueOptionCandidate? FindExclamationOption(CapturedFrame frame)
    {
        var result = Match(
            frame,
            BetterGiAssetPaths.AutoSkipExclamationIcon,
            OptionSearchRegion(frame.Size));
        return result.IsMatch && result.Region is { } region
            ? new DialogueOptionCandidate(string.Empty, region, Kind: DialogueOptionKind.Exclamation)
            : null;
    }

    public async ValueTask<IReadOnlyList<DialogueOptionCandidate>> FindHangoutOptionsAsync(
        CapturedFrame frame,
        CancellationToken cancellationToken = default)
    {
        var selected = MatchAll(frame, BetterGiAssetPaths.AutoSkipHangoutSelected, null, DefaultThreshold)
            .Select(region => (region, DialogueOptionKind.HangoutSelected));
        var unselected = MatchAll(frame, BetterGiAssetPaths.AutoSkipHangoutUnselected, null, DefaultThreshold)
            .Select(region => (region, DialogueOptionKind.HangoutUnselected));
        var result = new List<DialogueOptionCandidate>();
        foreach (var (icon, kind) in selected.Concat(unselected).OrderBy(item => item.region.Y))
        {
            var textRegion = HangoutTextRegion(frame.Size, icon);
            if (textRegion is null)
            {
                continue;
            }

            var ocr = await _ocrEngine.RecognizeAsync(frame, textRegion, cancellationToken).ConfigureAwait(false);
            result.Add(new DialogueOptionCandidate(
                BetterGiAutoSkipRules.NormalizeOcrText(ocr.Text),
                icon,
                Kind: kind));
        }

        return result;
    }

    public RecognitionResult FindHangoutSkip(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipHangoutSkip, TopLeft(frame.Size));

    public RecognitionResult FindPageClose(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipPageClose, TopRight(frame.Size));

    public RecognitionResult FindPageCloseMain(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipPageCloseMain, TopLeftNarrow(frame.Size));

    public RecognitionResult FindSubmitExclamation(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipSubmitExclamation, TopQuarter(frame.Size));

    public RecognitionResult FindSubmitGoods(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipSubmitGoods, TopLeftHalf(frame.Size), 0.9);

    public RecognitionResult FindConfirm(CapturedFrame frame) =>
        Best(
            Match(frame, BetterGiAssetPaths.AutoSkipConfirm1, null),
            Match(frame, BetterGiAssetPaths.AutoSkipConfirm2, null));

    public RecognitionResult FindDailyReward(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipPrimogem, MiddleThird(frame.Size));

    public RecognitionResult FindCollect(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipCollect, BottomLeft(frame.Size));

    public RecognitionResult FindReExplore(CapturedFrame frame) =>
        Match(frame, BetterGiAssetPaths.AutoSkipReExplore, BottomRightCenter(frame.Size));

    public RegionOfInterest? FindBottomTriangle(CapturedFrame frame)
    {
        var scale = AssetScale(frame.Size);
        var crop = Clamp(frame.Size, (int)(945 * scale), (int)(980 * scale), (int)(30 * scale), (int)(80 * scale));
        return frame.UseImage(source =>
        {
            using var view = new Mat(source, crop.ToRect());
            using var hsv = new Mat();
            Cv2.CvtColor(view, hsv, ColorConversionCodes.BGR2HSV);
            using var yellow = new Mat();
            using var blue = new Mat();
            Cv2.InRange(hsv, new Scalar(0, 240, 229), new Scalar(25, 255, 255), yellow);
            Cv2.InRange(hsv, new Scalar(90, 156, 145), new Scalar(99, 208, 253), blue);
            var contours = yellow.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple)
                .Concat(blue.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple));
            foreach (var contour in contours)
            {
                var area = Cv2.ContourArea(contour);
                var approx = Cv2.ApproxPolyDP(contour, 0.04 * Cv2.ArcLength(contour, true), true);
                if (area is < 10 or > 50 || approx.Length != 3)
                {
                    continue;
                }

                var rect = Cv2.BoundingRect(approx);
                return (RegionOfInterest?)new RegionOfInterest(crop.X + rect.X, crop.Y + rect.Y, rect.Width, rect.Height);
            }

            return null;
        });
    }

    public RegionOfInterest? FindCharacterPopup(CapturedFrame frame) => frame.UseImage(source =>
    {
        var scale = AssetScale(frame.Size);
        using var image = source.Clone();
        Cv2.Rectangle(image, new Rect((int)(240 * scale), (int)(395 * scale), (int)(300 * scale), (int)(50 * scale)), new Scalar(229, 241, 245), -1);
        Cv2.Rectangle(image, new Rect((int)(290 * scale), (int)(660 * scale), (int)(210 * scale), (int)(40 * scale)), new Scalar(101, 82, 74), -1);
        using var hsv = image.CvtColor(ColorConversionCodes.BGR2HSV);
        using var light = new Mat();
        using var dark = new Mat();
        using var combined = new Mat();
        Cv2.InRange(hsv, new Scalar(18, 16, 234), new Scalar(27, 19, 250), light);
        Cv2.InRange(hsv, new Scalar(101, 57, 95), new Scalar(118, 85, 106), dark);
        Cv2.BitwiseOr(light, dark, combined);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(21, 21));
        Cv2.MorphologyEx(combined, combined, MorphTypes.Close, kernel);
        Cv2.MorphologyEx(combined, combined, MorphTypes.Open, kernel);
        var contours = combined.FindContoursAsArray(RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var box = Cv2.BoundingRect(contour);
            if (box.Height == 0)
            {
                continue;
            }

            var areaRatio = (double)(box.Width * box.Height) / (image.Width * image.Height);
            var aspect = (double)box.Width / box.Height;
            if (areaRatio is <= 0.24 or >= 0.3 || aspect is < 5.6 or > 7.2 ||
                box.Y <= image.Height * 0.3 || box.Bottom >= image.Height * 0.7)
            {
                continue;
            }

            using var lightCrop = new Mat(light, box);
            using var darkCrop = new Mat(dark, box);
            if (Cv2.CountNonZero(lightCrop) > 0 && Cv2.CountNonZero(darkCrop) > 0)
            {
                return (RegionOfInterest?)new RegionOfInterest(box.X, box.Y, box.Width, box.Height);
            }
        }

        return null;
    });

    public double GetBlackScreenRatio(CapturedFrame frame) => frame.UseImage(source =>
    {
        using var gray = source.Channels() == 1 ? source.Clone() : source.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var middle = new Mat(gray, new Rect(0, gray.Height / 3, gray.Width, gray.Height / 3));
        using var black = middle.InRange(Scalar.Black, Scalar.Black);
        return Cv2.CountNonZero(black) / (double)(middle.Width * middle.Height);
    });

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
        RegionOfInterest? searchRegion,
        double threshold = DefaultThreshold)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _templateMatcher.Match(frame, GetTemplate(assetPath, frame.Size), searchRegion, threshold);
        }
    }

    private IReadOnlyList<RegionOfInterest> MatchAll(
        CapturedFrame frame,
        string assetPath,
        RegionOfInterest? searchRegion,
        double threshold)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var template = GetTemplate(assetPath, frame.Size);
            return frame.UseImage(source => template.UseImage(pattern =>
            {
                var roi = searchRegion ?? new RegionOfInterest(0, 0, frame.Size.Width, frame.Size.Height);
                using var view = new Mat(source, roi.ToRect());
                if (pattern.Width > view.Width || pattern.Height > view.Height)
                {
                    return (IReadOnlyList<RegionOfInterest>)[];
                }

                using var scores = new Mat();
                Cv2.MatchTemplate(view, pattern, scores, TemplateMatchModes.CCoeffNormed);
                var matches = new List<(RegionOfInterest Region, double Score)>();
                using var working = scores.Clone();
                while (true)
                {
                    Cv2.MinMaxLoc(working, out _, out var score, out _, out var location);
                    if (score < threshold)
                    {
                        break;
                    }

                    matches.Add((new RegionOfInterest(
                        roi.X + location.X,
                        roi.Y + location.Y,
                        pattern.Width,
                        pattern.Height), score));
                    var suppress = new Rect(
                        Math.Max(0, location.X - pattern.Width / 2),
                        Math.Max(0, location.Y - pattern.Height / 2),
                        Math.Min(working.Width - Math.Max(0, location.X - pattern.Width / 2), pattern.Width * 2),
                        Math.Min(working.Height - Math.Max(0, location.Y - pattern.Height / 2), pattern.Height * 2));
                    working[suppress].SetTo(Scalar.All(-1));
                }

                return (IReadOnlyList<RegionOfInterest>)matches
                    .OrderByDescending(match => match.Score)
                    .Select(match => match.Region)
                    .ToArray();
            }));
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
            throw new InvalidDataException($"Unable to load BetterGI AutoSkip template '{assetPath}'.");
        }

        var scale = AssetScale(size);
        using var resized = original.Resize(new Size(
            Math.Max(1, (int)(original.Width * scale)),
            Math.Max(1, (int)(original.Height * scale))),
            interpolation: scale > 1 ? InterpolationFlags.Linear : InterpolationFlags.Area);
        var template = CapturedFrame.TakeOwnership(resized.Clone(), 0, DateTimeOffset.UnixEpoch, assetPath);
        _templates.Add(key, template);
        return template;
    }

    private bool IsOrange(CapturedFrame frame, RegionOfInterest region) => frame.UseImage(source =>
    {
        var safe = region.Clamp(frame.Size);
        using var text = new Mat(source, safe.ToRect());
        using var mask = new Mat();
        Cv2.InRange(text, new Scalar(243, 195, 48), new Scalar(255, 205, 55), mask);
        return Cv2.CountNonZero(mask) / (double)(mask.Width * mask.Height) > 0.06;
    });

    private static bool IsUsefulText(string text)
    {
        var normalized = BetterGiAutoSkipRules.NormalizeOcrText(text);
        return normalized.Length > 0 && !(normalized.Length < 5 && EnglishOrNumber().IsMatch(normalized));
    }

    private static RegionOfInterest? HangoutTextRegion(CaptureSize size, RegionOfInterest icon)
    {
        var scale = AssetScale(size);
        RegionOfInterest region;
        if (icon.X > size.Width / 2)
        {
            region = new RegionOfInterest(icon.Right, icon.Y - icon.Height * 2 / 3, size.Width - icon.Right - (int)(10 * scale), icon.Height * 7 / 3);
        }
        else if (icon.Right < size.Width / 2)
        {
            region = new RegionOfInterest((int)(10 * scale), icon.Y - icon.Height * 2 / 3, icon.X - (int)(10 * scale), icon.Height * 7 / 3);
        }
        else
        {
            return null;
        }

        region = region.Clamp(size);
        return region.Width >= size.Width / 8 ? region : null;
    }

    private static RecognitionResult Best(RecognitionResult first, RecognitionResult second) =>
        first.Confidence >= second.Confidence ? first : second;

    private static double AssetScale(CaptureSize size) => size.Height / 1080d;
    private static RegionOfInterest TopLeft(CaptureSize size) => Clamp(size, 0, 0, size.Width / 5, size.Height / 8);
    private static RegionOfInterest TopLeftWide(CaptureSize size) => Clamp(size, 0, 0, size.Width / 3, size.Height / 8);
    private static RegionOfInterest TopLeftNarrow(CaptureSize size) => Clamp(size, 0, 0, size.Width / 25, size.Height / 14);
    private static RegionOfInterest TopLeftHalf(CaptureSize size) => Clamp(size, 0, 0, size.Width / 2, size.Height / 3);
    private static RegionOfInterest TopRight(CaptureSize size) => Clamp(size, size.Width - size.Width / 8, 0, size.Width / 8, size.Height / 8);
    private static RegionOfInterest TopQuarter(CaptureSize size) => Clamp(size, 0, 0, size.Width, size.Height / 4);
    private static RegionOfInterest MiddleThird(CaptureSize size) => Clamp(size, 0, size.Height / 3, size.Width, size.Height / 3);
    private static RegionOfInterest BottomLeft(CaptureSize size) => Clamp(size, 0, size.Height * 2 / 3, size.Width / 4, size.Height / 3);
    private static RegionOfInterest BottomRightCenter(CaptureSize size) => Clamp(size, size.Width / 2, size.Height * 3 / 4, size.Width / 4, size.Height / 4);
    private static RegionOfInterest OptionSearchRegion(CaptureSize size) => Clamp(size, size.Width / 2, size.Height / 12, size.Width / 3, size.Height - size.Height / 12 - 10);
    private static RegionOfInterest DialogueInteractionSearchRegion(CaptureSize size)
    {
        var scale = AssetScale(size);
        return Clamp(
            size,
            (int)(1200 * scale),
            (int)(350 * scale),
            (int)(50 * scale),
            size.Height - (int)(570 * scale));
    }
    private static RegionOfInterest Clamp(CaptureSize size, int x, int y, int width, int height) =>
        new RegionOfInterest(Math.Max(0, x), Math.Max(0, y), Math.Max(1, width), Math.Max(1, height)).Clamp(size);

    [GeneratedRegex("^[a-zA-Z0-9]+$")]
    private static partial Regex EnglishOrNumber();

    private readonly record struct TemplateKey(string AssetPath, int Width, int Height);
}

internal static class RegionOfInterestOpenCvExtensions
{
    public static Rect ToRect(this RegionOfInterest region) => new(region.X, region.Y, region.Width, region.Height);
}
