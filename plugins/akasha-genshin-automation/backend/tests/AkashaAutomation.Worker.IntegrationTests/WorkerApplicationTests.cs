using System.IO.Pipes;
using System.Text.Json;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkashaAutomation.Worker.IntegrationTests;

public class WorkerApplicationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Session_ShouldHandshakeServeRequestsAndShutdown()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var options = CreateOptions(pipeName);
        var inputArbiter = new TrackingInputArbiter();
        var autoPickController = new AutoPickController(new RootedAssetPathResolver(AppContext.BaseDirectory));
        autoPickController.SetEnabled(true);
        var autoDialogueController = new AutoDialogueController(new RootedAssetPathResolver(AppContext.BaseDirectory));
        autoDialogueController.SetEnabled(true);
        var application = new WorkerApplication(
            parent,
            new WorkerRuntime(autoPickController: autoPickController, autoDialogueController: autoDialogueController),
            NullLogger<WorkerApplication>.Instance,
            inputArbiter: inputArbiter,
            autoPickController: autoPickController,
            autoDialogueController: autoDialogueController);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(options, timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);

        var hello = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal(CompanionProtocol.Hello, hello.Type);
        Assert.Equal(CompanionProtocol.CurrentVersion, hello.ProtocolVersion);
        Assert.Equal(options.Token, hello.Token);
        Assert.Equal(options.ParentProcessId, hello.ParentProcessId);
        Assert.False(string.IsNullOrWhiteSpace(hello.WorkerVersion));

        await AcceptHandshakeAsync(protocol, server, timeout.Token);

        await protocol.WriteAsync(
            server,
            Request("status-1", "worker.getStatus"),
            timeout.Token);
        var statusResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal(CompanionProtocol.Response, statusResponse.Type);
        Assert.Equal("status-1", statusResponse.CorrelationId);
        Assert.Null(statusResponse.Error);
        Assert.Equal("ready", statusResponse.Payload!.Value.GetProperty("state").GetString());
        Assert.False(statusResponse.Payload.Value.GetProperty("realInputEnabled").GetBoolean());
        Assert.Equal(
            "not_found",
            statusResponse.Payload.Value.GetProperty("gameWindow").GetProperty("state").GetString());
        Assert.False(
            statusResponse.Payload.Value.GetProperty("emergencyStop").GetProperty("isActive").GetBoolean());

        var echoPayload = JsonSerializer.SerializeToElement(new { text = "echo-value", number = 7 });
        await protocol.WriteAsync(
            server,
            Request("echo-1", "worker.echo", echoPayload),
            timeout.Token);
        var echoResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal("echo-value", echoResponse.Payload!.Value.GetProperty("text").GetString());
        Assert.Equal(7, echoResponse.Payload.Value.GetProperty("number").GetInt32());

        await protocol.WriteAsync(
            server,
            Request("unknown-1", "worker.notImplemented"),
            timeout.Token);
        var unknownResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal("method_not_found", unknownResponse.Error!.Code);

        await protocol.WriteAsync(
            server,
            Request("emergency-1", "automation.emergencyStop"),
            timeout.Token);
        var emergencyResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.True(emergencyResponse.Payload!.Value.GetProperty("accepted").GetBoolean());
        Assert.True(emergencyResponse.Payload.Value.GetProperty("active").GetBoolean());
        Assert.Equal(1, inputArbiter.EmergencyStopCount);
        Assert.False(autoPickController.Options.Enabled);
        Assert.False(autoDialogueController.Options.Enabled);

        await protocol.WriteAsync(
            server,
            Request("status-2", "worker.getStatus"),
            timeout.Token);
        var stoppedStatusResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.True(
            stoppedStatusResponse.Payload!.Value
                .GetProperty("emergencyStop")
                .GetProperty("isActive")
                .GetBoolean());
        Assert.False(
            stoppedStatusResponse.Payload.Value
                .GetProperty("features")
                .GetProperty("autoPick")
                .GetProperty("isEnabled")
                .GetBoolean());
        Assert.False(
            stoppedStatusResponse.Payload.Value
                .GetProperty("features")
                .GetProperty("autoDialogue")
                .GetProperty("isEnabled")
                .GetBoolean());

        await protocol.WriteAsync(
            server,
            Request("shutdown-1", "worker.shutdown"),
            timeout.Token);
        var shutdownResponse = await protocol.ReadAsync(server, timeout.Token);
        Assert.True(shutdownResponse.Payload!.Value.GetProperty("accepted").GetBoolean());

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Session_ShouldExitWhenHandshakeIsRejected()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var application = new WorkerApplication(parent);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await protocol.WriteAsync(
            server,
            new CompanionEnvelope
            {
                Type = CompanionProtocol.Welcome,
                ProtocolVersion = CompanionProtocol.CurrentVersion,
                Accepted = false,
                Error = new CompanionError("invalid_token", "Session token was rejected."),
            },
            timeout.Token);

        Assert.Equal((int)WorkerExitCode.HandshakeRejected, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Session_ShouldReturnProtocolErrorForMissingHandshakeType()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var application = new WorkerApplication(parent);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await protocol.WriteAsync(
            server,
            new CompanionEnvelope
            {
                Type = null!,
                ProtocolVersion = CompanionProtocol.CurrentVersion,
                Accepted = true,
            },
            timeout.Token);

        Assert.Equal((int)WorkerExitCode.ProtocolError, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Session_ShouldReturnProtocolErrorForMissingRequestType()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var application = new WorkerApplication(parent);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await protocol.WriteAsync(
            server,
            new CompanionEnvelope
            {
                Type = null!,
                CorrelationId = "invalid-1",
                Method = "worker.echo",
            },
            timeout.Token);

        Assert.Equal((int)WorkerExitCode.ProtocolError, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Session_ShouldExitCleanlyWhenPipeDisconnects()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        WorkerRuntime? runtime = null;
        var resource = new EmergencyObservingResource(
            () => runtime!.EmergencyStop.Snapshot.IsActive);
        runtime = new WorkerRuntime([resource]);
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await server.DisposeAsync();

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
        Assert.Equal(WorkerState.Stopped, runtime.StateMachine.State);
        Assert.Equal(
            WorkerStopReason.PipeDisconnected,
            runtime.EmergencyStop.Snapshot.Reason);
        Assert.True(resource.EmergencyWasActiveWhenStopped);
    }

    [Fact]
    public async Task Session_ShouldExitCleanlyWhenParentExits()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var runtime = new WorkerRuntime();
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        parent.SignalExit();

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
        Assert.Equal(WorkerState.Stopped, runtime.StateMachine.State);
        Assert.Equal(
            WorkerStopReason.ParentProcessExited,
            runtime.EmergencyStop.Snapshot.Reason);
    }

    [Fact]
    public async Task Session_ShouldExitCleanlyWhenHostIsCancelled()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var runtime = new WorkerRuntime();
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);
        using var hostCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var workerTask = application.RunAsync(CreateOptions(pipeName), hostCancellation.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        hostCancellation.Cancel();

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
        Assert.Equal(WorkerState.Stopped, runtime.StateMachine.State);
        Assert.Equal(
            WorkerStopReason.HostCancellation,
            runtime.EmergencyStop.Snapshot.Reason);
    }

    [Fact]
    public async Task Session_ShouldFailWhenPipeCannotBeReached()
    {
        using var parent = new FakeParentProcessLifetime();
        var application = new WorkerApplication(
            parent,
            connectionTimeout: TimeSpan.FromMilliseconds(100));

        var result = await application.RunAsync(CreateOptions($"akasha-missing-{Guid.NewGuid():N}"));

        Assert.Equal((int)WorkerExitCode.ConnectionFailed, result);
    }

    [Fact]
    public async Task Session_ShouldExitWhenShutdownFrameIsReceived()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var application = new WorkerApplication(parent);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await protocol.WriteAsync(
            server,
            new CompanionEnvelope { Type = CompanionProtocol.Shutdown },
            timeout.Token);

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task ShutdownResponse_ShouldWaitUntilRuntimeResourcesAreReleased()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var resource = new BlockingRuntimeResource();
        var runtime = new WorkerRuntime([resource]);
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await protocol.WriteAsync(
            server,
            Request("shutdown-after-cleanup", "worker.shutdown"),
            timeout.Token);
        var responseTask = protocol.ReadAsync(server, timeout.Token).AsTask();

        try
        {
            await resource.StopStarted.Task.WaitAsync(timeout.Token);
            Assert.False(responseTask.IsCompleted);
        }
        finally
        {
            resource.ReleaseStop.TrySetResult();
        }

        var response = await responseTask;
        Assert.True(response.Payload!.Value.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, resource.StopCount);
        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task EmergencyStop_ShouldCancelActiveAutomationCommandImmediately()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var handler = new CancellationAwareCommandHandler();
        var runtime = new WorkerRuntime(
            commandHandlerFactory: (_, _) => handler);
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await protocol.WriteAsync(
            server,
            Request("blocking-1", "features.test.block"),
            timeout.Token);
        await handler.CommandStarted.Task.WaitAsync(timeout.Token);

        await protocol.WriteAsync(
            server,
            Request("emergency-active", "automation.emergencyStop"),
            timeout.Token);
        var first = await protocol.ReadAsync(server, timeout.Token);
        var second = await protocol.ReadAsync(server, timeout.Token);
        var responses = new[] { first, second }.ToDictionary(item => item.CorrelationId!);

        Assert.True(handler.WasCancelled);
        Assert.True(
            responses["emergency-active"].Payload!.Value.GetProperty("active").GetBoolean());
        Assert.Equal("emergency_stopped", responses["blocking-1"].Error!.Code);

        await protocol.WriteAsync(
            server,
            Request("shutdown-after-emergency", "worker.shutdown"),
            timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task PipeDisconnect_ShouldCancelActiveAutomationCommandBeforeCleanup()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var handler = new CancellationAwareCommandHandler();
        WorkerRuntime? runtime = null;
        var resource = new EmergencyObservingResource(
            () => runtime!.EmergencyStop.Snapshot.IsActive);
        runtime = new WorkerRuntime(
            [resource],
            commandHandlerFactory: (_, _) => handler);
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);
        await protocol.WriteAsync(
            server,
            Request("blocking-disconnect", "features.test.block"),
            timeout.Token);
        await handler.CommandStarted.Task.WaitAsync(timeout.Token);
        await server.DisposeAsync();

        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
        Assert.True(handler.WasCancelled);
        Assert.True(resource.EmergencyWasActiveWhenStopped);
        Assert.Equal(
            WorkerStopReason.PipeDisconnected,
            runtime.EmergencyStop.Snapshot.Reason);
    }

    [Fact]
    public async Task FullCommandQueue_ShouldRejectOverflowWithoutBlockingEmergencyStop()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        var pipeName = $"akasha-test-{Guid.NewGuid():N}";
        await using var server = CreateServer(pipeName);
        using var parent = new FakeParentProcessLifetime();
        var handler = new CancellationAwareCommandHandler();
        var runtime = new WorkerRuntime(
            commandQueueCapacity: 1,
            commandHandlerFactory: (_, _) => handler);
        var application = new WorkerApplication(
            parent,
            runtime,
            NullLogger<WorkerApplication>.Instance);
        using var timeout = new CancellationTokenSource(TestTimeout);

        var workerTask = application.RunAsync(CreateOptions(pipeName), timeout.Token);
        await server.WaitForConnectionAsync(timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        await AcceptHandshakeAsync(protocol, server, timeout.Token);

        await protocol.WriteAsync(
            server,
            Request("active-capacity", "features.test.block"),
            timeout.Token);
        await handler.CommandStarted.Task.WaitAsync(timeout.Token);
        await protocol.WriteAsync(
            server,
            Request("buffered-capacity", "features.test.buffered"),
            timeout.Token);
        await protocol.WriteAsync(
            server,
            Request("overflow-capacity", "features.test.overflow"),
            timeout.Token);

        var overflow = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal("overflow-capacity", overflow.CorrelationId);
        Assert.Equal("queue_full", overflow.Error!.Code);
        Assert.Equal(1, handler.HandledCount);

        await protocol.WriteAsync(
            server,
            Request("emergency-capacity", "automation.emergencyStop"),
            timeout.Token);
        var remaining = new[]
        {
            await protocol.ReadAsync(server, timeout.Token),
            await protocol.ReadAsync(server, timeout.Token),
            await protocol.ReadAsync(server, timeout.Token),
        }.ToDictionary(item => item.CorrelationId!);

        Assert.True(
            remaining["emergency-capacity"].Payload!.Value.GetProperty("active").GetBoolean());
        Assert.Equal("emergency_stopped", remaining["active-capacity"].Error!.Code);
        Assert.Equal("emergency_stopped", remaining["buffered-capacity"].Error!.Code);
        Assert.Equal(1, handler.HandledCount);

        await protocol.WriteAsync(
            server,
            Request("shutdown-capacity", "worker.shutdown"),
            timeout.Token);
        _ = await protocol.ReadAsync(server, timeout.Token);
        Assert.Equal((int)WorkerExitCode.Success, await workerTask.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task Session_ShouldRejectUnavailableParentBeforeConnecting()
    {
        using var parent = new FakeParentProcessLifetime();
        parent.SignalExit();
        var application = new WorkerApplication(parent);

        var result = await application.RunAsync(CreateOptions($"akasha-unused-{Guid.NewGuid():N}"));

        Assert.Equal((int)WorkerExitCode.ParentProcessUnavailable, result);
    }

    private static NamedPipeServerStream CreateServer(string pipeName) =>
        new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

    private static WorkerLaunchOptions CreateOptions(string pipeName) =>
        new(
            pipeName,
            "0123456789abcdef0123456789abcdef",
            Environment.ProcessId,
            CompanionProtocol.CurrentVersion);

    private static CompanionEnvelope Request(
        string correlationId,
        string method,
        JsonElement? payload = null) =>
        new()
        {
            Type = CompanionProtocol.Request,
            CorrelationId = correlationId,
            Method = method,
            Payload = payload,
        };

    private static ValueTask AcceptHandshakeAsync(
        LengthPrefixedJsonProtocol protocol,
        Stream stream,
        CancellationToken cancellationToken) =>
        protocol.WriteAsync(
            stream,
            new CompanionEnvelope
            {
                Type = CompanionProtocol.Welcome,
                ProtocolVersion = CompanionProtocol.CurrentVersion,
                Accepted = true,
            },
            cancellationToken);

    private sealed class FakeParentProcessLifetime : IParentProcessLifetime
    {
        private readonly TaskCompletionSource _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsAlive { get; private set; } = true;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.Task.WaitAsync(cancellationToken);

        public void SignalExit()
        {
            IsAlive = false;
            _exit.TrySetResult();
        }

        public void Dispose() => _exit.TrySetCanceled();
    }

    private sealed class EmergencyObservingResource(Func<bool> isEmergencyActive)
        : IWorkerRuntimeResource
    {
        public bool EmergencyWasActiveWhenStopped { get; private set; }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EmergencyWasActiveWhenStopped = isEmergencyActive();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRuntimeResource : IWorkerRuntimeResource
    {
        public TaskCompletionSource StopStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StopCount { get; private set; }

        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            StopStarted.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CancellationAwareCommandHandler : IWorkerCommandHandler
    {
        private int _handledCount;

        public TaskCompletionSource CommandStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool WasCancelled { get; private set; }

        public int HandledCount => Volatile.Read(ref _handledCount);

        public async ValueTask<CompanionEnvelope> HandleAsync(
            WorkerCommandContext command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _handledCount);
            CommandStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }

            throw new InvalidOperationException("The blocking command unexpectedly completed.");
        }
    }

    private sealed class TrackingInputArbiter : IInputArbiter
    {
        private int _emergencyStopCount;

        public int EmergencyStopCount => Volatile.Read(ref _emergencyStopCount);

        public ValueTask<InputArbitrationResult> SubmitAsync(
            long frameSequence,
            GameContextSnapshot context,
            IReadOnlyCollection<AutomationIntent> intents,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new InputArbitrationResult(false, "test"));

        public ValueTask EmergencyStopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _emergencyStopCount);
            return ValueTask.CompletedTask;
        }
    }
}
