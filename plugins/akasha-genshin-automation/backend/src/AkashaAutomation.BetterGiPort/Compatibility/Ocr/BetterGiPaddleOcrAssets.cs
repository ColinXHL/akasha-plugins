using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Ocr;

namespace AkashaAutomation.BetterGiPort.Compatibility.Ocr;

public static class BetterGiPaddleOcrAssets
{
    public static PaddleOcrModelOptions CreateV4Options(IAssetPathResolver assetPathResolver)
    {
        ArgumentNullException.ThrowIfNull(assetPathResolver);
        return new PaddleOcrModelOptions(
            assetPathResolver.Resolve(BetterGiAssetPaths.PaddleOcrV4DetectionModel),
            assetPathResolver.Resolve(BetterGiAssetPaths.PaddleOcrV4RecognitionModel),
            assetPathResolver.Resolve(BetterGiAssetPaths.PaddleOcrV4RecognitionConfig));
    }
}
