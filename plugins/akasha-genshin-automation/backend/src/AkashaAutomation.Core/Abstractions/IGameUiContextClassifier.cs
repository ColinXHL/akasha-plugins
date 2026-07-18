using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Abstractions;

public interface IGameUiContextClassifier
{
    ValueTask<GameUiCategory> ClassifyAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default);
}

public sealed class NullGameUiContextClassifier : IGameUiContextClassifier
{
    public ValueTask<GameUiCategory> ClassifyAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GameUiCategory.Unknown);
    }
}
