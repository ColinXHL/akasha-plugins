using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Features.AutoDialogue;

public interface IDialogueOptionVoiceWaiter : IAsyncDisposable
{
    bool IsWaiting { get; }

    bool IsFallback { get; }

    bool Start(int processId, TimeSpan maximumWait, TimeSpan fallbackDelay);

    bool Update();

    void Cancel();
}

public sealed class FixedDelayDialogueOptionVoiceWaiter(IClock clock) : IDialogueOptionVoiceWaiter
{
    private DateTimeOffset? _dueUtc;

    public bool IsWaiting => _dueUtc is not null;

    public bool IsFallback => IsWaiting;

    public bool Start(int processId, TimeSpan maximumWait, TimeSpan fallbackDelay)
    {
        _dueUtc = fallbackDelay > TimeSpan.Zero ? clock.UtcNow + fallbackDelay : null;
        return IsWaiting;
    }

    public bool Update()
    {
        if (_dueUtc is null || clock.UtcNow < _dueUtc)
        {
            return _dueUtc is null;
        }

        _dueUtc = null;
        return true;
    }

    public void Cancel() => _dueUtc = null;

    public ValueTask DisposeAsync()
    {
        Cancel();
        return ValueTask.CompletedTask;
    }
}
