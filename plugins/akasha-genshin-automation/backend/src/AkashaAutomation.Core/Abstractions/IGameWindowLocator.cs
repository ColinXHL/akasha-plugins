using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Abstractions;

public interface IGameWindowLocator
{
    ValueTask<GameWindowInfo?> LocateAsync(CancellationToken cancellationToken = default);
}
