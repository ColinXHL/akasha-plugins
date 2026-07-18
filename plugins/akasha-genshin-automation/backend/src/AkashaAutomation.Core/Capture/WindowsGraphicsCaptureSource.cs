using AkashaAutomation.Core.Abstractions;
using OpenCvSharp;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Device = SharpDX.Direct3D11.Device;

namespace AkashaAutomation.Core.Capture;

public sealed class WindowsGraphicsCaptureSource : ICaptureSource
{
    private readonly IGameWindowLocator _windowLocator;
    private readonly IClock _clock;
    private readonly object _frameGate = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private Device? _sharpDxDevice;
    private IDirect3DDevice? _winRtDevice;
    private GraphicsCaptureItem? _captureItem;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private Texture2D? _stagingTexture;
    private Mat? _latestFrame;
    private TaskCompletionSource _firstFrame = NewFrameCompletion();
    private nint _windowHandle;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private long _sequence;
    private bool _disposed;

    public WindowsGraphicsCaptureSource(IGameWindowLocator windowLocator, IClock clock)
    {
        _windowLocator = windowLocator;
        _clock = clock;
    }

    public async ValueTask<CapturedFrame?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var window = await _windowLocator.LocateAsync(cancellationToken).ConfigureAwait(false);
        if (window is null)
        {
            return null;
        }

        await EnsureStartedAsync(window.Handle, cancellationToken).ConfigureAwait(false);
        Mat? clone;
        lock (_frameGate)
        {
            clone = _latestFrame?.Clone();
        }

        if (clone is null)
        {
            await _firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            lock (_frameGate)
            {
                clone = _latestFrame?.Clone();
            }
        }

        return clone is null
            ? null
            : CapturedFrame.TakeOwnership(
                clone,
                Interlocked.Increment(ref _sequence),
                _clock.UtcNow,
                $"wgc:{window.ProcessId}");
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCapture();
        }
        finally
        {
            _lifecycleGate.Release();
            _lifecycleGate.Dispose();
        }
    }

    private async ValueTask EnsureStartedAsync(nint windowHandle, CancellationToken cancellationToken)
    {
        if (_windowHandle == windowHandle && _session is not null)
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_windowHandle == windowHandle && _session is not null)
            {
                return;
            }

            StopCapture();
            _windowHandle = windowHandle;
            _sharpDxDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);
            _winRtDevice = WindowsGraphicsCaptureInterop.CreateWinRtDevice(_sharpDxDevice);
            _captureItem = WindowsGraphicsCaptureInterop.CreateItemForWindow(windowHandle);
            _surfaceWidth = _captureItem.Size.Width;
            _surfaceHeight = _captureItem.Size.Height;
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                _winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                _captureItem.Size);
            _framePool.FrameArrived += OnFrameArrived;
            _captureItem.Closed += OnCaptureItemClosed;
            _session = _framePool.CreateCaptureSession(_captureItem);
            _session.IsCursorCaptureEnabled = false;
            _firstFrame = NewFrameCompletion();
            _session.StartCapture();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        try
        {
            using var frame = sender.TryGetNextFrame();
            if (frame is null || _sharpDxDevice is null || _winRtDevice is null)
            {
                return;
            }

            var width = frame.ContentSize.Width;
            var height = frame.ContentSize.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            if (width != _surfaceWidth || height != _surfaceHeight)
            {
                _surfaceWidth = width;
                _surfaceHeight = height;
                _stagingTexture?.Dispose();
                _stagingTexture = null;
                sender.Recreate(
                    _winRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    2,
                    frame.ContentSize);
                return;
            }

            using var surfaceTexture = WindowsGraphicsCaptureInterop.CreateTexture(frame.Surface);
            _stagingTexture ??= CreateStagingTexture(_sharpDxDevice, width, height);
            _sharpDxDevice.ImmediateContext.CopyResource(surfaceTexture, _stagingTexture);
            var data = _sharpDxDevice.ImmediateContext.MapSubresource(
                _stagingTexture,
                0,
                MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);
            Mat converted;
            try
            {
                using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, data.DataPointer, data.RowPitch);
                converted = bgra.CvtColor(ColorConversionCodes.BGRA2BGR);
            }
            finally
            {
                _sharpDxDevice.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
            }

            lock (_frameGate)
            {
                _latestFrame?.Dispose();
                _latestFrame = converted;
            }

            _firstFrame.TrySetResult();
        }
        catch (SharpDXException exception)
        {
            _firstFrame.TrySetException(exception);
        }
        catch (Exception exception) when (!_disposed)
        {
            _firstFrame.TrySetException(exception);
        }
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        lock (_frameGate)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _firstFrame.TrySetException(new InvalidOperationException("The captured game window was closed."));
    }

    private void StopCapture()
    {
        if (_framePool is not null)
        {
            _framePool.FrameArrived -= OnFrameArrived;
        }

        if (_captureItem is not null)
        {
            _captureItem.Closed -= OnCaptureItemClosed;
        }

        _session?.Dispose();
        _session = null;
        _framePool?.Dispose();
        _framePool = null;
        _captureItem = null;
        _winRtDevice?.Dispose();
        _winRtDevice = null;
        _stagingTexture?.Dispose();
        _stagingTexture = null;
        _sharpDxDevice?.Dispose();
        _sharpDxDevice = null;
        lock (_frameGate)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _windowHandle = nint.Zero;
        _surfaceWidth = 0;
        _surfaceHeight = 0;
    }

    private static Texture2D CreateStagingTexture(Device device, int width, int height) =>
        new(
            device,
            new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                Usage = ResourceUsage.Staging,
                SampleDescription = new SampleDescription(1, 0),
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
                OptionFlags = ResourceOptionFlags.None,
            });

    private static TaskCompletionSource NewFrameCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
