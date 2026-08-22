namespace AkashaAutomation.Features.QuickTeleport;

public sealed record QuickTeleportOptions
{
    public bool Enabled { get; init; }

    public int TeleportListClickDelayMilliseconds { get; init; } = 200;

    public int WaitTeleportPanelDelayMilliseconds { get; init; } = 50;
}

public sealed record QuickTeleportRuntimeStatus(
    bool Enabled,
    bool IsRunning,
    string State,
    string? LastCandidateText,
    string LastDecisionReason,
    bool LastIntentSubmitted,
    long? LastFrameSequence,
    DateTimeOffset? UpdatedAtUtc);
