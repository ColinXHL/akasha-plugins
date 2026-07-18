using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkashaAutomation.Worker.Hosting;

public static class WorkerStopReason
{
    public const string ShutdownRequested = "shutdown_requested";
    public const string ShutdownFrame = "shutdown_frame";
    public const string CompanionEmergencyStop = "companion_emergency_stop";
    public const string PipeDisconnected = "pipe_disconnected";
    public const string ParentProcessExited = "parent_process_exited";
    public const string HostCancellation = "host_cancellation";
    public const string ConnectionFailed = "connection_failed";
    public const string HandshakeRejected = "handshake_rejected";
    public const string ProtocolError = "protocol_error";
    public const string UnexpectedError = "unexpected_error";
}

public sealed class WorkerShutdownCoordinator
{
    private readonly object _gate = new();
    private readonly EmergencyStopController _emergencyStop;
    private readonly WorkerStateMachine _stateMachine;
    private readonly WorkerCommandQueue _commandQueue;
    private readonly IReadOnlyList<IWorkerRuntimeResource> _resources;
    private readonly WorkerStatusProvider _statusProvider;
    private readonly ILogger<WorkerShutdownCoordinator> _logger;
    private Task? _shutdownTask;

    public WorkerShutdownCoordinator(
        EmergencyStopController emergencyStop,
        WorkerStateMachine stateMachine,
        WorkerCommandQueue commandQueue,
        WorkerStatusProvider statusProvider,
        IEnumerable<IWorkerRuntimeResource>? resources = null,
        ILogger<WorkerShutdownCoordinator>? logger = null)
    {
        _emergencyStop = emergencyStop ?? throw new ArgumentNullException(nameof(emergencyStop));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _commandQueue = commandQueue ?? throw new ArgumentNullException(nameof(commandQueue));
        _statusProvider = statusProvider ?? throw new ArgumentNullException(nameof(statusProvider));
        _resources = resources?.ToArray() ?? [];
        _logger = logger ?? NullLogger<WorkerShutdownCoordinator>.Instance;
    }

    public Task StopAsync(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_gate)
        {
            return _shutdownTask ??= StopCoreAsync(reason);
        }
    }

    private async Task StopCoreAsync(string reason)
    {
        _emergencyStop.Trigger(reason);
        TransitionToStopping();

        _logger.LogInformation("Worker shutdown started. Reason: {Reason}", reason);
        await StopSafelyAsync(
            () => new ValueTask(_commandQueue.StopAsync()),
            "command_queue").ConfigureAwait(false);

        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            var resource = _resources[index];
            await StopSafelyAsync(
                () => resource.StopAsync(CancellationToken.None),
                resource.GetType().Name).ConfigureAwait(false);
        }

        if (_stateMachine.State == WorkerState.Stopping)
        {
            _stateMachine.TransitionTo(WorkerState.Stopped);
        }

        _logger.LogInformation("Worker shutdown completed. Reason: {Reason}", reason);
    }

    private void TransitionToStopping()
    {
        var current = _stateMachine.State;
        if (current is not WorkerState.Stopping and not WorkerState.Stopped)
        {
            _stateMachine.TransitionTo(WorkerState.Stopping);
        }
    }

    private async ValueTask StopSafelyAsync(
        Func<ValueTask> stop,
        string component)
    {
        try
        {
            await stop().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _statusProvider.ReportError(
                "shutdown_cleanup_failed",
                $"Failed to stop {component}.");
            _logger.LogError(
                exception,
                "Failed to stop Worker component {Component}",
                component);
        }
    }
}
