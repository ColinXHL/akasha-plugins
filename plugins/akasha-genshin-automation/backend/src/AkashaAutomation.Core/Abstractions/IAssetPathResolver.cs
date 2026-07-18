namespace AkashaAutomation.Core.Abstractions;

public interface IAssetPathResolver
{
    string Resolve(string relativePath);
}
