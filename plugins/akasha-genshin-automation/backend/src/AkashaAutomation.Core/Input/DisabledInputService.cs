using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Input;

public sealed class DisabledInputService : IInputService
{
    public ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new InvalidOperationException("Real input is disabled in this runtime."));

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
