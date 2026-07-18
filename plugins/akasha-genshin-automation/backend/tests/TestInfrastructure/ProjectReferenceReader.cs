using System.Xml.Linq;

namespace AkashaAutomation.Tests;

internal static class ProjectReferenceReader
{
    public static string[] GetProjectReferences(params string[] projectPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine([repositoryRoot, .. projectPath]));

        return document.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AkashaAutomation.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("AkashaAutomation repository root not found.");
    }
}
