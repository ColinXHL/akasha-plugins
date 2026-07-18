using AkashaAutomation.Tests;

namespace AkashaAutomation.Core.Tests;

public class ProjectDependencyTests
{
    [Fact]
    public void CoreProject_ShouldNotReferenceOtherSourceProjects()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.Core", "AkashaAutomation.Core.csproj");

        Assert.Empty(references);
    }
}
