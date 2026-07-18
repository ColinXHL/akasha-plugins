using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.Core.Scheduling;

public interface IAutomationFeature
{
    string Id { get; }

    int Priority { get; }

    ValueTask<FeatureDecision> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default);
}
