using System.Security.Cryptography;
using System.Text.Json;
using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Features.AutoPick;

public sealed class AutoPickDefaultBlacklistProvider :
    IAutoPickDefaultBlacklistProvider,
    IDisposable
{
    public const string ResourceId = "bettergi-default-pick-blacklist";
    private const string ResourceStateFileName = "resource-state.json";
    private const int MinimumEntryCount = 1000;
    private const int MaximumEntryCount = 20000;
    private const int MaximumEntryLength = 200;
    private const long MaximumFileBytes = 2L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string? _pluginDataDirectory;
    private readonly AutoPickDefaultBlacklistSnapshot _packaged;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer? _reloadTimer;
    private AutoPickDefaultBlacklistSnapshot _current;
    private bool _disposed;

    public AutoPickDefaultBlacklistProvider(
        IAssetPathResolver assetPathResolver,
        string? pluginDataDirectory)
    {
        ArgumentNullException.ThrowIfNull(assetPathResolver);
        _pluginDataDirectory = NormalizeDataDirectory(pluginDataDirectory);
        _packaged = LoadPackaged(assetPathResolver);
        _current = TryLoadExternal(out var external) ? external! : _packaged;

        if (_pluginDataDirectory != null)
        {
            Directory.CreateDirectory(_pluginDataDirectory);
            _reloadTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(_pluginDataDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnResourceChanged;
            _watcher.Created += OnResourceChanged;
            _watcher.Renamed += OnResourceChanged;
            _watcher.Deleted += OnResourceChanged;
        }
    }

    public AutoPickDefaultBlacklistSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnResourceChanged;
            _watcher.Created -= OnResourceChanged;
            _watcher.Renamed -= OnResourceChanged;
            _watcher.Deleted -= OnResourceChanged;
            _watcher.Dispose();
        }

        _reloadTimer?.Dispose();
    }

    private void OnResourceChanged(object sender, FileSystemEventArgs args)
    {
        if (!IsRelevantPath(args.FullPath))
        {
            return;
        }

        try
        {
            _reloadTimer?.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsRelevantPath(string path)
    {
        if (_pluginDataDirectory == null)
        {
            return false;
        }

        var statePath = Path.Combine(_pluginDataDirectory, ResourceStateFileName);
        var resourceDirectory = Path.Combine(_pluginDataDirectory, ResourceId);
        return string.Equals(path, statePath, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(
                   resourceDirectory + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void Reload()
    {
        AutoPickDefaultBlacklistSnapshot next;
        if (!TryLoadExternal(out var external))
        {
            // A transient or corrupt remote update must not replace the last
            // known-good external snapshot. Only packaged data is used when no
            // external snapshot has ever been accepted.
            lock (_gate)
            {
                if (_current.IsExternal)
                {
                    return;
                }

                next = _packaged;
            }
        }
        else
        {
            next = external!;
        }

        var changed = false;
        lock (_gate)
        {
            if (_disposed ||
                string.Equals(_current.Sha256, next.Sha256, StringComparison.Ordinal) &&
                string.Equals(_current.Revision, next.Revision, StringComparison.Ordinal) &&
                string.Equals(
                    _current.SourceVersion,
                    next.SourceVersion,
                    StringComparison.Ordinal) &&
                _current.IsExternal == next.IsExternal)
            {
                return;
            }

            _current = next;
            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool TryLoadExternal(out AutoPickDefaultBlacklistSnapshot? snapshot)
    {
        snapshot = null;
        if (_pluginDataDirectory == null)
        {
            return false;
        }

        try
        {
            var statePath = Path.Combine(_pluginDataDirectory, ResourceStateFileName);
            if (!File.Exists(statePath))
            {
                return false;
            }

            if (new FileInfo(statePath).Length is <= 0 or > 1024 * 1024)
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(statePath));
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) ||
                schema.GetInt32() != 1 ||
                !document.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Object ||
                !resources.TryGetProperty(ResourceId, out var resource))
            {
                return false;
            }

            var revision = RequiredString(resource, "revision");
            var sourceVersion = RequiredString(resource, "sourceVersion");
            var expectedHash = RequiredString(resource, "sha256");
            var expectedSize = resource.GetProperty("size").GetInt64();
            var fileName = RequiredString(resource, "fileName");
            if (revision.Length > 128 ||
                sourceVersion.Length > 128 ||
                !IsSha256(expectedHash) ||
                expectedSize is <= 0 or > MaximumFileBytes ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                !string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var resourceDirectory = Path.GetFullPath(
                Path.Combine(_pluginDataDirectory, ResourceId));
            var filePath = Path.GetFullPath(Path.Combine(resourceDirectory, fileName));
            if (!filePath.StartsWith(
                    resourceDirectory + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(filePath))
            {
                return false;
            }

            var info = new FileInfo(filePath);
            if (info.Length != expectedSize)
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                return false;
            }

            var entries = BetterGiJsonList.Load(filePath);
            if (!IsValidEntries(entries))
            {
                return false;
            }

            snapshot = new AutoPickDefaultBlacklistSnapshot(
                entries.ToArray(),
                revision,
                sourceVersion,
                expectedHash,
                IsExternal: true);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or KeyNotFoundException or FormatException or
                InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    private static AutoPickDefaultBlacklistSnapshot LoadPackaged(
        IAssetPathResolver assetPathResolver)
    {
        var path = assetPathResolver.Resolve(BetterGiAssetPaths.DefaultPickBlacklist);
        var entries = BetterGiJsonList.Load(path);
        if (!IsValidEntries(entries))
        {
            throw new InvalidDataException("Packaged default pickup blacklist is invalid.");
        }

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new AutoPickDefaultBlacklistSnapshot(
            entries.ToArray(),
            "packaged",
            "0.63.0",
            hash,
            IsExternal: false);
    }

    private static bool IsValidEntries(IReadOnlyList<string> entries) =>
        entries.Count is >= MinimumEntryCount and <= MaximumEntryCount &&
        entries.All(
            value =>
                !string.IsNullOrWhiteSpace(value) &&
                value.Length <= MaximumEntryLength &&
                !value.Any(char.IsControl));

    private static string RequiredString(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Resource state property '{propertyName}' is empty.")
            : value;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? NormalizeDataDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(value);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
