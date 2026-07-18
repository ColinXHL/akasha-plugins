using OpenCvSharp;

namespace AkashaAutomation.Core.Ocr;

public interface IPaddleOcrSession : IDisposable
{
    OcrResult Recognize(Mat image, CancellationToken cancellationToken = default);

    OcrResult RecognizeSingleLine(Mat image, CancellationToken cancellationToken = default) =>
        Recognize(image, cancellationToken);
}

public interface IPaddleOcrSessionFactory
{
    IPaddleOcrSession Create(PaddleOcrModelOptions options);
}
