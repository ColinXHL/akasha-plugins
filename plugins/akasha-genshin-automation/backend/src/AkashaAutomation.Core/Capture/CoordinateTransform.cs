namespace AkashaAutomation.Core.Capture;

public sealed class CoordinateTransform
{
    public CoordinateTransform(CaptureSize referenceSize, CaptureSize actualSize)
    {
        ReferenceSize = referenceSize;
        ActualSize = actualSize;
    }

    public CaptureSize ReferenceSize { get; }

    public CaptureSize ActualSize { get; }

    public double ScaleX => (double)ActualSize.Width / ReferenceSize.Width;

    public double ScaleY => (double)ActualSize.Height / ReferenceSize.Height;

    public RegionOfInterest Scale(RegionOfInterest region)
    {
        var left = ScaleCoordinate(region.X, ScaleX);
        var top = ScaleCoordinate(region.Y, ScaleY);
        var right = ScaleCoordinate(region.Right, ScaleX);
        var bottom = ScaleCoordinate(region.Bottom, ScaleY);
        return new RegionOfInterest(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top))
            .Clamp(ActualSize);
    }

    public (int X, int Y) ScalePoint(int x, int y) =>
        (ScaleCoordinate(x, ScaleX), ScaleCoordinate(y, ScaleY));

    private static int ScaleCoordinate(int value, double scale) =>
        checked((int)Math.Round(value * scale, MidpointRounding.AwayFromZero));
}
