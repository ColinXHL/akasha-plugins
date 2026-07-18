namespace AkashaAutomation.BetterGiPort.Compatibility.Audio;

public interface IBetterGiDialogueVoiceDetector : IDisposable
{
    int ProcessId { get; }

    void Reset();

    float Update();
}

public sealed class BetterGiDialogueVoiceDetector : IBetterGiDialogueVoiceDetector
{
    private readonly BetterGiProcessLoopbackAudioCapture _capture;
    private readonly BetterGiSileroVadDetector _vad;
    private readonly List<float> _pendingSamples = [];
    private int _pendingOffset;

    private BetterGiDialogueVoiceDetector(
        int processId,
        BetterGiProcessLoopbackAudioCapture capture,
        BetterGiSileroVadDetector vad)
    {
        ProcessId = processId;
        _capture = capture;
        _vad = vad;
    }

    public int ProcessId { get; }

    public static BetterGiDialogueVoiceDetector Create(int processId, string modelPath)
    {
        BetterGiProcessLoopbackAudioCapture? capture = null;
        BetterGiSileroVadDetector? vad = null;
        try
        {
            vad = new BetterGiSileroVadDetector(modelPath);
            capture = new BetterGiProcessLoopbackAudioCapture(processId);
            return new BetterGiDialogueVoiceDetector(processId, capture, vad);
        }
        catch
        {
            capture?.Dispose();
            vad?.Dispose();
            throw;
        }
    }

    public void Reset()
    {
        _pendingSamples.Clear();
        _pendingOffset = 0;
        _vad.Reset();
        _capture.ReadAvailableSamples(null);
    }

    public float Update()
    {
        _capture.ReadAvailableSamples(_pendingSamples);
        var maximum = 0f;
        while (_pendingSamples.Count - _pendingOffset >= BetterGiSileroVadDetector.FrameSampleCount)
        {
            var frame = new float[BetterGiSileroVadDetector.FrameSampleCount];
            _pendingSamples.CopyTo(_pendingOffset, frame, 0, frame.Length);
            _pendingOffset += frame.Length;
            maximum = Math.Max(maximum, _vad.Predict(frame));
        }

        if (_pendingOffset > 0)
        {
            if (_pendingOffset >= _pendingSamples.Count)
            {
                _pendingSamples.Clear();
            }
            else
            {
                _pendingSamples.RemoveRange(0, _pendingOffset);
            }

            _pendingOffset = 0;
        }

        return maximum;
    }

    public void Dispose()
    {
        _capture.Dispose();
        _vad.Dispose();
    }
}
