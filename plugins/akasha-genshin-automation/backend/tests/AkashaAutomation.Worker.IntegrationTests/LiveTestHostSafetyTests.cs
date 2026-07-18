using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.LiveTestHost;
using AkashaAutomation.Tests;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class LiveTestHostSafetyTests
{
    [Fact]
    public void MenuOptions_ShouldExposeOnlyPickAndDialogueSwitchesAtBetterGiCadence()
    {
        var options = new LiveTestHostOptions(AutoPickEnabled: true, AutoDialogueEnabled: false);

        Assert.True(options.AutoPickEnabled);
        Assert.False(options.AutoDialogueEnabled);
        Assert.Equal(50, options.IntervalMilliseconds);
    }

    [Fact]
    public async Task InputWrapper_ShouldCountEveryExecutedGroupWithoutStoppingTheSession()
    {
        await using var inner = new RecordingInputService();
        await using var input = new LiveInputService(inner);
        var context = new GameContextSnapshot(
            DateTimeOffset.UnixEpoch,
            new GameWindowInfo(1, 1, "GenshinImpact", "Genshin Impact", new CaptureSize(1920, 1080), true));

        for (var index = 0; index < 3; index++)
        {
            await input.ExecuteAsync(
                new InputActionGroup("test", [InputAction.KeyPress(0x46)]),
                context);
        }

        Assert.Equal(3, input.ExecutedGroups);
        Assert.Equal(3, inner.Recordings.Count);
    }

    [Fact]
    public async Task EmergencyStopMonitor_ShouldCancelWhenChordIsDetected()
    {
        using var cancellation = new CancellationTokenSource();

        await GlobalEmergencyStopMonitor.RunAsync(
            cancellation,
            _ => true,
            TimeSpan.Zero);

        Assert.True(cancellation.IsCancellationRequested);
    }

    [Fact]
    public void LiveTestHostProject_ShouldBeIndependentFromDevHostWorkerAndNavigator()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.LiveTestHost", "AkashaAutomation.LiveTestHost.csproj");

        Assert.Equal(
            ["AkashaAutomation.BetterGiPort", "AkashaAutomation.Core", "AkashaAutomation.Features"],
            references);
    }

    [Fact]
    public void LiveTestHostSource_ShouldKeepForegroundEmergencyStopAndAdministratorBoundaries()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src", "AkashaAutomation.LiveTestHost");
        var source = string.Join(
            '\n',
            Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Contains("WindowsSendInputService", source, StringComparison.Ordinal);
        Assert.Contains("Ctrl+Alt+F12", source, StringComparison.Ordinal);
        Assert.Contains("EmergencyStopAsync", source, StringComparison.Ordinal);
        Assert.Contains("ProcessElevation.IsAdministrator", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--arm-live-input", source, StringComparison.Ordinal);
        Assert.DoesNotContain("--max-actions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("优先选择的选项文字", source, StringComparison.Ordinal);
        Assert.DoesNotContain("等待角色语音", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AkashaAutomation.Worker", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NamedPipe", source, StringComparison.Ordinal);
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
