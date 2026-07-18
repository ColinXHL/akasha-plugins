using AkashaAutomation.BetterGiPort.Upstream.AutoPick;

namespace AkashaAutomation.Features.AutoPick;

public interface IAutoPickController
{
    AutoPickOptions Options { get; }

    AutoPickRuntimeStatus Status { get; }

    AutoPickConfiguration Snapshot { get; }

    void SetEnabled(bool enabled);

    void SetOptions(AutoPickOptions options);

    void Report(long frameSequence, string? text, string reason, bool intentSubmitted, DateTimeOffset timestampUtc);
}

public sealed record AutoPickConfiguration(AutoPickOptions Options, BetterGiAutoPickLists Lists);
