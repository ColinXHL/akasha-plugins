using AkashaAutomation.Tests;

namespace AkashaAutomation.BetterGiPort.Tests;

public class ProjectDependencyTests
{
    [Fact]
    public void BetterGiPortProject_ShouldReferenceCoreOnly()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.BetterGiPort", "AkashaAutomation.BetterGiPort.csproj");

        Assert.Equal(["AkashaAutomation.Core"], references);
    }
}
