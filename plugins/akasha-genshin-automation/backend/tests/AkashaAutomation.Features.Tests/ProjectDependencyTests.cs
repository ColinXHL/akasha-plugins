using AkashaAutomation.Tests;

namespace AkashaAutomation.Features.Tests;

public class ProjectDependencyTests
{
    [Fact]
    public void FeaturesProject_ShouldReferenceCoreAndBetterGiPort()
    {
        var references = ProjectReferenceReader.GetProjectReferences(
            "src", "AkashaAutomation.Features", "AkashaAutomation.Features.csproj");

        Assert.Equal(["AkashaAutomation.BetterGiPort", "AkashaAutomation.Core"], references);
    }
}
