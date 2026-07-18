using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Input;

public sealed class RecordingInputService : IInputService
{
    private readonly object _gate = new();
    private readonly List<RecordedInputActionGroup> _recordings = [];
    private bool _disposed;

    public IReadOnlyList<RecordedInputActionGroup> Recordings
    {
        get
        {
            lock (_gate)
            {
                return _recordings.ToArray();
            }
        }
    }

    public ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _recordings.Add(new RecordedInputActionGroup(actions, context));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}

public sealed record RecordedInputActionGroup(
    InputActionGroup Actions,
    GameContextSnapshot Context);
