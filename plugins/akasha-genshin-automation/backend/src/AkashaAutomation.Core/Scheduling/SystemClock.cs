using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Core.Scheduling;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public async ValueTask DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default) =>
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
}
