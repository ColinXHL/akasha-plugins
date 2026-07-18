namespace AkashaAutomation.Core.GameContext;

public sealed record GameContextSnapshot(
    DateTimeOffset ObservedAtUtc,
    GameWindowInfo? Window)
{
    public GameUiCategory UiCategory { get; init; } = GameUiCategory.Unknown;

    public bool HasGameWindow => Window is not null;

    public bool IsGameForeground => Window?.IsForeground == true;

    public bool IsTalk => UiCategory == GameUiCategory.Talk;
}

public enum GameUiCategory
{
    Unknown,
    Main,
    Talk,
    BigMap,
}
