using System.Threading.Channels;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;

namespace AkashaAutomation.Worker.Hosting;

public sealed record WorkerCommandContext(
    CompanionEnvelope Request,
    WorkerLaunchOptions Options,
    string WorkerVersion,
    DateTimeOffset StartedAtUtc);

public interface IWorkerCommandHandler
{
    ValueTask<CompanionEnvelope> HandleAsync(
        WorkerCommandContext command,
        CancellationToken cancellationToken);
}

public enum WorkerCommandAdmission
{
    Accepted,
    Full,
    Stopping,
}

public sealed class WorkerCommandQueue : IAsyncDisposable
{
    public const int DefaultCapacity = 32;

    private readonly object _stopGate = new();
    private readonly Channel<QueuedCommand> _channel;
    private readonly IWorkerCommandHandler _handler;
    private readonly CancellationToken _emergencyCancellationToken;
    private readonly CancellationTokenSource _stopCancellation = new();
    private readonly Task _processorTask;
    private Task? _stopTask;
    private int _isStopping;

    public WorkerCommandQueue(
        IWorkerCommandHandler handler,
        int capacity = DefaultCapacity,
        CancellationToken emergencyCancellationToken = default)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        Capacity = capacity;
        _emergencyCancellationToken = emergencyCancellationToken;
        _channel = Channel.CreateBounded<QueuedCommand>(
            new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
            });
        _processorTask = ProcessAsync();
    }

    public int Capacity { get; }

    public bool IsStopping => Volatile.Read(ref _isStopping) != 0;

    public async ValueTask<CompanionEnvelope> EnqueueAsync(
        WorkerCommandContext command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ThrowIfStopping();

        var queued = new QueuedCommand(command, cancellationToken);
        try
        {
            await _channel.Writer.WriteAsync(queued, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new WorkerStoppingException();
        }

        return await queued.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public WorkerCommandAdmission TryEnqueue(
        WorkerCommandContext command,
        CancellationToken cancellationToken,
        out Task<CompanionEnvelope>? responseTask)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (IsStopping)
        {
            responseTask = null;
            return WorkerCommandAdmission.Stopping;
        }

        var queued = new QueuedCommand(command, cancellationToken);
        if (_channel.Writer.TryWrite(queued))
        {
            responseTask = queued.Completion.Task;
            return WorkerCommandAdmission.Accepted;
        }

        responseTask = null;
        return IsStopping
            ? WorkerCommandAdmission.Stopping
            : WorkerCommandAdmission.Full;
    }

    public Task StopAsync()
    {
        lock (_stopGate)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _stopCancellation.Dispose();
    }

    private async Task ProcessAsync()
    {
        await foreach (var queued in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (IsStopping)
            {
                queued.Completion.TrySetException(new WorkerStoppingException());
                continue;
            }

            try
            {
                using var commandCancellation = CreateCommandCancellation(queued);
                commandCancellation.Token.ThrowIfCancellationRequested();
                var response = await _handler.HandleAsync(
                    queued.Context,
                    commandCancellation.Token).ConfigureAwait(false);
                queued.Completion.TrySetResult(response);
            }
            catch (OperationCanceledException)
                when (queued.CancellationToken.IsCancellationRequested)
            {
                queued.Completion.TrySetCanceled(queued.CancellationToken);
            }
            catch (OperationCanceledException)
                when (_stopCancellation.IsCancellationRequested)
            {
                queued.Completion.TrySetException(new WorkerStoppingException());
            }
            catch (OperationCanceledException)
                when (_emergencyCancellationToken.IsCancellationRequested)
            {
                queued.Completion.TrySetException(new EmergencyStopException());
            }
            catch (Exception exception)
            {
                queued.Completion.TrySetException(exception);
            }
        }
    }

    private async Task StopCoreAsync()
    {
        Interlocked.Exchange(ref _isStopping, 1);
        _channel.Writer.TryComplete();
        _stopCancellation.Cancel();
        await _processorTask.ConfigureAwait(false);
    }

    private CancellationTokenSource CreateCommandCancellation(QueuedCommand queued)
    {
        if (IsWorkerControlMethod(queued.Context.Request.Method))
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                queued.CancellationToken,
                _stopCancellation.Token);
        }

        return CancellationTokenSource.CreateLinkedTokenSource(
            queued.CancellationToken,
            _stopCancellation.Token,
            _emergencyCancellationToken);
    }

    private static bool IsWorkerControlMethod(string? method) =>
        method?.StartsWith("worker.", StringComparison.Ordinal) is true;

    private void ThrowIfStopping()
    {
        if (IsStopping)
        {
            throw new WorkerStoppingException();
        }
    }

    private sealed record QueuedCommand(
        WorkerCommandContext Context,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource<CompanionEnvelope> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

public sealed class WorkerStoppingException()
    : InvalidOperationException("The Worker is stopping and no longer accepts commands.");

public sealed class EmergencyStopException()
    : InvalidOperationException("Emergency stop is active and the automation command was cancelled.");
