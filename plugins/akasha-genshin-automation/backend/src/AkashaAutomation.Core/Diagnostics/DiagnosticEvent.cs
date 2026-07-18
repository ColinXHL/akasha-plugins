namespace AkashaAutomation.Core.Diagnostics;

public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    string Category,
    string Name,
    IReadOnlyDictionary<string, object?> Data);
