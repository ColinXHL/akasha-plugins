using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Recognition;

namespace AkashaAutomation.Core.Abstractions;

public interface ITemplateMatcher
{
    RecognitionResult Match(
        CapturedFrame frame,
        CapturedFrame template,
        RegionOfInterest? searchRegion = null,
        double threshold = 0.8);

    IReadOnlyList<RecognitionResult> MatchAll(
        CapturedFrame frame,
        CapturedFrame template,
        RegionOfInterest? searchRegion = null,
        double threshold = 0.8,
        int maximumMatches = 64);
}
