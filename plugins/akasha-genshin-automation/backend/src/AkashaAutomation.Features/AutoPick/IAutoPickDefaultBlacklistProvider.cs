namespace AkashaAutomation.Features.AutoPick;

public interface IAutoPickDefaultBlacklistProvider
{
    AutoPickDefaultBlacklistSnapshot Current { get; }

    event EventHandler? Changed;
}

public sealed record AutoPickDefaultBlacklistSnapshot(
    IReadOnlyList<string> Entries,
    string Revision,
    string SourceVersion,
    string Sha256,
    bool IsExternal);
