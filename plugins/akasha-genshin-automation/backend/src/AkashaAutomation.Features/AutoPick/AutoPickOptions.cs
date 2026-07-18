namespace AkashaAutomation.Features.AutoPick;

public sealed record AutoPickOptions
{
    public bool Enabled { get; init; }

    public string PickKey { get; init; } = "F";

    public string OcrEngine { get; init; } = "Paddle";

    public bool BlackListEnabled { get; init; } = true;

    public bool WhiteListEnabled { get; init; }

    public IReadOnlyList<string> UserExactBlacklist { get; init; } = [];

    public IReadOnlyList<string> UserFuzzyBlacklist { get; init; } = [];

    public IReadOnlyList<string> UserWhitelist { get; init; } = [];

    public int ItemIconLeftOffset { get; init; } = 60;

    public int ItemTextLeftOffset { get; init; } = 115;

    public int ItemTextRightOffset { get; init; } = 400;
}

public sealed record AutoPickRuntimeStatus(
    bool Enabled,
    bool IsRunning,
    string? LastRecognizedText,
    string LastDecisionReason,
    bool LastIntentSubmitted,
    long? LastFrameSequence,
    DateTimeOffset? UpdatedAtUtc);
