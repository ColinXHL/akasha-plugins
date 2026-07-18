using System.Security.Cryptography;

namespace AkashaAutomation.BetterGiPort.Assets;

public static class BetterGiAssetIntegrity
{
    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void VerifySha256(string path, string expectedSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        var actualSha256 = ComputeSha256(path);
        if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"BetterGI asset hash mismatch for '{path}'. Expected {expectedSha256}, got {actualSha256}.");
        }
    }
}
