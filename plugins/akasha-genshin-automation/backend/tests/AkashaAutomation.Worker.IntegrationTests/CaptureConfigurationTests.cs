using System.Xml.Linq;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class CaptureConfigurationTests
{
    [Fact]
    public void BitBltCaptureSource_ShouldUsePersistentGameWindowDeviceContext()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "AkashaAutomation.Core",
                "Capture",
                "WindowsBitBltCaptureSource.cs"));

        Assert.Contains("NativeMethods.GetDC(_windowHandle)", source, StringComparison.Ordinal);
        Assert.Contains("CreateDIBSection", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.GdiFlush()", source, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.SourceCopy", source, StringComparison.Ordinal);
        Assert.Contains("CaptureSession? _session", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetDC(nint.Zero)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientToScreen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CaptureLayeredWindows", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutableProjects_ShouldSharePerMonitorV2Manifest()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "build", "AkashaAutomation.app.manifest");
        var manifest = XDocument.Load(manifestPath);

        Assert.Equal(
            "PerMonitorV2",
            manifest.Descendants().Single(element => element.Name.LocalName == "dpiAwareness").Value);
        Assert.Equal(
            "true/PM",
            manifest.Descendants().Single(element => element.Name.LocalName == "dpiAware").Value);

        foreach (var projectName in new[]
                 {
                     "AkashaAutomation.Worker",
                     "AkashaAutomation.DevHost",
                     "AkashaAutomation.LiveTestHost",
                 })
        {
            var projectPath = Path.Combine(root, "src", projectName, $"{projectName}.csproj");
            var project = XDocument.Load(projectPath);
            var configuredManifest = project
                .Descendants()
                .Single(element => element.Name.LocalName == "ApplicationManifest")
                .Value;
            var resolvedManifest = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(projectPath)!, configuredManifest));

            Assert.Equal(manifestPath, resolvedManifest);
        }
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
