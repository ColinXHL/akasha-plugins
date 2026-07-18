namespace AkashaAutomation.Features.AutoDialogue;

public sealed record AutoDialogueOptions
{
    public bool Enabled { get; init; }

    public bool QuicklyAdvanceEnabled { get; init; } = true;

    public string AdvanceKey { get; init; } = "Space";

    public string InteractionKey { get; init; } = "F";

    public string OptionStrategy { get; init; } = "First";

    public bool CustomPriorityOptionsEnabled { get; init; }

    public IReadOnlyList<string> CustomPriorityOptions { get; init; } = [];

    public bool SkipBuiltInPriority { get; init; }

    public int BeforeAdvanceDelayMilliseconds { get; init; }

    public int AfterOptionDelayMilliseconds { get; init; }

    public bool AutoWaitDialogueVoiceEnabled { get; init; }

    public int DialogueVoiceMaxWaitSeconds { get; init; } = 30;

    public bool ClosePopupPagesEnabled { get; init; } = true;

    public bool SubmitGoodsEnabled { get; init; } = true;

    public bool AutoGetDailyRewardsEnabled { get; init; } = true;

    public bool AutoReExploreEnabled { get; init; } = true;

    public bool AutoHangoutEnabled { get; init; }

    public string HangoutEnding { get; init; } = string.Empty;

    public bool AutoHangoutSkipEnabled { get; init; } = true;
}

public sealed record AutoDialogueRuntimeStatus(
    bool Enabled,
    bool IsRunning,
    string UiCategory,
    IReadOnlyList<string> LastRecognizedOptions,
    string LastDecisionReason,
    bool LastIntentSubmitted,
    bool VoiceWaitActive,
    bool VoiceWaitFallback,
    long? LastFrameSequence,
    DateTimeOffset? UpdatedAtUtc);
