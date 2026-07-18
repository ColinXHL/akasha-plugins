using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Core.GameContext;

public sealed class GameContextProvider : IGameContextProvider
{
    private readonly IGameWindowLocator _windowLocator;
    private readonly IClock _clock;

    public GameContextProvider(IGameWindowLocator windowLocator, IClock clock)
    {
        _windowLocator = windowLocator;
        _clock = clock;
    }

    public async ValueTask<GameContextSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        new(
            _clock.UtcNow,
            await _windowLocator.LocateAsync(cancellationToken).ConfigureAwait(false));
}
