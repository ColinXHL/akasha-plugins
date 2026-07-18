using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AkashaAutomation.BetterGiPort.Compatibility.Audio;

public sealed class BetterGiSileroVadDetector : IDisposable
{
    public const int SampleRate = 16_000;
    public const int FrameSampleCount = 512;
    private readonly InferenceSession _session;
    private readonly float[] _state = new float[2 * 1 * 128];
    private readonly long[] _sampleRate = [SampleRate];

    public BetterGiSileroVadDetector(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Silero VAD model is unavailable.", modelPath);
        }

        _session = new InferenceSession(
            modelPath,
            new SessionOptions
            {
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
                InterOpNumThreads = 1,
                IntraOpNumThreads = 1,
            });
    }

    public void Reset() => Array.Clear(_state);

    public float Predict(float[] samples)
    {
        if (samples.Length != FrameSampleCount)
        {
            throw new ArgumentException($"Silero VAD requires {FrameSampleCount} samples.", nameof(samples));
        }

        var input = new DenseTensor<float>(samples, [1, FrameSampleCount]);
        var state = new DenseTensor<float>(_state, [2, 1, 128]);
        var sampleRate = new DenseTensor<long>(_sampleRate, []);
        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("input", input),
            NamedOnnxValue.CreateFromTensor("state", state),
            NamedOnnxValue.CreateFromTensor("sr", sampleRate),
        ]);
        var probability = results.First(result => result.Name == "output").AsEnumerable<float>().FirstOrDefault();
        var index = 0;
        foreach (var value in results.First(result => result.Name == "stateN").AsEnumerable<float>())
        {
            if (index >= _state.Length)
            {
                break;
            }

            _state[index++] = value;
        }

        return Math.Clamp(probability, 0f, 1f);
    }

    public void Dispose() => _session.Dispose();
}
