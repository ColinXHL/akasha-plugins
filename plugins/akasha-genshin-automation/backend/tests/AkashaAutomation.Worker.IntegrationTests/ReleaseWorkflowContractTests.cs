namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class ReleaseWorkflowContractTests
{
    [Fact]
    public void PublishWorkflow_ShouldReleaseAndPublishCatalogWithoutDispatch()
    {
        var workflow = File.ReadAllText(
            Path.Combine(
                FindRepositoryRoot(),
                ".github",
                "workflows",
                "publish-automation.yml"));

        Assert.Contains(
            "plugins/akasha-genshin-automation/**",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "$env:PLUGIN_ID-v$version",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Publish-Plugin.ps1",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "package_sha256",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "package_size",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Verify GitHub Release readback",
            workflow,
            StringComparison.Ordinal);
        Assert.Contains(
            "Publish verified automation catalog",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "repository_dispatch",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "AKASHA_NAVIGATOR_DISPATCH_TOKEN",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ColinXHL/akasha-automation",
            workflow,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "schemas",
                        "plugin-manifest.schema.json")) &&
                Directory.Exists(
                    Path.Combine(current.FullName, "plugins")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the AkashaPlugins repository root.");
    }
}
