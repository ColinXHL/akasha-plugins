using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.Core.Ocr;

public sealed record OcrResult(
    string Text,
    IReadOnlyList<OcrTextRegion> Regions,
    TimeSpan Duration)
{
    public static OcrResult Empty(TimeSpan duration = default) => new(string.Empty, [], duration);
}

public sealed record OcrTextRegion(
    string Text,
    double Confidence,
    RegionOfInterest Region);
