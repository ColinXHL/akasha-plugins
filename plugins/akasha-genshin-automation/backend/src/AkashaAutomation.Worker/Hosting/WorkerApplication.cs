using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkashaAutomation.Worker.Hosting;

public sealed class WorkerApplication
{
    private static readonly string WorkerVersion =
        typeof(WorkerApplication).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(WorkerApplication).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private readonly IParentProcessLifetime _parentProcess;
    private readonly WorkerRuntime _runtime;
    private readonly ILogger<WorkerApplication> _logger;
    private readonly LengthPrefixedJsonProtocol _protocol;
    private readonly TimeSpan _connectionTimeout;
    private readonly TimeSpan _handshakeTimeout;
    private readonly DateTimeOffset _startedAtUtc;
    private readonly IInputArbiter? _inputArbiter;
    private readonly IAutoPickController? _autoPickController;
    private readonly IAutoDialogueController? _autoDialogueController;

    public WorkerApplication(
        IParentProcessLifetime parentProcess,
        LengthPrefixedJsonProtocol? protocol = null,
        TimeSpan? connectionTimeout = null,
        TimeSpan? handshakeTimeout = null)
        : this(
            parentProcess,
            new WorkerRuntime(),
            NullLogger<WorkerApplication>.Instance,
            protocol,
            connectionTimeout,
            handshakeTimeout,
            inputArbiter: null,
            autoPickController: null,
            autoDialogueController: null)
    {
    }

    public WorkerApplication(
        IParentProcessLifetime parentProcess,
        WorkerRuntime runtime,
        ILogger<WorkerApplication> logger,
        LengthPrefixedJsonProtocol? protocol = null,
        TimeSpan? connectionTimeout = null,
        TimeSpan? handshakeTimeout = null,
        IInputArbiter? inputArbiter = null,
        IAutoPickController? autoPickController = null,
        IAutoDialogueController? autoDialogueController = null)
    {
        _parentProcess = parentProcess ?? throw new ArgumentNullException(nameof(parentProcess));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _protocol = protocol ?? new LengthPrefixedJsonProtocol();
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(10);
        _handshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(10);
        _startedAtUtc = DateTimeOffset.UtcNow;
        _inputArbiter = inputArbiter;
        _autoPickController = autoPickController;
        _autoDialogueController = autoDialogueController;
    }

    public async Task<int> RunAsync(
        WorkerLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stopReason = WorkerStopReason.HostCancellation;

        try
        {
            if (!_parentProcess.IsAlive)
            {
                stopReason = WorkerStopReason.ParentProcessExited;
                _runtime.StatusProvider.ReportError(
                    "parent_process_unavailable",
                    "The parent process is not available.");
                return (int)WorkerExitCode.ParentProcessUnavailable;
            }

            _runtime.StateMachine.TransitionTo(WorkerState.Connecting);
            _logger.LogInformation(
                "Worker session is connecting to the companion pipe for parent process {ParentProcessId}",
                options.ParentProcessId);

            using var sessionCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var sessionTask = RunPipeSessionAsync(options, sessionCancellation.Token);
            var parentExitTask = _parentProcess.WaitForExitAsync(sessionCancellation.Token);
            var completedTask = await Task.WhenAny(sessionTask, parentExitTask).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (completedTask == parentExitTask)
            {
                stopReason = WorkerStopReason.ParentProcessExited;
                _runtime.EmergencyStop.Trigger(stopReason);
                sessionCancellation.Cancel();
                await IgnoreCancellationAsync(sessionTask).ConfigureAwait(false);
                return (int)WorkerExitCode.Success;
            }

            var result = await sessionTask.ConfigureAwait(false);
            stopReason = result.StopReason;
            sessionCancellation.Cancel();
            await IgnoreCancellationAsync(parentExitTask).ConfigureAwait(false);
            return (int)result.ExitCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopReason = WorkerStopReason.HostCancellation;
            _runtime.EmergencyStop.Trigger(stopReason);
            return (int)WorkerExitCode.Success;
        }
        catch (Exception exception)
        {
            stopReason = WorkerStopReason.UnexpectedError;
            _runtime.StatusProvider.ReportError(
                "unexpected_error",
                "The Worker encountered an unexpected error.");
            _logger.LogError(exception, "Worker session failed unexpectedly");
            return (int)WorkerExitCode.UnexpectedError;
        }
        finally
        {
            await _runtime.Shutdown.StopAsync(stopReason).ConfigureAwait(false);
        }
    }

