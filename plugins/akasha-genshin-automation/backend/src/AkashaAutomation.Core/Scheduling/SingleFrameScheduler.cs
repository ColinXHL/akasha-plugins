using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.Core.Scheduling;

public sealed class SingleFrameScheduler
{
    private readonly ICaptureSource _captureSource;
    private readonly IGameContextProvider _gameContextProvider;
    private readonly IReadOnlyList<IAutomationFeature> _features;
    private readonly IInputArbiter _inputArbiter;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly IClock _clock;
    private readonly IGameUiContextClassifier _contextClassifier;

    public SingleFrameScheduler(
        ICaptureSource captureSource,
        IGameContextProvider gameContextProvider,
        IEnumerable<IAutomationFeature> features,
        IInputArbiter inputArbiter,
        IDiagnosticsSink diagnostics,
        IClock clock,
        IGameUiContextClassifier? contextClassifier = null)
    {
        _captureSource = captureSource;
        _gameContextProvider = gameContextProvider;
        _features = features.OrderByDescending(feature => feature.Priority).ToArray();
        _inputArbiter = inputArbiter;
        _diagnostics = diagnostics;
        _clock = clock;
        _contextClassifier = contextClassifier ?? new NullGameUiContextClassifier();
    }

    public async ValueTask<SingleFrameScheduleResult> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        var context = await _gameContextProvider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!context.HasGameWindow)
        {
            return new SingleFrameScheduleResult(false, null, [], new(false, "game_window_not_found"));
        }

        using var frame = await _captureSource.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (frame is null)
        {
            return new SingleFrameScheduleResult(false, null, [], new(false, "capture_unavailable"));
        }

        try
        {
            context = context with
            {
                UiCategory = await _contextClassifier
                    .ClassifyAsync(frame, context, cancellationToken)
                    .ConfigureAwait(false),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _diagnostics.Write(
                new DiagnosticEvent(
                    _clock.UtcNow,
                    "scheduler",
                    "context_classification_failed",
                    new Dictionary<string, object?>
                    {
                        ["exceptionType"] = exception.GetType().FullName,
                        ["message"] = exception.Message,
                    }));
        }

        var decisions = new List<FeatureDecision>(_features.Count);
        foreach (var feature in _features)
        {
            try
            {
                decisions.Add(await feature.EvaluateAsync(frame, context, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                decisions.Add(FeatureDecision.NoAction(feature.Id, "feature_failed"));
                _diagnostics.Write(
                    new DiagnosticEvent(
                        _clock.UtcNow,
                        "scheduler",
                        "feature_failed",
                        new Dictionary<string, object?>
                        {
                            ["featureId"] = feature.Id,
                            ["exceptionType"] = exception.GetType().FullName,
                            ["message"] = exception.Message,
                        }));
            }
        }

        var intents = decisions
            .Where(decision => decision.ShouldAct && decision.Intent is not null)
            .Select(decision => decision.Intent!)
            .ToArray();
        var arbitration = await _inputArbiter
            .SubmitAsync(frame.Sequence, context, intents, cancellationToken)
            .ConfigureAwait(false);
        return new SingleFrameScheduleResult(true, frame.Sequence, decisions, arbitration);
    }
}

public sealed record SingleFrameScheduleResult(
    bool Captured,
    long? FrameSequence,
    IReadOnlyList<FeatureDecision> Decisions,
    InputArbitrationResult Arbitration);
