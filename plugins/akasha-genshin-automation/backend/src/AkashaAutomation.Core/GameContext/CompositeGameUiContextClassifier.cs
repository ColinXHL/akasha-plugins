using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.Core.GameContext;

public sealed class CompositeGameUiContextClassifier : IGameUiContextClassifier
{
    private readonly IReadOnlyList<IGameUiContextDetector> _detectors;

    public CompositeGameUiContextClassifier(IEnumerable<IGameUiContextDetector> detectors)
    {
        ArgumentNullException.ThrowIfNull(detectors);
        _detectors = detectors
            .OrderByDescending(detector => detector.Priority)
            .ThenBy(detector => detector.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async ValueTask<GameUiCategory> ClassifyAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var detector in _detectors)
        {
            var category = await detector
                .DetectAsync(frame, context, cancellationToken)
                .ConfigureAwait(false);
            if (category is not null and not GameUiCategory.Unknown)
            {
                return category.Value;
            }
        }

        return GameUiCategory.Unknown;
    }
}
