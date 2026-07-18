using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.Core.Recognition;

public sealed record RecognitionResult(
    bool IsMatch,
    double Confidence,
    RegionOfInterest? Region,
    string? Diagnostic = null);
