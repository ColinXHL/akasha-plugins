namespace AkashaAutomation.Core.Ocr;

public sealed record PaddleOcrModelOptions(
    string DetectionModelPath,
    string RecognitionModelPath,
    string RecognitionConfigPath)
{
    public void ValidateFiles()
    {
        ValidateFile(DetectionModelPath, nameof(DetectionModelPath));
        ValidateFile(RecognitionModelPath, nameof(RecognitionModelPath));
        ValidateFile(RecognitionConfigPath, nameof(RecognitionConfigPath));
    }

    private static void ValidateFile(string path, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Paddle OCR asset was not found: {path}", path);
        }
    }
}
