using AkashaAutomation.Core.GameContext;

namespace AkashaAutomation.Core.Abstractions;

public interface IGameContextProvider
{
    ValueTask<GameContextSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
