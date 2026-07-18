using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.Core.Abstractions;

public interface ICaptureSource : IAsyncDisposable
{
    ValueTask<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken = default);
}
