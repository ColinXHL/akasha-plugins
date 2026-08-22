using AkashaAutomation.BetterGiPort.Upstream.AutoPick;
using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Features.AutoPick;

public interface IAutoPickController : IAutomationFeatureControl
{
    AutoPickOptions Options { get; }

    AutoPickRuntimeStatus Status { get; }

    AutoPickConfiguration Snapshot { get; }

    void SetOptions(AutoPickOptions options);

    void Report(long frameSequence, string? text, string reason, bool intentSubmitted, DateTimeOffset timestampUtc);
}

public sealed record AutoPickConfiguration(AutoPickOptions Options, BetterGiAutoPickLists Lists);
