using OpenCvSharp;

namespace AkashaAutomation.Core.Capture;

public sealed class CapturedFrame : IDisposable
{
    private static long _activeOwnedFrames;
    private Mat? _image;

    private CapturedFrame(Mat image, long sequence, DateTimeOffset capturedAtUtc, string source)
    {
        if (image.Empty())
        {
            image.Dispose();
            throw new ArgumentException("A captured frame cannot own an empty image.", nameof(image));
        }

        _image = image;
        Sequence = sequence;
        CapturedAtUtc = capturedAtUtc;
        Source = source;
        Size = new CaptureSize(image.Width, image.Height);
        Interlocked.Increment(ref _activeOwnedFrames);
    }

    public static long ActiveOwnedFrames => Interlocked.Read(ref _activeOwnedFrames);

    public long Sequence { get; }

    public DateTimeOffset CapturedAtUtc { get; }

    public string Source { get; }

    public CaptureSize Size { get; }

    public bool IsDisposed => _image is null;

    public static CapturedFrame TakeOwnership(
        Mat image,
        long sequence,
        DateTimeOffset capturedAtUtc,
        string source)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new CapturedFrame(image, sequence, capturedAtUtc, source);
    }

    public CapturedFrame CloneRegion(RegionOfInterest region, string? source = null)
    {
        var image = GetImage();
        if (!region.FitsWithin(Size))
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The ROI must fit completely inside the frame.");
        }

        using var view = new Mat(image, new Rect(region.X, region.Y, region.Width, region.Height));
        return new CapturedFrame(
            view.Clone(),
            Sequence,
            CapturedAtUtc,
            source ?? $"{Source}#roi({region.X},{region.Y},{region.Width},{region.Height})");
    }

    public T UseImage<T>(Func<Mat, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(GetImage());
    }

    public void Dispose()
    {
        var image = Interlocked.Exchange(ref _image, null);
        if (image is null)
        {
            return;
        }

        image.Dispose();
        Interlocked.Decrement(ref _activeOwnedFrames);
    }

    private Mat GetImage() => _image ?? throw new ObjectDisposedException(nameof(CapturedFrame));
}
