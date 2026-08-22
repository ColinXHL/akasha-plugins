namespace AkashaAutomation.Core.Scheduling;

public interface IAutomationFeatureControl
{
    string FeatureId { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

public sealed class AutomationFeatureControls
{
    private readonly IReadOnlyList<IAutomationFeatureControl> _controls;

    public AutomationFeatureControls(IEnumerable<IAutomationFeatureControl> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        _controls = controls.ToArray();

        if (_controls.Any(control => string.IsNullOrWhiteSpace(control.FeatureId)))
        {
            throw new InvalidOperationException("Automation feature controls require a feature id.");
        }

        var duplicate = _controls
            .GroupBy(control => control.FeatureId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Automation feature control '{duplicate.Key}' is registered more than once.");
        }
    }

    public bool AnyEnabled => _controls.Any(control => control.IsEnabled);

    public void DisableAll()
    {
        foreach (var control in _controls)
        {
            control.SetEnabled(false);
        }
    }
}
