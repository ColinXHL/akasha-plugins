using AkashaAutomation.Features.AutoDialogue;

namespace AkashaAutomation.Worker.Hosting;

public sealed class AutoDialogueRuntimeResource(
    IAutoDialogueController controller,
    IDialogueOptionVoiceWaiter voiceWaiter) : IWorkerRuntimeResource
{
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        controller.SetEnabled(false);
        voiceWaiter.Cancel();
        await voiceWaiter.DisposeAsync().ConfigureAwait(false);
    }
}
