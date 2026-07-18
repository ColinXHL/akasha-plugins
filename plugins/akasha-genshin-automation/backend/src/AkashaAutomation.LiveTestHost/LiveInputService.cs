using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.LiveTestHost;

public sealed class LiveInputService(
    IInputService inner) : IInputService
{
    private long _executedGroups;

    public long ExecutedGroups => Interlocked.Read(ref _executedGroups);

    public async ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        await inner.ExecuteAsync(actions, context, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _executedGroups);
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default) =>
        inner.ReleaseAllAsync(cancellationToken);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
