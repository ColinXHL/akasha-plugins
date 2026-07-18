using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Core.Recognition;

public sealed class RootedAssetPathResolver : IAssetPathResolver
{
    private readonly string _rootWithSeparator;

    public RootedAssetPathResolver(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public string Resolve(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Asset paths must be relative.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootWithSeparator, relativePath));
        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Asset path escapes the configured root.", nameof(relativePath));
        }

        return fullPath;
    }
}