    private async Task<SessionResult> RunPipeSessionAsync(
        WorkerLaunchOptions options,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using (var connectionCancellation =
               CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            connectionCancellation.CancelAfter(_connectionTimeout);
            try
            {
                await pipe.ConnectAsync(connectionCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ConnectionFailed("The companion pipe connection timed out.");
            }
            catch (IOException)
            {
                return ConnectionFailed("The companion pipe could not be reached.");
            }
        }

        _runtime.StateMachine.TransitionTo(WorkerState.Handshaking);

        try
        {
            var handshakeResult =
                await PerformHandshakeAsync(pipe, options, cancellationToken).ConfigureAwait(false);
            if (handshakeResult != WorkerExitCode.Success)
            {
                _runtime.StatusProvider.ReportError(
                    "handshake_rejected",
                    "The companion handshake was rejected or timed out.");
                return new SessionResult(
                    handshakeResult,
                    WorkerStopReason.HandshakeRejected);
            }

            _runtime.StateMachine.TransitionTo(WorkerState.Ready);
            _logger.LogInformation(
                "Worker session is ready without an attached game window");
            return await ProcessRequestsAsync(pipe, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (EndOfStreamException)
        {
            return PipeDisconnected();
        }
        catch (IOException)
        {
            return PipeDisconnected();
        }
        catch (JsonException)
        {
            return ProtocolError();
        }
        catch (InvalidDataException)
        {
            return ProtocolError();
        }
        catch (WorkerStoppingException)
        {
            return new SessionResult(
                WorkerExitCode.Success,
                WorkerStopReason.ShutdownRequested);
        }
    }

    private async Task<WorkerExitCode> PerformHandshakeAsync(
        Stream pipe,
        WorkerLaunchOptions options,
        CancellationToken cancellationToken)
    {
        using var handshakeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeCancellation.CancelAfter(_handshakeTimeout);

        try
        {
            await _protocol.WriteAsync(
                pipe,
                new CompanionEnvelope
                {
                    Type = CompanionProtocol.Hello,
                    ProtocolVersion = options.ProtocolVersion,
                    Token = options.Token,
                    WorkerVersion = WorkerVersion,
                    ParentProcessId = options.ParentProcessId,
                },
                handshakeCancellation.Token).ConfigureAwait(false);

            var welcome =
                await _protocol.ReadAsync(pipe, handshakeCancellation.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(welcome.Type) ||
                !string.Equals(welcome.Type, CompanionProtocol.Welcome, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Companion handshake response type is invalid.");
            }

            if (welcome.Accepted is not true ||
                welcome.ProtocolVersion != CompanionProtocol.CurrentVersion)
            {
                return WorkerExitCode.HandshakeRejected;
            }

            return WorkerExitCode.Success;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return WorkerExitCode.HandshakeRejected;
        }
    }

    private async Task<SessionResult> ProcessRequestsAsync(
        Stream pipe,
        WorkerLaunchOptions options,
        CancellationToken cancellationToken)
    {
        using var responseWriteGate = new SemaphoreSlim(1, 1);
        var outstandingRequests = new HashSet<Task>();

        try
        {
            while (true)
            {
                var request = await _protocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (string.Equals(request.Type, CompanionProtocol.Shutdown, StringComparison.Ordinal))
                {
                    await StopAndAwaitOutstandingRequestsAsync(
                        WorkerStopReason.ShutdownFrame,
                        outstandingRequests).ConfigureAwait(false);
                    return new SessionResult(
                        WorkerExitCode.Success,
                        WorkerStopReason.ShutdownFrame);
                }

                if (!string.Equals(request.Type, CompanionProtocol.Request, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(request.CorrelationId) ||
                    string.IsNullOrWhiteSpace(request.Method))
                {
                    throw new InvalidDataException(
                        "Companion requests require type, correlationId and method.");
                }

                RemoveCompletedRequests(outstandingRequests);

                if (request.Method.Equals("automation.emergencyStop", StringComparison.Ordinal))
                {
                    _runtime.EmergencyStop.Trigger(WorkerStopReason.CompanionEmergencyStop);
                    _autoPickController?.SetEnabled(false);
                    _autoDialogueController?.SetEnabled(false);
                    if (_inputArbiter is not null)
                    {
                        await _inputArbiter.EmergencyStopAsync(cancellationToken).ConfigureAwait(false);
                    }

                    await WriteResponseAsync(
                        pipe,
                        responseWriteGate,
                        SuccessResponse(
                            request,
                            JsonSerializer.SerializeToElement(
                                new { accepted = true, active = true },
                                CompanionProtocol.JsonOptions)),
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (request.Method.Equals("worker.shutdown", StringComparison.Ordinal))
                {
                    await StopAndAwaitOutstandingRequestsAsync(
                        WorkerStopReason.ShutdownRequested,
                        outstandingRequests).ConfigureAwait(false);
                    await WriteResponseAsync(
                        pipe,
                        responseWriteGate,
                        SuccessResponse(
                            request,
                            JsonSerializer.SerializeToElement(
                                new { accepted = true },
                                CompanionProtocol.JsonOptions)),
                        cancellationToken).ConfigureAwait(false);
                    return new SessionResult(
                        WorkerExitCode.Success,
                        WorkerStopReason.ShutdownRequested);
                }

                var command = new WorkerCommandContext(
                    request,
                    options,
                    WorkerVersion,
                    _startedAtUtc);
                var admission = _runtime.CommandQueue.TryEnqueue(
                    command,
                    cancellationToken,
                    out var responseTask);
                if (admission == WorkerCommandAdmission.Accepted)
                {
                    outstandingRequests.Add(
                        ProcessAdmittedRequestAsync(
                            pipe,
                            responseWriteGate,
                            request,
                            responseTask!,
                            cancellationToken));
                    continue;
                }

                var error = admission == WorkerCommandAdmission.Full
                    ? ErrorResponse(
                        request,
                        "queue_full",
                        "The Worker command queue is full. Try again later.")
                    : ErrorResponse(
                        request,
                        "worker_stopping",
                        "The Worker is stopping and no longer accepts commands.");
                await WriteResponseAsync(
                    pipe,
                    responseWriteGate,
                    error,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (EndOfStreamException)
        {
            await StopAndAwaitOutstandingRequestsAsync(
                WorkerStopReason.PipeDisconnected,
                outstandingRequests).ConfigureAwait(false);
            return PipeDisconnected();
        }
        catch (IOException)
        {
            await StopAndAwaitOutstandingRequestsAsync(
                WorkerStopReason.PipeDisconnected,
                outstandingRequests).ConfigureAwait(false);
            return PipeDisconnected();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var reason = _runtime.EmergencyStop.Snapshot.Reason
                         ?? WorkerStopReason.HostCancellation;
            await StopAndAwaitOutstandingRequestsAsync(
                reason,
                outstandingRequests).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await StopAndAwaitOutstandingRequestsAsync(
                WorkerStopReason.ProtocolError,
                outstandingRequests).ConfigureAwait(false);
            throw;
        }
    }

    private async Task ProcessAdmittedRequestAsync(
        Stream pipe,
        SemaphoreSlim responseWriteGate,
        CompanionEnvelope request,
        Task<CompanionEnvelope> responseTask,
        CancellationToken cancellationToken)
    {
        CompanionEnvelope response;
        try
        {
            response = await responseTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (EmergencyStopException)
        {
            response = ErrorResponse(
                request,
                "emergency_stopped",
                "Emergency stop cancelled the automation command.");
        }
        catch (WorkerStoppingException)
        {
            response = ErrorResponse(
                request,
                "worker_stopping",
                "The Worker is stopping and no longer accepts commands.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _runtime.StatusProvider.ReportError(
                "command_failed",
                $"Companion method '{request.Method}' failed.");
            _logger.LogError(
                exception,
                "Companion method {Method} failed",
                request.Method);
            response = ErrorResponse(
                request,
                "command_failed",
                "The Worker could not complete the command.");
        }

        try
        {
            await WriteResponseAsync(
                pipe,
                responseWriteGate,
                response,
                cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException)
        {
            _runtime.EmergencyStop.Trigger(WorkerStopReason.PipeDisconnected);
        }
        catch (IOException)
        {
            _runtime.EmergencyStop.Trigger(WorkerStopReason.PipeDisconnected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StopAndAwaitOutstandingRequestsAsync(
        string reason,
        IEnumerable<Task> outstandingRequests)
    {
        _runtime.EmergencyStop.Trigger(reason);
        await _runtime.Shutdown.StopAsync(reason).ConfigureAwait(false);
        await Task.WhenAll(outstandingRequests).ConfigureAwait(false);
    }

    private async ValueTask WriteResponseAsync(
        Stream pipe,
        SemaphoreSlim responseWriteGate,
        CompanionEnvelope response,
        CancellationToken cancellationToken)
    {
        await responseWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _protocol.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            responseWriteGate.Release();
        }
    }

    private static void RemoveCompletedRequests(ISet<Task> outstandingRequests)
    {
        foreach (var completed in outstandingRequests.Where(task => task.IsCompleted).ToArray())
        {
            outstandingRequests.Remove(completed);
        }
    }

    private static CompanionEnvelope SuccessResponse(
        CompanionEnvelope request,
        JsonElement? payload) =>
        new()
        {
            Type = CompanionProtocol.Response,
            CorrelationId = request.CorrelationId,
            Payload = payload,
        };

    private static CompanionEnvelope ErrorResponse(
        CompanionEnvelope request,
        string code,
        string message) =>
        new()
        {
            Type = CompanionProtocol.Response,
            CorrelationId = request.CorrelationId,
            Error = new CompanionError(code, message),
        };

    private SessionResult ConnectionFailed(string message)
    {
        _runtime.StatusProvider.ReportError("connection_failed", message);
        _logger.LogWarning("{Message}", message);
        return new SessionResult(
            WorkerExitCode.ConnectionFailed,
            WorkerStopReason.ConnectionFailed);
    }

    private SessionResult PipeDisconnected()
    {
        _runtime.EmergencyStop.Trigger(WorkerStopReason.PipeDisconnected);
        _logger.LogInformation("Companion pipe disconnected");
        return new SessionResult(
            WorkerExitCode.Success,
            WorkerStopReason.PipeDisconnected);
    }

    private SessionResult ProtocolError()
    {
        _runtime.StatusProvider.ReportError(
            "protocol_error",
            "The companion sent invalid protocol data.");
        return new SessionResult(
            WorkerExitCode.ProtocolError,
            WorkerStopReason.ProtocolError);
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record SessionResult(
        WorkerExitCode ExitCode,
        string StopReason);
}
