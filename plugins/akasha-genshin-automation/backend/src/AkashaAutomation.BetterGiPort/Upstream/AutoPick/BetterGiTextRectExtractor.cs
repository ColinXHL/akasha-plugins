using AkashaAutomation.Core.Capture;
using OpenCvSharp;

namespace AkashaAutomation.BetterGiPort.Upstream.AutoPick;

public static class BetterGiTextRectExtractor
{
    public static BetterGiTextAnalysisResult Analyze(CapturedFrame frame, RegionOfInterest textRegion)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!textRegion.FitsWithin(frame.Size))
        {
            throw new ArgumentOutOfRangeException(nameof(textRegion));
        }

        var analysis = frame.UseImage(source =>
        {
            using var text = new Mat(source, new Rect(textRegion.X, textRegion.Y, textRegion.Width, textRegion.Height));
            using var gray = new Mat();
            if (text.Channels() == 3)
            {
                Cv2.CvtColor(text, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                text.CopyTo(gray);
            }

            using var topRows = new Mat(gray, new Rect(0, 0, gray.Width, Math.Min(gray.Height, 3)));
            using var gradient = topRows.Sobel(MatType.CV_32F, 1, 0);
            var isPickAnimationInProgress = Cv2.Mean(gradient).Val0 < -3;

            using var binary = new Mat();
            Cv2.Threshold(gray, binary, 160, 255, ThresholdTypes.Binary);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            Cv2.Erode(binary, binary, kernel, iterations: 1);
            Cv2.Dilate(binary, binary, kernel, iterations: 2);
            return (Width: ProjectWidth(binary), IsPickAnimationInProgress: isPickAnimationInProgress);
        });

        return analysis.Width <= 5
            ? new BetterGiTextAnalysisResult(textRegion, UseDetector: true, analysis.IsPickAnimationInProgress)
            : new BetterGiTextAnalysisResult(
                new RegionOfInterest(
                    textRegion.X,
                    textRegion.Y,
                    Math.Min(textRegion.Width, analysis.Width + 5),
                    textRegion.Height),
                UseDetector: false,
                analysis.IsPickAnimationInProgress);
    }

    public static BetterGiTextRegionResult Extract(CapturedFrame frame, RegionOfInterest textRegion)
    {
        var analysis = Analyze(frame, textRegion);
        return new BetterGiTextRegionResult(analysis.Region, analysis.UseDetector);
    }

    public static RegionOfInterest Refine(CapturedFrame frame, RegionOfInterest textRegion) =>
        Extract(frame, textRegion).Region;

    public static bool IsPickAnimationInProgress(CapturedFrame frame, RegionOfInterest textRegion) =>
        Analyze(frame, textRegion).IsPickAnimationInProgress;

    private static int ProjectWidth(Mat binary)
    {
        using var projection = new Mat();
        Cv2.Reduce(binary, projection, 0, ReduceTypes.Sum, MatType.CV_32S);
        projection.GetArray(out int[] columnSums);
        var gapCount = 0;
        var lastNonEmpty = -1;
        for (var x = 0; x < columnSums.Length; x++)
        {
            if (columnSums[x] > 0)
            {
                lastNonEmpty = x;
                gapCount = 0;
                continue;
            }

            gapCount++;
            if (gapCount > 30)
            {
                break;
            }
        }

        return Math.Max(0, lastNonEmpty);
    }
}

public readonly record struct BetterGiTextRegionResult(
    RegionOfInterest Region,
    bool UseDetector);

public readonly record struct BetterGiTextAnalysisResult(
    RegionOfInterest Region,
    bool UseDetector,
    bool IsPickAnimationInProgress);
