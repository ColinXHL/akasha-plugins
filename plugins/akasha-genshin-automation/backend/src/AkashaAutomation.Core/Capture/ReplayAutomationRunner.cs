using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Core.Capture;

public sealed class ReplayAutomationRunner
{
    private readonly SingleFrameScheduler _scheduler;

    public ReplayAutomationRunner(
        ReplayCaptureSource captureSource,
        RecordingInputService inputService,
        IGameContextProvider gameContextProvider,
        IEnumerable<IAutomationFeature> features,
        IClock clock,
        InMemoryDiagnosticsSink diagnostics)
    {
        var arbiter = new InputArbiter(inputService, diagnostics, clock);
        _scheduler = new SingleFrameScheduler(
            captureSource,
            gameContextProvider,
            features,
            arbiter,
            diagnostics,
            clock);
    }

    public ValueTask<SingleFrameScheduleResult> RunOnceAsync(CancellationToken cancellationToken = default) =>
        _scheduler.RunOnceAsync(cancellationToken);
}
