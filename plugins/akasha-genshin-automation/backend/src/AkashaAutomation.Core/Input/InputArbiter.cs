using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Input;

public sealed class InputArbiter : IInputArbiter
{
    private readonly IInputService _inputService;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastFrameSequence = -1;
    private int _emergencyStop;

    public InputArbiter(IInputService inputService, IDiagnosticsSink diagnostics, IClock clock)
    {
        _inputService = inputService;
        _diagnostics = diagnostics;
        _clock = clock;
    }

    public async ValueTask<InputArbitrationResult> SubmitAsync(
        long frameSequence,
        GameContextSnapshot context,
        IReadOnlyCollection<AutomationIntent> intents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(intents);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _emergencyStop) != 0)
            {
                return Reject("emergency_stop", frameSequence, intents.Count);
            }

            if (!context.IsGameForeground)
            {
                return Reject("game_not_foreground", frameSequence, intents.Count);
            }

            if (frameSequence <= _lastFrameSequence)
            {
                return Reject("frame_already_processed", frameSequence, intents.Count);
            }

            _lastFrameSequence = frameSequence;
            var selected = intents
                .OrderByDescending(intent => intent.Priority)
                .ThenBy(intent => intent.FeatureId, StringComparer.Ordinal)
                .FirstOrDefault();
            if (selected is null)
            {
                return Reject("no_intent", frameSequence, 0);
            }

            await _inputService.ExecuteAsync(selected.Actions, context, cancellationToken).ConfigureAwait(false);
            WriteDiagnostic("executed", frameSequence, intents.Count, selected.FeatureId);
            return new InputArbitrationResult(true, "executed", selected);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _emergencyStop, 1);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _inputService.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
            WriteDiagnostic("emergency_stop", _lastFrameSequence, 0, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private InputArbitrationResult Reject(string reason, long frameSequence, int intentCount)
    {
        WriteDiagnostic(reason, frameSequence, intentCount, null);
        return new InputArbitrationResult(false, reason);
    }

    private void WriteDiagnostic(string name, long frameSequence, int intentCount, string? featureId)
    {
        _diagnostics.Write(
            new DiagnosticEvent(
                _clock.UtcNow,
                "input",
                name,
                new Dictionary<string, object?>
                {
                    ["frameSequence"] = frameSequence,
                    ["intentCount"] = intentCount,
                    ["featureId"] = featureId,
                }));
    }
}
