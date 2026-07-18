using System.Text.Json;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class PluginPackageContractTests
{
    [Fact]
    public void PluginManifest_DeclaresFixedCompanionExecutableAndProtocol()
    {
        var pluginRoot = FindPluginRoot();
        var manifestPath = Path.Combine(pluginRoot, "manifest.json");
        var mainPath = Path.Combine(pluginRoot, "frontend", "main.js");

        Assert.True(
            File.Exists(manifestPath),
            $"Plugin manifest was not found: {manifestPath}");
        Assert.True(
            File.Exists(mainPath),
            $"Plugin entry point was not found: {mainPath}");

        using var document =
            JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        var permissions = root.GetProperty("permissions")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        var distribution = root.GetProperty("distribution");
        var backend = root.GetProperty("backend");

        Assert.Equal(2, root.GetProperty("manifestVersion").GetInt32());
        Assert.Equal(
            "frontend/main.js",
            root.GetProperty("main").GetString());
        Assert.Contains("companion", permissions);
        Assert.Contains("hotkey", permissions);
        Assert.Equal("release", distribution.GetProperty("type").GetString());
        Assert.Equal(
            "akasha-genshin-automation-v0.4.4",
            distribution.GetProperty("tag").GetString());
        Assert.Equal(
            "akasha-genshin-automation-0.4.4-win-x64.zip",
            distribution.GetProperty("asset").GetString());
        Assert.Equal(
            "runtime/AkashaAutomation.Worker.exe",
            backend.GetProperty("entry").GetString());
        Assert.Equal(1, backend.GetProperty("protocolVersion").GetInt32());
        Assert.Equal("plugin", backend.GetProperty("lifetime").GetString());
        Assert.Equal(
            "inherit",
            backend.GetProperty("integrityLevel").GetString());
        Assert.Equal(
            5000,
            backend.GetProperty("shutdownTimeoutMs").GetInt32());
    }

    [Fact]
    public void SettingsUi_DefaultsAndMainScript_ShouldStayInSyncWithManifest()
    {
        var pluginRoot = FindPluginRoot();
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(pluginRoot, "manifest.json")));
        using var settings = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    pluginRoot,
                    "frontend",
                    "settings_ui.json")));
        var defaults =
            manifest.RootElement.GetProperty("defaultConfig");
        var settingsJson = settings.RootElement.GetRawText();
        Assert.Contains(
            "runtime/Assets/Config/Pick",
            settingsJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "worker/win-x64",
            settingsJson,
            StringComparison.Ordinal);
        var keyedItems =
            new List<(string Key, string DefaultJson)>();

        foreach (var section in settings.RootElement
                     .GetProperty("sections")
                     .EnumerateArray())
        {
            CollectKeyedItems(
                section.GetProperty("items"),
                keyedItems);
        }

        Assert.Equal(
            keyedItems.Count,
            keyedItems
                .Select(item => item.Key)
                .Distinct(StringComparer.Ordinal)
                .Count());
        foreach (var item in keyedItems)
        {
            Assert.True(
                defaults.TryGetProperty(
                    item.Key,
                    out var manifestDefault),
                $"Missing manifest default: {item.Key}");
            Assert.Equal(
                item.DefaultJson,
                manifestDefault.GetRawText());
        }

        var script = File.ReadAllText(
            Path.Combine(pluginRoot, "frontend", "main.js"));
        Assert.Contains(
            "features.autoPick.setOptions",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "features.autoDialogue.setOptions",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "buildAutoPickOptions",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "buildAutoDialogueOptions",
            script,
            StringComparison.Ordinal);
        Assert.Contains("autoPickHotkey", script, StringComparison.Ordinal);
        Assert.Contains(
            "autoDialogueHotkey",
            script,
            StringComparison.Ordinal);
        Assert.Contains("hotkey.register", script, StringComparison.Ordinal);
        Assert.Contains(
            "hotkey.unregisterAll",
            script,
            StringComparison.Ordinal);
        Assert.Contains("osd.show", script, StringComparison.Ordinal);
        Assert.Contains("已关闭", script, StringComparison.Ordinal);
        Assert.DoesNotContain("正在启用", script, StringComparison.Ordinal);
        Assert.DoesNotContain("正在关闭", script, StringComparison.Ordinal);
        Assert.Contains("切换失败", script, StringComparison.Ordinal);
    }

    private static void CollectKeyedItems(
        JsonElement items,
        ICollection<(string Key, string DefaultJson)> destination)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("key", out var key))
            {
                destination.Add(
                    (key.GetString()!,
                     item.GetProperty("default").GetRawText()));
            }

            if (item.TryGetProperty("items", out var children))
            {
                CollectKeyedItems(children, destination);
            }
        }
    }

    private static string FindPluginRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var manifestPath =
                Path.Combine(directory.FullName, "manifest.json");
            var backendSolution = Path.Combine(
                directory.FullName,
                "backend",
                "AkashaAutomation.sln");
            if (File.Exists(manifestPath) &&
                File.Exists(backendSolution))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Akasha Automation plugin root not found.");
    }
}
