using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;

namespace AkashaAutomation.Features.AutoDialogue;

public interface IAutoDialogueController
{
    event Action? Disabled;

    AutoDialogueOptions Options { get; }

    AutoDialogueRuntimeStatus Status { get; }

    AutoDialogueConfiguration Snapshot { get; }

    void SetEnabled(bool enabled);

    void SetOptions(AutoDialogueOptions options);

    void Report(
        long frameSequence,
        string uiCategory,
        IReadOnlyList<string> recognizedOptions,
        string reason,
        bool intentSubmitted,
        bool voiceWaitActive,
        bool voiceWaitFallback,
        DateTimeOffset timestampUtc);
}

public sealed record AutoDialogueConfiguration(
    AutoDialogueOptions Options,
    DialogueOptionRuleOptions RuleOptions,
    BetterGiAutoSkipLists Lists,
    IReadOnlyDictionary<string, IReadOnlyList<string>> HangoutOptions);
