using AkashaAutomation.Core.Abstractions;
using OpenCvSharp;

namespace AkashaAutomation.Core.Capture;

public sealed class ReplayCaptureSource : ICaptureSource
{
    private readonly IReadOnlyList<string> _framePaths;
    private readonly IClock _clock;
    private int _nextFrame;
    private bool _disposed;

    public ReplayCaptureSource(IEnumerable<string> framePaths, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(framePaths);
        _clock = clock;
        _framePaths = framePaths.Select(Path.GetFullPath).ToArray();
        if (_framePaths.Count == 0)
        {
            throw new ArgumentException("At least one replay frame is required.", nameof(framePaths));
        }

        var missing = _framePaths.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            throw new FileNotFoundException("Replay frame was not found.", missing);
        }
    }

    public ValueTask<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (_nextFrame >= _framePaths.Count)
        {
            return ValueTask.FromResult<CapturedFrame?>(null);
        }

        var path = _framePaths[_nextFrame++];
        var mat = Cv2.ImRead(path, ImreadModes.Color);
        if (mat.Empty())
        {
            mat.Dispose();
            throw new InvalidDataException($"Replay frame is not a decodable image: {path}");
        }

        return ValueTask.FromResult<CapturedFrame?>(
            CapturedFrame.TakeOwnership(mat, _nextFrame, _clock.UtcNow, path));
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
