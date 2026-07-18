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
}
