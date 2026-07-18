namespace AkashaAutomation.Core.Capture;

public readonly record struct RegionOfInterest
{
    public RegionOfInterest(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public bool FitsWithin(CaptureSize size) => Right <= size.Width && Bottom <= size.Height;

    public RegionOfInterest Clamp(CaptureSize size)
    {
        var left = Math.Clamp(X, 0, size.Width - 1);
        var top = Math.Clamp(Y, 0, size.Height - 1);
        var right = Math.Clamp(Right, left + 1, size.Width);
        var bottom = Math.Clamp(Bottom, top + 1, size.Height);
        return new RegionOfInterest(left, top, right - left, bottom - top);
    }
}
