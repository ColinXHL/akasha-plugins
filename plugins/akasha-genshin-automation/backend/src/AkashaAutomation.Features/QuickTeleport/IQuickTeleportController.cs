using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Features.QuickTeleport;

public interface IQuickTeleportController : IAutomationFeatureControl
{
    event Action? Disabled;

    QuickTeleportOptions Options { get; }

    QuickTeleportRuntimeStatus Status { get; }

    void SetOptions(QuickTeleportOptions options);

    void Report(
        long frameSequence,
        string state,
        string? candidateText,
        string reason,
        bool intentSubmitted,
        DateTimeOffset timestampUtc);
}
