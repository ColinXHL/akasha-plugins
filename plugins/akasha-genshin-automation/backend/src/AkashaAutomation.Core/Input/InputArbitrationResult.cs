namespace AkashaAutomation.Core.Input;

public sealed record InputArbitrationResult(
    bool Executed,
    string Reason,
    AutomationIntent? SelectedIntent = null);
