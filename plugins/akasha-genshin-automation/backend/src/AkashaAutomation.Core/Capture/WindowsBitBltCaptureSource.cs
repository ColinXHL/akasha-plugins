using System.ComponentModel;
using System.Runtime.InteropServices;
using AkashaAutomation.Core.Abstractions;
using OpenCvSharp;

namespace AkashaAutomation.Core.Capture;

public sealed class WindowsBitBltCaptureSource(
    IGameWindowLocator windowLocator,
    IClock clock) : ICaptureSource
{
    private readonly object _sessionGate = new();
    private CaptureSession? _session;
    private long _sequence;
    private bool _disposed;

    public async ValueTask<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var window = await windowLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Mat image;
        lock (_sessionGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            image = CaptureWindow(window.Handle, window.ClientSize);
        }

        return CapturedFrame.TakeOwnership(
            image,
            Interlocked.Increment(ref _sequence),
            clock.UtcNow,
            $"bitblt:{window.ProcessId}");
    }

    public ValueTask DisposeAsync()
    {
        lock (_sessionGate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            ResetSession();
        }

        return ValueTask.CompletedTask;
    }

    private Mat CaptureWindow(nint windowHandle, CaptureSize clientSize)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            EnsureSession(windowHandle, clientSize);
            try
            {
                return _session!.Capture();
            }
            catch when (attempt == 0)
            {
                ResetSession();
            }
            catch
            {
                ResetSession();
                throw;
            }
        }

        throw new InvalidOperationException("Unable to capture the game window.");
    }

    private void EnsureSession(nint windowHandle, CaptureSize clientSize)
    {
        if (_session?.Matches(windowHandle, clientSize) == true)
        {
            return;
        }

        ResetSession();
        _session = new CaptureSession(windowHandle, clientSize);
    }

    private void ResetSession()
    {
        _session?.Dispose();
        _session = null;
    }

    private sealed class CaptureSession : IDisposable
    {
        private readonly nint _windowHandle;
        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;
        private nint _sourceDc;
        private nint _memoryDc;
        private nint _bitmap;
        private nint _previousBitmap;
        private nint _bits;
        private bool _disposed;

        internal CaptureSession(nint windowHandle, CaptureSize clientSize)
        {
            if (windowHandle == nint.Zero)
            {
                throw new ArgumentException("The game window handle is invalid.", nameof(windowHandle));
            }

            if (clientSize.Width <= 0 || clientSize.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(clientSize), "The game client size is invalid.");
            }

            _windowHandle = windowHandle;
            _width = clientSize.Width;
            _height = clientSize.Height;
            _stride = checked(_width * 4);

            try
            {
                _sourceDc = NativeMethods.GetDC(_windowHandle);
                if (_sourceDc == nint.Zero)
                {
                    throw NewWin32Exception("Unable to acquire the game window device context.");
                }

                _memoryDc = NativeMethods.CreateCompatibleDC(_sourceDc);
                if (_memoryDc == nint.Zero)
                {
                    throw NewWin32Exception("Unable to create a capture device context.");
                }

                var bitmapInfo = NativeBitmapInfo.Create(_width, _height);
                _bitmap = NativeMethods.CreateDIBSection(
                    _sourceDc,
                    ref bitmapInfo,
                    NativeMethods.DibRgbColors,
                    out _bits,
                    nint.Zero,
                    0);
                if (_bitmap == nint.Zero || _bits == nint.Zero)
                {
                    throw NewWin32Exception("Unable to create a capture DIB section.");
                }

                _previousBitmap = NativeMethods.SelectObject(_memoryDc, _bitmap);
                if (_previousBitmap == nint.Zero || _previousBitmap == new nint(-1))
                {
                    throw NewWin32Exception("Unable to select the capture DIB section.");
                }

                if (!NativeMethods.GdiFlush())
                {
                    throw NewWin32Exception("Unable to initialize the capture session.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool Matches(nint windowHandle, CaptureSize clientSize) =>
            !_disposed &&
            _windowHandle == windowHandle &&
            _width == clientSize.Width &&
            _height == clientSize.Height;

        internal Mat Capture()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!NativeMethods.BitBlt(
                    _memoryDc,
                    0,
                    0,
                    _width,
                    _height,
                    _sourceDc,
                    0,
                    0,
                    NativeMethods.SourceCopy))
            {
                throw NewWin32Exception("Unable to capture the game client area.");
            }

            if (!NativeMethods.GdiFlush())
            {
                throw NewWin32Exception("Unable to flush the captured game frame.");
            }

            using var bgra = Mat.FromPixelData(
                _height,
                _width,
                MatType.CV_8UC4,
                _bits,
                _stride);
            return bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            NativeMethods.GdiFlush();

            if (_previousBitmap != nint.Zero &&
                _previousBitmap != new nint(-1) &&
                _memoryDc != nint.Zero)
            {
                NativeMethods.SelectObject(_memoryDc, _previousBitmap);
            }

            _previousBitmap = nint.Zero;
            if (_bitmap != nint.Zero)
            {
                NativeMethods.DeleteObject(_bitmap);
                _bitmap = nint.Zero;
            }

            _bits = nint.Zero;
            if (_memoryDc != nint.Zero)
            {
                NativeMethods.DeleteDC(_memoryDc);
                _memoryDc = nint.Zero;
            }

            if (_sourceDc != nint.Zero)
            {
                NativeMethods.ReleaseDC(_windowHandle, _sourceDc);
                _sourceDc = nint.Zero;
            }
        }

        private static Win32Exception NewWin32Exception(string message) =>
            new(Marshal.GetLastWin32Error(), message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPixelsPerMeter;
        internal int YPixelsPerMeter;
        internal uint ColorsUsed;
        internal uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmapInfo
    {
        internal NativeBitmapInfoHeader Header;
        internal uint Colors;

        internal static NativeBitmapInfo Create(int width, int height) =>
            new()
            {
                Header = new NativeBitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<NativeBitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = NativeMethods.BitmapCompressionRgb,
                    SizeImage = checked((uint)(width * height * 4)),
                },
            };
    }

    private static class NativeMethods
    {
        internal const uint SourceCopy = 0x00CC0020;
        internal const uint DibRgbColors = 0;
        internal const uint BitmapCompressionRgb = 0;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint GetDC(nint window);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern int ReleaseDC(nint window, nint deviceContext);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateCompatibleDC(nint deviceContext);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint CreateDIBSection(
            nint deviceContext,
            ref NativeBitmapInfo bitmapInfo,
            uint usage,
            out nint bits,
            nint section,
            uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        internal static extern nint SelectObject(nint deviceContext, nint value);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteDC(nint deviceContext);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BitBlt(
            nint destination,
            int destinationX,
            int destinationY,
            int width,
            int height,
            nint source,
            int sourceX,
            int sourceY,
            uint operation);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GdiFlush();
    }
}
