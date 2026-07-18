namespace AkashaAutomation.Worker.Hosting;

public sealed class WorkerStateMachine
{
    private readonly object _gate = new();
    private WorkerState _state = WorkerState.Created;

    public WorkerState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public void TransitionTo(WorkerState nextState)
    {
        lock (_gate)
        {
            if (!IsAllowed(_state, nextState))
            {
                throw new InvalidOperationException(
                    $"Worker state cannot transition from {_state} to {nextState}.");
            }

            _state = nextState;
        }
    }

    private static bool IsAllowed(WorkerState current, WorkerState next) =>
        (current, next) switch
        {
            (WorkerState.Created, WorkerState.Connecting or WorkerState.Stopping) => true,
            (WorkerState.Connecting, WorkerState.Handshaking or WorkerState.Stopping) => true,
            (WorkerState.Handshaking, WorkerState.Ready or WorkerState.Stopping) => true,
            (WorkerState.Ready, WorkerState.Running or WorkerState.Stopping) => true,
            (WorkerState.Running, WorkerState.Ready or WorkerState.Stopping) => true,
            (WorkerState.Stopping, WorkerState.Stopped) => true,
            _ => false,
        };
}
