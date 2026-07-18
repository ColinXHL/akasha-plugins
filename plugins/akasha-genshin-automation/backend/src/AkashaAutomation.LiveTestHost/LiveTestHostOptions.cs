namespace AkashaAutomation.LiveTestHost;

public sealed record LiveTestHostOptions(
    bool AutoPickEnabled,
    bool AutoDialogueEnabled,
    int IntervalMilliseconds = 50,
    bool ShowAllFrames = false);
