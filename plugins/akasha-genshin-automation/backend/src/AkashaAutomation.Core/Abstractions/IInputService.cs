using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.Core.Abstractions;

public interface IInputService : IAsyncDisposable
{
    ValueTask ExecuteAsync(
        InputActionGroup actions,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default);

    ValueTask ReleaseAllAsync(CancellationToken cancellationToken = default);
}
