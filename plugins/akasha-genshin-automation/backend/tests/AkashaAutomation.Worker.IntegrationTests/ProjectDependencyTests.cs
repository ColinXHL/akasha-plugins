using AkashaAutomation.Tests;

namespace AkashaAutomation.Worker.IntegrationTests;

public class ProjectDependencyTests
{
    [Fact]
    public void WorkerProject_ShouldReferenceCoreAndFeatures()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.Worker", "AkashaAutomation.Worker.csproj");

        Assert.Equal(["AkashaAutomation.Core", "AkashaAutomation.Features"], references);
    }
}
