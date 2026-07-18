namespace AkashaAutomation.Core.Input;

public sealed record AutomationIntent(
    string FeatureId,
    int Priority,
    InputActionGroup Actions,
    string Reason);
