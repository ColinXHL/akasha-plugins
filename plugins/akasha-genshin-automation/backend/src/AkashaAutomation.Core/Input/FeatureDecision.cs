namespace AkashaAutomation.Core.Input;

public sealed record FeatureDecision(
    string FeatureId,
    bool ShouldAct,
    string Reason,
    AutomationIntent? Intent = null)
{
    public static FeatureDecision NoAction(string featureId, string reason) =>
        new(featureId, false, reason);

    public static FeatureDecision Act(AutomationIntent intent) =>
        new(intent.FeatureId, true, intent.Reason, intent);
}
