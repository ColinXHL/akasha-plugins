using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Abstractions;

public interface IGameUiContextDetector
{
    string Id { get; }

    int Priority { get; }

    ValueTask<GameUiCategory?> DetectAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default);
}
