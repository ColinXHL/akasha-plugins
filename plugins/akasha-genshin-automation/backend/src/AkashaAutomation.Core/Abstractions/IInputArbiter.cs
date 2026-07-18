using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.Core.Abstractions;

public interface IInputArbiter
{
    ValueTask<InputArbitrationResult> SubmitAsync(
        long frameSequence,
        GameContextSnapshot context,
        IReadOnlyCollection<AutomationIntent> intents,
        CancellationToken cancellationToken = default);

    ValueTask EmergencyStopAsync(CancellationToken cancellationToken = default);
}
