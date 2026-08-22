using Microsoft.Extensions.Logging;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.QuickTeleport;

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
        IQuickTeleportController? quickTeleportController = null,
        AutomationFeatureControls? featureControls = null,
        bool realInputEnabled = false)
    {
        StateMachine = new WorkerStateMachine();
        EmergencyStop = new EmergencyStopController();
        FeatureControls = featureControls ?? new AutomationFeatureControls([]);
        StatusProvider = new WorkerStatusProvider(
            StateMachine,
            EmergencyStop,
            autoPickController,
            autoDialogueController,
            quickTeleportController,
            realInputEnabled);
        CommandHandler = commandHandlerFactory?.Invoke(StatusProvider, EmergencyStop)
                         ?? new WorkerCommandHandler(
                             StatusProvider,
                             EmergencyStop,
                             autoPickController,
                             autoDialogueController,
                             quickTeleportController);
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

    public AutomationFeatureControls FeatureControls { get; }

    public WorkerStatusProvider StatusProvider { get; }

    public IWorkerCommandHandler CommandHandler { get; }

    public WorkerCommandQueue CommandQueue { get; }

    public WorkerShutdownCoordinator Shutdown { get; }
}
