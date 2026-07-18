using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.DevHost;

internal sealed class ObserveOnlyInputService : IInputService
{
    private long _observedGroups;

    public long ObservedGroups => Interlocked.Read(ref _observedGroups);

    public ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _observedGroups);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class NullDiagnosticsSink : IDiagnosticsSink
{
    public void Write(AkashaAutomation.Core.Diagnostics.DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
    }
}
