using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Worker.Hosting;

public sealed class AutomationRecognitionRuntimeResource(
    ICaptureSource captureSource,
    IOcrEngine ocrEngine,
    BetterGiAutoPickRecognizer autoPickRecognizer,
    BetterGiAutoDialogueRecognizer autoDialogueRecognizer) : IWorkerRuntimeResource
{
    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await captureSource.DisposeAsync().ConfigureAwait(false);
        await ocrEngine.DisposeAsync().ConfigureAwait(false);
        autoDialogueRecognizer.Dispose();
        autoPickRecognizer.Dispose();
    }
}
