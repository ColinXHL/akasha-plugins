namespace AkashaAutomation.Core.Input;

public sealed record InputActionGroup
{
    public InputActionGroup(string name, IReadOnlyList<InputAction> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0)
        {
            throw new ArgumentException("An input action group must contain at least one action.", nameof(actions));
        }

        Name = name;
        Actions = actions.ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<InputAction> Actions { get; }
}
