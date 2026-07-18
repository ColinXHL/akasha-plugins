using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.Core.GameContext;

public sealed record GameWindowInfo(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    CaptureSize ClientSize,
    bool IsForeground);
