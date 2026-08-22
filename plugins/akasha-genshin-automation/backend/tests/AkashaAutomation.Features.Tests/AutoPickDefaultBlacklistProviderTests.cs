using System.Security.Cryptography;
using System.Text.Json;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Features.AutoPick;

namespace AkashaAutomation.Features.Tests;

public sealed class AutoPickDefaultBlacklistProviderTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"akasha-blacklist-resource-{Guid.NewGuid():N}");

    [Fact]
    public void ValidVerifiedExternalResource_ShouldReplacePackagedSnapshot()
    {
        var entries = Enumerable.Range(0, 1000).Select(index => $"外部名单-{index}").ToArray();
        WriteExternalResource(entries, revision: "revision-one");

        using var provider = CreateProvider();

        Assert.True(provider.Current.IsExternal);
        Assert.Equal("revision-one", provider.Current.Revision);
        Assert.Equal("0.64.0", provider.Current.SourceVersion);
        Assert.Equal(entries, provider.Current.Entries);
    }

    [Fact]
    public void HashMismatch_ShouldKeepPackagedSnapshot()
    {
        var entries = Enumerable.Range(0, 1000).Select(index => $"外部名单-{index}").ToArray();
        WriteExternalResource(entries, revision: "revision-one", declaredHash: new string('a', 64));

        using var provider = CreateProvider();

        Assert.False(provider.Current.IsExternal);
        Assert.Equal("packaged", provider.Current.Revision);
        Assert.True(provider.Current.Entries.Count >= 1000);
    }

    [Fact]
    public async Task AtomicStateChange_ShouldHotReloadNewVerifiedSnapshot()
    {
        using var provider = CreateProvider();
        Assert.False(provider.Current.IsExternal);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Changed += (_, _) => changed.TrySetResult();
        var entries = Enumerable.Range(0, 1000).Select(index => $"热更新-{index}").ToArray();

        WriteExternalResource(entries, revision: "revision-two");
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(provider.Current.IsExternal);
        Assert.Equal("revision-two", provider.Current.Revision);
        Assert.Equal(entries, provider.Current.Entries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private AutoPickDefaultBlacklistProvider CreateProvider() =>
        new(new RootedAssetPathResolver(AppContext.BaseDirectory), _dataDirectory);

    private void WriteExternalResource(
        IReadOnlyList<string> entries,
        string revision,
        string? declaredHash = null)
    {
        var resourceDirectory = Path.Combine(
            _dataDirectory,
            AutoPickDefaultBlacklistProvider.ResourceId);
        Directory.CreateDirectory(resourceDirectory);
        var currentPath = Path.Combine(resourceDirectory, "current.json");
        var payload = JsonSerializer.SerializeToUtf8Bytes(entries);
        File.WriteAllBytes(currentPath, payload);
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var state = new
        {
            schemaVersion = 1,
            resources = new Dictionary<string, object>
            {
                [AutoPickDefaultBlacklistProvider.ResourceId] = new
                {
                    revision,
                    sourceVersion = "0.64.0",
                    sha256 = declaredHash ?? hash,
                    size = payload.Length,
                    fileName = "current.json",
                    updatedAtUtc = DateTimeOffset.UtcNow,
                },
            },
        };
        Directory.CreateDirectory(_dataDirectory);
        var statePath = Path.Combine(_dataDirectory, "resource-state.json");
        var temporaryPath = statePath + ".tmp";
        File.WriteAllBytes(temporaryPath, JsonSerializer.SerializeToUtf8Bytes(state));
        File.Move(temporaryPath, statePath, overwrite: true);
    }
}
