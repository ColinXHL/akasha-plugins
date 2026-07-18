using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Core.Scheduling;

public sealed class FakeClock : IClock
{
    private readonly object _gate = new();
    private readonly List<DelayRegistration> _delays = [];
    private DateTimeOffset _utcNow;

    public FakeClock(DateTimeOffset? initialUtc = null)
    {
        _utcNow = initialUtc ?? DateTimeOffset.UnixEpoch;
    }

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        if (delay == TimeSpan.Zero)
        {
            return ValueTask.CompletedTask;
        }

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetCanceled(),
                completion);
            _delays.Add(new DelayRegistration(_utcNow + delay, completion, registration));
            return new ValueTask(completion.Task);
        }
    }

    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(amount, TimeSpan.Zero);
        List<DelayRegistration> ready;
        lock (_gate)
        {
            _utcNow += amount;
            ready = _delays.Where(delay => delay.DueUtc <= _utcNow).ToList();
            _delays.RemoveAll(delay => delay.DueUtc <= _utcNow);
        }

        foreach (var delay in ready)
        {
            delay.CancellationRegistration.Dispose();
            delay.Completion.TrySetResult();
        }
    }

    private sealed record DelayRegistration(
        DateTimeOffset DueUtc,
        TaskCompletionSource Completion,
        CancellationTokenRegistration CancellationRegistration);
}
