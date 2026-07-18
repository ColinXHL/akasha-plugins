using System.Text.Json;
using System.Diagnostics;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;
using AkashaAutomation.Worker.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkashaAutomation.Worker.IntegrationTests;

public class WorkerInfrastructureTests
{
    [Fact]
    public async Task ParentProcessLifetime_ShouldObserveRealProcessExit()
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec")!;
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = commandInterpreter,
            Arguments = "/d /c ping 127.0.0.1 -n 60 > nul",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        Assert.NotNull(process);
        Assert.True(ProcessParentProcessLifetime.TryCreate(process!.Id, out var lifetime));
        using (lifetime)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            Assert.True(lifetime.IsAlive);

            process.Kill(entireProcessTree: true);
            await lifetime.WaitForExitAsync(timeout.Token);

            Assert.False(lifetime.IsAlive);
        }
    }

    [Fact]
    public void StateMachine_ShouldAllowTheLifecycleSequence()
    {
        var stateMachine = new WorkerStateMachine();

        stateMachine.TransitionTo(WorkerState.Connecting);
        stateMachine.TransitionTo(WorkerState.Handshaking);
        stateMachine.TransitionTo(WorkerState.Ready);
        stateMachine.TransitionTo(WorkerState.Running);
        stateMachine.TransitionTo(WorkerState.Ready);
        stateMachine.TransitionTo(WorkerState.Stopping);
        stateMachine.TransitionTo(WorkerState.Stopped);

        Assert.Equal(WorkerState.Stopped, stateMachine.State);
    }

    [Theory]
    [InlineData(WorkerState.Created, WorkerState.Ready)]
    [InlineData(WorkerState.Connecting, WorkerState.Running)]
    [InlineData(WorkerState.Handshaking, WorkerState.Running)]
    [InlineData(WorkerState.Ready, WorkerState.Handshaking)]
    [InlineData(WorkerState.Stopping, WorkerState.Ready)]
    [InlineData(WorkerState.Stopped, WorkerState.Connecting)]
    public void StateMachine_ShouldRejectIllegalTransitions(
        WorkerState initial,
        WorkerState invalidNext)
    {
        var stateMachine = CreateStateMachineAt(initial);

        var exception = Assert.Throws<InvalidOperationException>(
            () => stateMachine.TransitionTo(invalidNext));

        Assert.Contains(initial.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(invalidNext.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(initial, stateMachine.State);
    }

    [Fact]
    public async Task CommandQueue_ShouldApplyBackpressureAndRejectCommandsAfterStop()
    {
        var handler = new BlockingCommandHandler();
        await using var queue = new WorkerCommandQueue(handler, capacity: 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = queue.EnqueueAsync(CreateCommand("one"), timeout.Token).AsTask();
        await handler.FirstCommandStarted.Task.WaitAsync(timeout.Token);
        var second = queue.EnqueueAsync(CreateCommand("two"), timeout.Token).AsTask();
        var third = queue.EnqueueAsync(CreateCommand("three"), timeout.Token).AsTask();

        await Task.Delay(50, timeout.Token);
        Assert.False(third.IsCompleted);

        handler.ReleaseFirstCommand.TrySetResult();
        await Task.WhenAll(first, second, third).WaitAsync(timeout.Token);
        await queue.StopAsync().WaitAsync(timeout.Token);

        Assert.Equal(3, handler.HandledCount);
        Assert.True(queue.IsStopping);
        await Assert.ThrowsAsync<WorkerStoppingException>(
            () => queue.EnqueueAsync(CreateCommand("late"), timeout.Token).AsTask());
    }

    [Fact]
    public async Task CommandQueue_StopShouldCancelActiveAndDiscardBufferedCommands()
    {
        var handler = new BlockingCommandHandler();
        await using var queue = new WorkerCommandQueue(handler, capacity: 2);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var active = queue.EnqueueAsync(CreateCommand("active"), timeout.Token).AsTask();
        await handler.FirstCommandStarted.Task.WaitAsync(timeout.Token);
        var buffered = queue.EnqueueAsync(CreateCommand("buffered"), timeout.Token).AsTask();

        await queue.StopAsync().WaitAsync(timeout.Token);

        await Assert.ThrowsAsync<WorkerStoppingException>(() => active);
        await Assert.ThrowsAsync<WorkerStoppingException>(() => buffered);
        Assert.Equal(1, handler.HandledCount);
    }

    [Fact]
    public async Task Shutdown_ShouldBeIdempotentAndTriggerEmergencyStopBeforeResources()
    {
        var stateMachine = new WorkerStateMachine();
        stateMachine.TransitionTo(WorkerState.Connecting);
        stateMachine.TransitionTo(WorkerState.Handshaking);
        stateMachine.TransitionTo(WorkerState.Ready);
        var emergencyStop = new EmergencyStopController();
        var statusProvider = new WorkerStatusProvider(stateMachine, emergencyStop);
        await using var queue = new WorkerCommandQueue(new PassthroughCommandHandler());
        var stopOrder = new List<string>();
        var firstResource = new ObservingResource("first", emergencyStop, stopOrder);
        var secondResource = new ObservingResource("second", emergencyStop, stopOrder);
        var coordinator = new WorkerShutdownCoordinator(
            emergencyStop,
            stateMachine,
            queue,
            statusProvider,
            [firstResource, secondResource]);

        var first = coordinator.StopAsync(WorkerStopReason.PipeDisconnected);
        var callers = Enumerable.Range(0, 16)
            .Select(_ => coordinator.StopAsync(WorkerStopReason.ParentProcessExited))
            .ToArray();

        Assert.All(callers, task => Assert.Same(first, task));
        await Task.WhenAll(callers.Append(first));

        Assert.Equal(WorkerState.Stopped, stateMachine.State);
        Assert.Equal(1, firstResource.StopCount);
        Assert.Equal(1, secondResource.StopCount);
        Assert.True(firstResource.EmergencyWasActiveWhenStopped);
        Assert.True(secondResource.EmergencyWasActiveWhenStopped);
        Assert.Equal(["second", "first"], stopOrder);
        Assert.Equal(
            WorkerStopReason.PipeDisconnected,
            emergencyStop.Snapshot.Reason);
    }

    [Fact]
    public async Task Shutdown_ShouldShareCompletionWhileResourceStopIsInFlight()
    {
        var stateMachine = CreateStateMachineAt(WorkerState.Ready);
        var emergencyStop = new EmergencyStopController();
        var statusProvider = new WorkerStatusProvider(stateMachine, emergencyStop);
        await using var queue = new WorkerCommandQueue(new PassthroughCommandHandler());
        var resource = new BlockingResource();
        var coordinator = new WorkerShutdownCoordinator(
            emergencyStop,
            stateMachine,
            queue,
            statusProvider,
            [resource]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var first = coordinator.StopAsync(WorkerStopReason.ShutdownRequested);
        await resource.StopStarted.Task.WaitAsync(timeout.Token);
        var callers = await Task.WhenAll(
            Enumerable.Range(0, 16)
                .Select(_ => Task.Run<Task>(
                    () => coordinator.StopAsync(WorkerStopReason.ParentProcessExited),
                    timeout.Token)));

        Assert.All(callers, task => Assert.Same(first, task));
        Assert.False(first.IsCompleted);
        resource.ReleaseStop.TrySetResult();
        await Task.WhenAll(callers.Append(first)).WaitAsync(timeout.Token);
        Assert.Equal(1, resource.StopCount);
    }

    [Fact]
    public void Status_ShouldDescribeReadyWorkerWithoutGameWindowOrInput()
    {
        var stateMachine = CreateStateMachineAt(WorkerState.Ready);
        var emergencyStop = new EmergencyStopController();
        var provider = new WorkerStatusProvider(stateMachine, emergencyStop);
        var options = CreateOptions();

        var status = provider.GetStatus(
            options,
            "test-version",
            DateTimeOffset.UnixEpoch);

        Assert.Equal("ready", status.State);
        Assert.False(status.RealInputEnabled);
        Assert.Equal("not_found", status.GameWindow.State);
        Assert.False(status.GameWindow.IsAvailable);
        Assert.Equal("not_started", status.Capture.State);
        Assert.Equal("not_started", status.Ocr.State);
        Assert.False(status.Features.AutoPick.IsEnabled);
        Assert.False(status.Features.AutoDialogue.IsEnabled);
        Assert.False(status.EmergencyStop.IsActive);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void RollingLogger_ShouldWriteStructuredJsonAndLimitArchives()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"akasha-log-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "worker.log");

        try
        {
            using var provider = new JsonRollingFileLoggerProvider(
                new JsonRollingFileLoggerOptions(
                    logPath,
                    MaximumFileBytes: 300,
                    RetainedFileCount: 2));
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = factory.CreateLogger("phase2-test");

            for (var index = 0; index < 12; index++)
            {
                logger.LogInformation(
                    "Processed command {CommandIndex} with a structured value",
                    index);
            }

            var files = Directory.GetFiles(directory, "worker*.log");
            Assert.InRange(files.Length, 1, 3);

            foreach (var line in files.SelectMany(File.ReadAllLines))
            {
                using var document = JsonDocument.Parse(line);
                Assert.Equal(
                    "phase2-test",
                    document.RootElement.GetProperty("category").GetString());
                Assert.True(
                    document.RootElement
                        .GetProperty("properties")
                        .TryGetProperty("CommandIndex", out _));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void RollingLogger_ShouldFallbackWhenStructuredPropertyCannotBeSerialized()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"akasha-log-{Guid.NewGuid():N}");
        var logPath = Path.Combine(directory, "worker.log");

        try
        {
            using var provider = new JsonRollingFileLoggerProvider(
                new JsonRollingFileLoggerOptions(logPath));
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = factory.CreateLogger("phase2-fallback-test");
            var cyclic = new CyclicLogValue();
            cyclic.Self = cyclic;

            var exception = Record.Exception(
                () => logger.LogInformation("Cyclic property {Cyclic}", cyclic));

            Assert.Null(exception);
            var line = Assert.Single(File.ReadAllLines(logPath));
            using var document = JsonDocument.Parse(line);
            Assert.Equal(
                "A log entry could not be serialized.",
                document.RootElement.GetProperty("message").GetString());
            Assert.Equal(
                "JsonException",
                document.RootElement.GetProperty("loggingError").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExceptionObserver_ShouldMarkTaskExceptionsAsObserved()
    {
        var observer = new WorkerExceptionObserver(
            NullLogger<WorkerExceptionObserver>.Instance);
        var eventArgs = new UnobservedTaskExceptionEventArgs(
            new AggregateException(new InvalidOperationException("test")));

        observer.HandleUnobservedTaskException(null, eventArgs);

        Assert.True(eventArgs.Observed);
    }

    private static WorkerStateMachine CreateStateMachineAt(WorkerState state)
    {
        var stateMachine = new WorkerStateMachine();
        if (state == WorkerState.Created)
        {
            return stateMachine;
        }

        stateMachine.TransitionTo(WorkerState.Connecting);
        if (state == WorkerState.Connecting)
        {
            return stateMachine;
        }

        if (state == WorkerState.Stopping)
        {
            stateMachine.TransitionTo(WorkerState.Stopping);
            return stateMachine;
        }

        if (state == WorkerState.Stopped)
        {
            stateMachine.TransitionTo(WorkerState.Stopping);
            stateMachine.TransitionTo(WorkerState.Stopped);
            return stateMachine;
        }

        stateMachine.TransitionTo(WorkerState.Handshaking);
        if (state == WorkerState.Handshaking)
        {
            return stateMachine;
        }

        stateMachine.TransitionTo(WorkerState.Ready);
        if (state == WorkerState.Ready)
        {
            return stateMachine;
        }

        stateMachine.TransitionTo(WorkerState.Running);
        return stateMachine;
    }

    private static WorkerCommandContext CreateCommand(string correlationId) =>
        new(
            new CompanionEnvelope
            {
                Type = CompanionProtocol.Request,
                CorrelationId = correlationId,
                Method = "worker.echo",
            },
            CreateOptions(),
            "test-version",
            DateTimeOffset.UnixEpoch);

    private static WorkerLaunchOptions CreateOptions() =>
        new(
            "akasha-phase2-test",
            "0123456789abcdef0123456789abcdef",
            Environment.ProcessId,
            CompanionProtocol.CurrentVersion);

    private sealed class BlockingCommandHandler : IWorkerCommandHandler
    {
        private int _handledCount;

        public TaskCompletionSource FirstCommandStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstCommand { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int HandledCount => Volatile.Read(ref _handledCount);

        public async ValueTask<CompanionEnvelope> HandleAsync(
            WorkerCommandContext command,
            CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref _handledCount);
            if (count == 1)
            {
                FirstCommandStarted.TrySetResult();
                await ReleaseFirstCommand.Task.WaitAsync(cancellationToken);
            }

            return Response(command.Request);
        }
    }

    private sealed class PassthroughCommandHandler : IWorkerCommandHandler
    {
        public ValueTask<CompanionEnvelope> HandleAsync(
            WorkerCommandContext command,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Response(command.Request));
    }

    private sealed class ObservingResource(
        string name,
        EmergencyStopController emergencyStop,
        ICollection<string> stopOrder)
        : IWorkerRuntimeResource
    {
        public int StopCount { get; private set; }

        public bool EmergencyWasActiveWhenStopped { get; private set; }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopCount++;
            EmergencyWasActiveWhenStopped = emergencyStop.Snapshot.IsActive;
            stopOrder.Add(name);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingResource : IWorkerRuntimeResource
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

    private sealed class CyclicLogValue
    {
        public CyclicLogValue? Self { get; set; }
    }

    private static CompanionEnvelope Response(CompanionEnvelope request) =>
        new()
        {
            Type = CompanionProtocol.Response,
            CorrelationId = request.CorrelationId,
        };
}
