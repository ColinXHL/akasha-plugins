using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.CoreAudio;

namespace AkashaAutomation.BetterGiPort.Compatibility.Audio;

public sealed class BetterGiProcessLoopbackAudioCapture : IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";
    private const long BufferDurationHundredNanoseconds = 1_000_000;
    private readonly IAudioClient _audioClient;
    private readonly IAudioCaptureClient _captureClient;
    private bool _started;

    public BetterGiProcessLoopbackAudioCapture(int targetProcessId)
    {
        if (targetProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetProcessId));
        }

        IAudioClient? audioClient = null;
        IAudioCaptureClient? captureClient = null;
        try
        {
            audioClient = ActivateAudioClient(targetProcessId);
            InitializeAudioClient(audioClient);
            var captureClientId = typeof(IAudioCaptureClient).GUID;
            captureClient = audioClient.GetService(in captureClientId) as IAudioCaptureClient
                ?? throw new InvalidCastException("Audio client did not return IAudioCaptureClient.");
            audioClient.Start();
            _audioClient = audioClient;
            _captureClient = captureClient;
            _started = true;
        }
        catch
        {
            ReleaseComObject(captureClient);
            ReleaseComObject(audioClient);
            throw;
        }
    }

    public void ReadAvailableSamples(List<float>? destination)
    {
        while (true)
        {
            _captureClient.GetNextPacketSize(out var packetFrameCount).ThrowIfFailed();
            if (packetFrameCount == 0)
            {
                return;
            }

            _captureClient.GetBuffer(out var pointer, out var frameCount, out var flags, out _, out _).ThrowIfFailed();
            try
            {
                if (frameCount == 0 || destination is null)
                {
                    continue;
                }

                var sampleCount = checked((int)frameCount);
                if ((flags & AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                {
                    destination.AddRange(Enumerable.Repeat(0f, sampleCount));
                    continue;
                }

                var samples = new short[sampleCount];
                Marshal.Copy(pointer, samples, 0, samples.Length);
                destination.AddRange(samples.Select(sample => sample / 32768f));
            }
            finally
            {
                _captureClient.ReleaseBuffer(frameCount).ThrowIfFailed();
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (_started)
            {
                _audioClient.Stop();
            }
        }
        catch
        {
            // Best-effort shutdown after device/process loss.
        }
        finally
        {
            ReleaseComObject(_captureClient);
            ReleaseComObject(_audioClient);
        }
    }

    private static void InitializeAudioClient(IAudioClient audioClient)
    {
        var blockAlign = (ushort)2;
        var format = new WaveFormatEx
        {
            WFormatTag = 1,
            NChannels = 1,
            NSamplesPerSec = BetterGiSileroVadDetector.SampleRate,
            NAvgBytesPerSec = (uint)(BetterGiSileroVadDetector.SampleRate * blockAlign),
            NBlockAlign = blockAlign,
            WBitsPerSample = 16,
            CbSize = 0,
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<WaveFormatEx>());
        try
        {
            Marshal.StructureToPtr(format, pointer, false);
            var session = Guid.Empty;
            audioClient.Initialize(
                AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_LOOPBACK |
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
                AUDCLNT_STREAMFLAGS.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                BufferDurationHundredNanoseconds,
                0,
                pointer,
                in session).ThrowIfFailed();
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static IAudioClient ActivateAudioClient(int targetProcessId)
    {
        var parameters = new AudioClientActivationParams
        {
            ActivationType = AudioClientActivationType.ProcessLoopback,
            ProcessLoopbackParams = new AudioClientProcessLoopbackParams
            {
                TargetProcessId = (uint)targetProcessId,
                ProcessLoopbackMode = ProcessLoopbackMode.IncludeTargetProcessTree,
            },
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            Marshal.StructureToPtr(parameters, pointer, false);
            var variant = PropVariant.FromBlob(pointer, (uint)Marshal.SizeOf<AudioClientActivationParams>());
            using var handler = new ActivationHandler();
            var audioClientId = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                ref audioClientId,
                ref variant,
                handler,
                out operation));
            return handler.WaitForAudioClient();
        }
        finally
        {
            ReleaseComObject(operation);
            Marshal.FreeHGlobal(pointer);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        try
        {
            if (value is not null && Marshal.IsComObject(value))
            {
                Marshal.ReleaseComObject(value);
            }
        }
        catch (Exception exception) when (exception is InvalidComObjectException or COMException)
        {
        }
    }

    [DllImport("Mmdevapi.dll", EntryPoint = "ActivateAudioInterfaceAsync", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid interfaceId,
        ref PropVariant activationParameters,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public uint TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public uint Size;
        public nint Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public Blob Blob;

        public static PropVariant FromBlob(nint data, uint size) =>
            new() { VariantType = 65, Blob = new Blob { Size = size, Data = data } };
    }

    private enum ProcessLoopbackMode { IncludeTargetProcessTree, ExcludeTargetProcessTree }
    private enum AudioClientActivationType { Default, ProcessLoopback }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort WFormatTag;
        public ushort NChannels;
        public uint NSamplesPerSec;
        public uint NAvgBytesPerSec;
        public ushort NBlockAlign;
        public ushort WBitsPerSample;
        public ushort CbSize;
    }

    [ComVisible(true)]
    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IDisposable
    {
        private readonly ManualResetEventSlim _completed = new(false);
        private Exception? _error;
        private IAudioClient? _audioClient;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out var result, out var activated).ThrowIfFailed();
                result.ThrowIfFailed();
                _audioClient = activated as IAudioClient ?? throw new InvalidCastException("Audio activation did not return IAudioClient.");
            }
            catch (Exception exception)
            {
                _error = exception;
            }
            finally
            {
                _completed.Set();
            }
        }

        public IAudioClient WaitForAudioClient()
        {
            if (!_completed.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Timed out activating process loopback audio.");
            }

            if (_error is not null)
            {
                throw _error;
            }

            return _audioClient ?? throw new InvalidOperationException("Process loopback audio activation failed.");
        }

        public void Dispose() => _completed.Dispose();
    }
}
