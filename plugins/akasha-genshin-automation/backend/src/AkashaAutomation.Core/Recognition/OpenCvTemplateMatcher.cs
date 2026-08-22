using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using OpenCvSharp;

namespace AkashaAutomation.Core.Recognition;

public sealed class OpenCvTemplateMatcher : ITemplateMatcher
{
    public RecognitionResult Match(
        CapturedFrame frame,
        CapturedFrame template,
        RegionOfInterest? searchRegion = null,
        double threshold = 0.8)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 1);

        using var search = searchRegion is { } region ? frame.CloneRegion(region) : null;
        var target = search ?? frame;
        if (template.Size.Width > target.Size.Width || template.Size.Height > target.Size.Height)
        {
            return new RecognitionResult(false, 0, null, "template_larger_than_search_region");
        }

        return target.UseImage(targetMat =>
            template.UseImage(templateMat =>
            {
                using var result = new Mat();
                Cv2.MatchTemplate(targetMat, templateMat, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out var confidence, out _, out var location);
                var offsetX = searchRegion?.X ?? 0;
                var offsetY = searchRegion?.Y ?? 0;
                var matchRegion = new RegionOfInterest(
                    offsetX + location.X,
                    offsetY + location.Y,
                    template.Size.Width,
                    template.Size.Height);
                return new RecognitionResult(confidence >= threshold, confidence, matchRegion);
            }));
    }

    public IReadOnlyList<RecognitionResult> MatchAll(
        CapturedFrame frame,
        CapturedFrame template,
        RegionOfInterest? searchRegion = null,
        double threshold = 0.8,
        int maximumMatches = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(threshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumMatches, 1);

        var region = searchRegion ?? new RegionOfInterest(0, 0, frame.Size.Width, frame.Size.Height);
        if (!region.FitsWithin(frame.Size))
        {
            throw new ArgumentOutOfRangeException(nameof(searchRegion), "The search region must fit within the frame.");
        }

        if (template.Size.Width > region.Width || template.Size.Height > region.Height)
        {
            return [];
        }

        return frame.UseImage(source =>
            template.UseImage(templateMat =>
            {
                using var targetMat = new Mat(
                    source,
                    new Rect(region.X, region.Y, region.Width, region.Height));
                using var scores = new Mat();
                Cv2.MatchTemplate(targetMat, templateMat, scores, TemplateMatchModes.CCoeffNormed);
                using var working = scores.Clone();
                var matches = new List<RecognitionResult>();

                while (matches.Count < maximumMatches)
                {
                    Cv2.MinMaxLoc(working, out _, out var confidence, out _, out var location);
                    if (confidence < threshold)
                    {
                        break;
                    }

                    matches.Add(
                        new RecognitionResult(
                            true,
                            confidence,
                            new RegionOfInterest(
                                region.X + location.X,
                                region.Y + location.Y,
                                template.Size.Width,
                                template.Size.Height)));

                    var left = Math.Max(0, location.X - template.Size.Width / 2);
                    var top = Math.Max(0, location.Y - template.Size.Height / 2);
                    var suppress = new Rect(
                        left,
                        top,
                        Math.Min(working.Width - left, template.Size.Width * 2),
                        Math.Min(working.Height - top, template.Size.Height * 2));
                    working[suppress].SetTo(Scalar.All(-1));
                }

                return (IReadOnlyList<RecognitionResult>)matches;
            }));
    }
}
