using System.Diagnostics;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Ocr;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using YamlDotNet.Serialization;

namespace AkashaAutomation.BetterGiPort.Compatibility.Ocr;

/// <summary>
/// Minimal PP-OCRv4 ONNX runtime used by the Akasha adapter. The preprocessing,
/// DB thresholding and CTC decoding stay aligned with the pinned BetterGI baseline.
/// </summary>
public sealed class PaddleOnnxOcrSessionFactory : IPaddleOcrSessionFactory
{
    public IPaddleOcrSession Create(PaddleOcrModelOptions options) => new PaddleOnnxOcrSession(options);
}

internal sealed class PaddleOnnxOcrSession : IPaddleOcrSession
{
    private const int DetectionSideLimit = 960;
    private const float DetectionThreshold = 0.3f;
    private const float BoxScoreThreshold = 0.6f;
    private readonly InferenceSession _detectionSession;
    private readonly InferenceSession _recognitionSession;
    private readonly IReadOnlyList<string> _labels;
    private bool _disposed;

    internal PaddleOnnxOcrSession(PaddleOcrModelOptions options)
    {
        options.ValidateFiles();
        using var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
        };
        _detectionSession = new InferenceSession(options.DetectionModelPath, sessionOptions);
        _recognitionSession = new InferenceSession(options.RecognitionModelPath, sessionOptions);
        _labels = LoadLabels(options.RecognitionConfigPath);
    }

    public OcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Empty())
        {
            return OcrResult.Empty();
        }

        var started = Stopwatch.GetTimestamp();
        using var source = EnsureBgr(image);
        var boxes = Detect(source, cancellationToken);
        var regions = new List<OcrTextRegion>(boxes.Count);
        foreach (var box in boxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var crop = new Mat(source, new Rect(box.Region.X, box.Region.Y, box.Region.Width, box.Region.Height));
            var recognized = RecognizeCrop(crop, cancellationToken);
            if (!string.IsNullOrEmpty(recognized.Text))
            {
                regions.Add(new OcrTextRegion(
                    recognized.Text,
                    Math.Min(box.Score, recognized.Score),
                    box.Region));
            }
        }

        var duration = Stopwatch.GetElapsedTime(started);
        return new OcrResult(
            string.Concat(regions.Select(region => region.Text)),
            regions,
            duration);
    }

    public OcrResult RecognizeSingleLine(Mat image, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        if (image.Empty())
        {
            return OcrResult.Empty();
        }

        var started = Stopwatch.GetTimestamp();
        using var source = EnsureBgr(image);
        var recognized = RecognizeCrop(source, cancellationToken);
        var regions = string.IsNullOrEmpty(recognized.Text)
            ? []
            : new[]
            {
                new OcrTextRegion(
                    recognized.Text,
                    recognized.Score,
                    new RegionOfInterest(0, 0, source.Width, source.Height)),
            };
        return new OcrResult(recognized.Text, regions, Stopwatch.GetElapsedTime(started));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _detectionSession.Dispose();
        _recognitionSession.Dispose();
    }

    private IReadOnlyList<DetectedBox> Detect(Mat source, CancellationToken cancellationToken)
    {
        using var resized = ResizeForDetection(source);
        var tensor = CreateDetectionTensor(resized);
        cancellationToken.ThrowIfCancellationRequested();
        using var results = _detectionSession.Run(
            [NamedOnnxValue.CreateFromTensor(_detectionSession.InputNames[0], tensor)]);
        var output = results[0].AsTensor<float>();
        if (output.Dimensions.Length != 4 || output.Dimensions[0] != 1 || output.Dimensions[1] != 1)
        {
            throw new InvalidDataException($"Unexpected Paddle detection output: {string.Join('x', output.Dimensions.ToArray())}");
        }

        using var probability = new Mat(output.Dimensions[2], output.Dimensions[3], MatType.CV_32FC1);
        output.ToArray().AsSpan().CopyTo(probability.AsSpan<float>());
        using var binary = probability.Threshold(DetectionThreshold, 1, ThresholdTypes.Binary);
        binary.ConvertTo(binary, MatType.CV_8UC1, 255);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        using var dilated = new Mat();
        Cv2.Dilate(binary, dilated, kernel);
        var contours = dilated.FindContoursAsArray(RetrievalModes.List, ContourApproximationModes.ApproxSimple);
        var scaleX = (double)source.Width / probability.Width;
        var scaleY = (double)source.Height / probability.Height;
        var boxes = new List<DetectedBox>();
        foreach (var contour in contours)
        {
            var score = ScoreContour(contour, probability);
            if (score < BoxScoreThreshold)
            {
                continue;
            }

            var rect = Cv2.BoundingRect(contour);
            if (rect.Width < 3 || rect.Height < 3)
            {
                continue;
            }

            var padding = Math.Max(1, Math.Min(rect.Width, rect.Height) / 4);
            var left = Math.Clamp((int)Math.Floor((rect.Left - padding) * scaleX), 0, source.Width - 1);
            var top = Math.Clamp((int)Math.Floor((rect.Top - padding) * scaleY), 0, source.Height - 1);
            var right = Math.Clamp((int)Math.Ceiling((rect.Right + padding) * scaleX), left + 1, source.Width);
            var bottom = Math.Clamp((int)Math.Ceiling((rect.Bottom + padding) * scaleY), top + 1, source.Height);
            boxes.Add(new DetectedBox(new RegionOfInterest(left, top, right - left, bottom - top), score));
        }

        return boxes
            .OrderBy(box => box.Region.Y)
            .ThenBy(box => box.Region.X)
            .ToArray();
    }

    private RecognizedText RecognizeCrop(Mat crop, CancellationToken cancellationToken)
    {
        var targetWidth = Math.Clamp((int)Math.Ceiling(crop.Width / (double)crop.Height * 48), 8, 3200);
        using var resized = crop.Resize(new Size(targetWidth, 48));
        using var blob = CvDnn.BlobFromImage(
            resized,
            2d / 255d,
            default,
            new Scalar(127.5, 127.5, 127.5),
            swapRB: false,
            crop: false);
        var tensor = new DenseTensor<float>(blob.AsSpan<float>().ToArray(), [1, 3, 48, targetWidth]);
        cancellationToken.ThrowIfCancellationRequested();
        using var results = _recognitionSession.Run(
            [NamedOnnxValue.CreateFromTensor(_recognitionSession.InputNames[0], tensor)]);
        var output = results[0].AsTensor<float>();
        if (output.Dimensions.Length != 3 || output.Dimensions[0] != 1)
        {
            throw new InvalidDataException($"Unexpected Paddle recognition output: {string.Join('x', output.Dimensions.ToArray())}");
        }

        var steps = output.Dimensions[1];
        var classes = output.Dimensions[2];
        var values = output.ToArray();
        var text = new System.Text.StringBuilder();
        var previous = 0;
        double score = 0;
        var accepted = 0;
        for (var step = 0; step < steps; step++)
        {
            var offset = step * classes;
            var bestIndex = 0;
            var bestScore = float.MinValue;
            for (var character = 0; character < classes; character++)
            {
                var value = values[offset + character];
                if (value > bestScore)
                {
                    bestScore = value;
                    bestIndex = character;
                }
            }

            if (bestIndex > 0 && bestIndex != previous)
            {
                text.Append(GetLabel(bestIndex));
                score += bestScore;
                accepted++;
            }

            previous = bestIndex;
        }

        return new RecognizedText(text.ToString(), accepted == 0 ? 0 : score / accepted);
    }

    private static DenseTensor<float> CreateDetectionTensor(Mat resized)
    {
        using var normalized = new Mat();
        var channels = resized.Split();
        try
        {
            var mean = new[] { 0.485f, 0.456f, 0.406f };
            var standardDeviation = new[] { 0.229f, 0.224f, 0.225f };
            const float scale = 1f / 255f;
            for (var index = 0; index < channels.Length; index++)
            {
                channels[index].ConvertTo(
                    channels[index],
                    MatType.CV_32FC1,
                    1f / standardDeviation[index],
                    -mean[index] / standardDeviation[index] / scale);
            }

            Cv2.Merge(channels, normalized);
        }
        finally
        {
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }

        using var blob = CvDnn.BlobFromImage(normalized, 1d / 255d);
        return new DenseTensor<float>(
            blob.AsSpan<float>().ToArray(),
            [1, 3, resized.Height, resized.Width]);
    }

    private static Mat ResizeForDetection(Mat source)
    {
        var ratio = Math.Max(source.Width, source.Height) > DetectionSideLimit
            ? DetectionSideLimit / (double)Math.Max(source.Width, source.Height)
            : 1d;
        var width = Math.Max(32, (int)Math.Round(source.Width * ratio / 32d) * 32);
        var height = Math.Max(32, (int)Math.Round(source.Height * ratio / 32d) * 32);
        return source.Resize(new Size(width, height));
    }

    private static float ScoreContour(Point[] contour, Mat probability)
    {
        var bounds = Cv2.BoundingRect(contour);
        bounds = bounds.Intersect(new Rect(0, 0, probability.Width, probability.Height));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return 0;
        }

        var points = contour.Select(point => new Point(point.X - bounds.X, point.Y - bounds.Y)).ToArray();
        using var mask = new Mat(bounds.Height, bounds.Width, MatType.CV_8UC1, Scalar.Black);
        mask.FillPoly([points], Scalar.White);
        using var crop = new Mat(probability, bounds);
        return (float)crop.Mean(mask).Val0;
    }

    private static Mat EnsureBgr(Mat image) => image.Channels() switch
    {
        1 => image.CvtColor(ColorConversionCodes.GRAY2BGR),
        3 => image.Clone(),
        4 => image.CvtColor(ColorConversionCodes.BGRA2BGR),
        _ => throw new ArgumentException($"Paddle OCR expects 1, 3, or 4 channels; got {image.Channels()}.", nameof(image)),
    };

    private static IReadOnlyList<string> LoadLabels(string configPath)
    {
        using var reader = File.OpenText(configPath);
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(reader);
        var postProcess = GetMap(root, "PostProcess");
        if (!postProcess.TryGetValue("character_dict", out var labelsValue) ||
            labelsValue is not IEnumerable<object> labelValues)
        {
            throw new InvalidDataException("Paddle recognition config does not contain PostProcess.character_dict.");
        }

        var labels = labelValues.Select(value => Convert.ToString(value) ?? string.Empty).ToArray();
        return labels.Length > 0
            ? labels
            : throw new InvalidDataException("Paddle recognition dictionary is empty.");
    }

    private static Dictionary<object, object> GetMap(Dictionary<object, object> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is not Dictionary<object, object> nested)
        {
            throw new InvalidDataException($"Paddle config does not contain '{key}'.");
        }

        return nested;
    }

    private string GetLabel(int index)
    {
        if (index <= _labels.Count)
        {
            return _labels[index - 1];
        }

        if (index == _labels.Count + 1)
        {
            return " ";
        }

        throw new InvalidDataException($"Paddle recognition index {index} exceeds dictionary size {_labels.Count}.");
    }

    private sealed record DetectedBox(RegionOfInterest Region, double Score);

    private sealed record RecognizedText(string Text, double Score);
}
