using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Worker.Hosting;

public sealed class AutomationInputRuntimeResource : IWorkerRuntimeResource
{
    private readonly IInputArbiter _inputArbiter;

    public AutomationInputRuntimeResource(IInputArbiter inputArbiter)
    {
        _inputArbiter = inputArbiter;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        _inputArbiter.EmergencyStopAsync(cancellationToken);
}
