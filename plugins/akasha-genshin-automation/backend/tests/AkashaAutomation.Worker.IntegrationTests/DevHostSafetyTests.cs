using AkashaAutomation.DevHost;
using AkashaAutomation.Tests;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class DevHostSafetyTests
{
    [Fact]
    public void Options_ShouldDefaultToSafeObserveOnlyConfiguration()
    {
        var options = DevHostOptions.Parse([]);

        Assert.Equal("F", options.PickKey);
        Assert.Equal("auto-pick", options.Feature);
        Assert.Equal(50, options.IntervalMilliseconds);
        Assert.True(options.BlacklistEnabled);
        Assert.False(options.ShowAllFrames);
        Assert.Empty(options.UserExactBlacklist);
        Assert.Empty(options.UserFuzzyBlacklist);
        Assert.Empty(options.UserWhitelist);
    }

    [Fact]
    public void Options_ShouldParseAutoDialogueObserveMode()
    {
        var options = DevHostOptions.Parse(
        [
            "--feature", "auto-dialogue",
            "--option-strategy", "last",
            "--custom-option", "优先项",
            "--advance-key", "interaction",
            "--voice-wait",
            "--hangout-ending", "测试结局",
        ]);

        Assert.Equal("auto-dialogue", options.Feature);
        Assert.Equal("Last", options.OptionStrategy);
        Assert.Equal(["优先项"], options.CustomPriorityOptions);
        Assert.Equal("Interaction", options.AdvanceKey);
        Assert.True(options.VoiceWaitEnabled);
        Assert.Equal("测试结局", options.HangoutEnding);
    }

    [Fact]
    public void Options_ShouldParseRepeatableListsAndNormalizeKey()
    {
        var options = DevHostOptions.Parse(
        [
            "--pick-key", "e",
            "--interval-ms", "250",
            "--no-blacklist",
            "--show-all",
            "--exact-blacklist", "机关",
            "--fuzzy-blacklist", "测试",
            "--whitelist", "与凯瑟琳对话",
        ]);

        Assert.Equal("E", options.PickKey);
        Assert.Equal(250, options.IntervalMilliseconds);
        Assert.False(options.BlacklistEnabled);
        Assert.True(options.ShowAllFrames);
        Assert.Equal(["机关"], options.UserExactBlacklist);
        Assert.Equal(["测试"], options.UserFuzzyBlacklist);
        Assert.Equal(["与凯瑟琳对话"], options.UserWhitelist);
    }

    [Theory]
    [InlineData("--pick-key", "Q")]
    [InlineData("--interval-ms", "24")]
    [InlineData("--interval-ms", "2001")]
    [InlineData("--unknown", "value")]
    public void Options_ShouldRejectUnsafeOrUnknownArguments(string argument, string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => DevHostOptions.Parse([argument, value]));
    }

    [Fact]
    public void DevHostProject_ShouldBeIndependentFromWorkerAndNavigator()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.DevHost", "AkashaAutomation.DevHost.csproj");

        Assert.Equal(
            ["AkashaAutomation.BetterGiPort", "AkashaAutomation.Core", "AkashaAutomation.Features"],
            references);
    }

    [Fact]
    public void DevHostSource_ShouldContainNoRealInputOrCompanionDependency()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src", "AkashaAutomation.DevHost");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("WindowsSendInputService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AkashaAutomation.Worker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipe", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--token", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AkashaAutomation.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
