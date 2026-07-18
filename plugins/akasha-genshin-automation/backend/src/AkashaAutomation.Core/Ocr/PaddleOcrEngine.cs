using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using OpenCvSharp;

namespace AkashaAutomation.Core.Ocr;

public sealed class PaddleOcrEngine : IOcrEngine
{
    private static long _activeSessions;
    private readonly PaddleOcrModelOptions _options;
    private readonly IPaddleOcrSessionFactory _sessionFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPaddleOcrSession? _session;
    private bool _disposed;

    public PaddleOcrEngine(PaddleOcrModelOptions options, IPaddleOcrSessionFactory sessionFactory)
    {
        _options = options;
        _sessionFactory = sessionFactory;
    }

    public static long ActiveSessions => Interlocked.Read(ref _activeSessions);

    public async ValueTask WarmUpAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = GetOrCreateSession();
            using var image = new Mat(32, 32, MatType.CV_8UC3, Scalar.Black);
            session.Recognize(image, cancellationToken);
            session.RecognizeSingleLine(image, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<OcrResult> RecognizeAsync(
        CapturedFrame frame,
        RegionOfInterest? region = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = GetOrCreateSession();
            using var cropped = region is { } roi ? frame.CloneRegion(roi) : null;
            var source = cropped ?? frame;
            var result = source.UseImage(mat => session.Recognize(mat, cancellationToken));
            return region is { } offset ? OffsetResult(result, offset.X, offset.Y) : result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<OcrResult> RecognizeSingleLineAsync(
        CapturedFrame frame,
        RegionOfInterest region,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var session = GetOrCreateSession();
            using var cropped = frame.CloneRegion(region);
            var result = cropped.UseImage(mat => session.RecognizeSingleLine(mat, cancellationToken));
            return OffsetResult(result, region.X, region.Y);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_session is not null)
            {
                _session.Dispose();
                _session = null;
                Interlocked.Decrement(ref _activeSessions);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private IPaddleOcrSession GetOrCreateSession()
    {
        if (_session is not null)
        {
            return _session;
        }

        _options.ValidateFiles();
        _session = _sessionFactory.Create(_options)
            ?? throw new InvalidOperationException("The Paddle OCR session factory returned null.");
        Interlocked.Increment(ref _activeSessions);
        return _session;
    }

    private static OcrResult OffsetResult(OcrResult result, int offsetX, int offsetY) =>
        result with
        {
            Regions = result.Regions
                .Select(region => region with
                {
                    Region = new RegionOfInterest(
                        region.Region.X + offsetX,
                        region.Region.Y + offsetY,
                        region.Region.Width,
                        region.Region.Height),
                })
                .ToArray(),
        };
}
