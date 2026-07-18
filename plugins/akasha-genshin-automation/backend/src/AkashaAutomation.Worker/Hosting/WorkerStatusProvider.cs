using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;

namespace AkashaAutomation.Worker.Hosting;

public sealed class WorkerStatusProvider(
    WorkerStateMachine stateMachine,
    EmergencyStopController emergencyStop,
    IAutoPickController? autoPickController = null,
    IAutoDialogueController? autoDialogueController = null,
    bool realInputEnabled = false)
{
    private readonly object _errorGate = new();
    private WorkerErrorStatus? _lastError;

    public WorkerStatus GetStatus(
        WorkerLaunchOptions options,
        string workerVersion,
        DateTimeOffset startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerVersion);

        var emergency = emergencyStop.Snapshot;
        WorkerErrorStatus? lastError;
        lock (_errorGate)
        {
            lastError = _lastError;
        }

        var autoPick = autoPickController?.Status;
        var autoPickStatus = autoPick is null
            ? new FeatureStatus(false, false)
            : new FeatureStatus(autoPick.Enabled, autoPick.IsRunning)
            {
                Recognition = new AutoPickRecognitionStatus(
                    autoPick.LastRecognizedText,
                    autoPick.LastDecisionReason,
                    autoPick.LastIntentSubmitted,
                    autoPick.LastFrameSequence,
                    autoPick.UpdatedAtUtc),
            };
        var autoDialogue = autoDialogueController?.Status;
        var autoDialogueStatus = autoDialogue is null
            ? new FeatureStatus(false, false)
            : new FeatureStatus(autoDialogue.Enabled, autoDialogue.IsRunning)
            {
                DialogueRecognition = new AutoDialogueRecognitionStatus(
                    autoDialogue.UiCategory,
                    autoDialogue.LastRecognizedOptions,
                    autoDialogue.LastDecisionReason,
                    autoDialogue.LastIntentSubmitted,
                    autoDialogue.VoiceWaitActive,
                    autoDialogue.VoiceWaitFallback,
                    autoDialogue.LastFrameSequence,
                    autoDialogue.UpdatedAtUtc),
            };
        return new WorkerStatus(
            ToProtocolState(stateMachine.State),
            CompanionProtocol.CurrentVersion,
            workerVersion,
            options.ParentProcessId,
            startedAtUtc,
            realInputEnabled,
            new EmergencyStopStatus(
                emergency.IsActive,
                emergency.Reason,
                emergency.TriggeredAtUtc),
            new GameWindowStatus("not_found", false, null, null),
            new SubsystemStatus("not_started", false),
            new SubsystemStatus("not_started", false),
            new FeatureStatuses(
                autoPickStatus,
                autoDialogueStatus),
            lastError);
    }

    public void ReportError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        lock (_errorGate)
        {
            _lastError = new WorkerErrorStatus(code, message, DateTimeOffset.UtcNow);
        }
    }

    private static string ToProtocolState(WorkerState state) =>
        state switch
        {
            WorkerState.Created => "created",
            WorkerState.Connecting => "connecting",
            WorkerState.Handshaking => "handshaking",
            WorkerState.Ready => "ready",
            WorkerState.Running => "running",
            WorkerState.Stopping => "stopping",
            WorkerState.Stopped => "stopped",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
}
