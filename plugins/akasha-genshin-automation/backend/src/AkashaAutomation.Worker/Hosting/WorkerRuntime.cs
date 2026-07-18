using Microsoft.Extensions.Logging;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;

namespace AkashaAutomation.Worker.Hosting;

public sealed class WorkerRuntime
{
    public WorkerRuntime(
        IEnumerable<IWorkerRuntimeResource>? resources = null,
        ILoggerFactory? loggerFactory = null,
        int commandQueueCapacity = WorkerCommandQueue.DefaultCapacity,
        Func<WorkerStatusProvider, EmergencyStopController, IWorkerCommandHandler>? commandHandlerFactory = null,
        IAutoPickController? autoPickController = null,
        IAutoDialogueController? autoDialogueController = null,
        bool realInputEnabled = false)
    {
        StateMachine = new WorkerStateMachine();
        EmergencyStop = new EmergencyStopController();
        StatusProvider = new WorkerStatusProvider(
            StateMachine,
            EmergencyStop,
            autoPickController,
            autoDialogueController,
            realInputEnabled);
        CommandHandler = commandHandlerFactory?.Invoke(StatusProvider, EmergencyStop)
                         ?? new WorkerCommandHandler(StatusProvider, EmergencyStop, autoPickController, autoDialogueController);
        CommandQueue = new WorkerCommandQueue(
            CommandHandler,
            commandQueueCapacity,
            EmergencyStop.CancellationToken);
        Shutdown = new WorkerShutdownCoordinator(
            EmergencyStop,
            StateMachine,
            CommandQueue,
            StatusProvider,
            resources,
            loggerFactory?.CreateLogger<WorkerShutdownCoordinator>());
    }

    public WorkerStateMachine StateMachine { get; }

    public EmergencyStopController EmergencyStop { get; }

    public WorkerStatusProvider StatusProvider { get; }

    public IWorkerCommandHandler CommandHandler { get; }

    public WorkerCommandQueue CommandQueue { get; }

    public WorkerShutdownCoordinator Shutdown { get; }
}
