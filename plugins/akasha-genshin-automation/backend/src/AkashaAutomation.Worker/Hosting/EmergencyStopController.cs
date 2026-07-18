namespace AkashaAutomation.Worker.Hosting;

public sealed class EmergencyStopController
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private EmergencyStopSnapshot _snapshot = new(false, null, null);

    public EmergencyStopSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public CancellationToken CancellationToken => _cancellation.Token;

    public bool Trigger(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_gate)
        {
            if (_snapshot.IsActive)
            {
                return false;
            }

            _snapshot = new EmergencyStopSnapshot(true, reason, DateTimeOffset.UtcNow);
        }

        try
        {
            _cancellation.Cancel();
        }
        catch (AggregateException)
        {
            // A failing cancellation callback must not undo the latched safety state.
        }

        return true;
    }
}

public sealed record EmergencyStopSnapshot(
    bool IsActive,
    string? Reason,
    DateTimeOffset? TriggeredAtUtc);
