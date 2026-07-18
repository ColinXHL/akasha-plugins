using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;

namespace AkashaAutomation.BetterGiPort.Upstream.AutoSkip;

public static class BetterGiAutoSkipRules
{
    public static BetterGiAutoSkipLists LoadLists(IAssetPathResolver resolver) =>
        new(
            BetterGiJsonList.Load(resolver.Resolve(BetterGiAssetPaths.SelectOptions)),
            BetterGiJsonList.Load(resolver.Resolve(BetterGiAssetPaths.PauseOptions)),
            BetterGiJsonList.Load(resolver.Resolve(BetterGiAssetPaths.DefaultPauseOptions)));

    public static DialogueOptionDecision Decide(
        IReadOnlyList<DialogueOptionCandidate> candidates,
        BetterGiAutoSkipLists lists,
        DialogueOptionRuleOptions options,
        int deterministicRandomIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(options);
        if (candidates.Count == 0)
        {
            return DialogueOptionDecision.None("option_not_found");
        }

        var ordered = candidates.OrderBy(candidate => candidate.Region.Y).ToArray();
        if (options.CustomPriorityEnabled)
        {
            var custom = NormalizeCustomOptions(options.CustomPriorityOptions);
            var match = FindContains(ordered, custom);
            if (match is not null)
            {
                return DialogueOptionDecision.Select(match, "custom_priority");
            }
        }

        if (options.Strategy == DialogueOptionStrategy.None)
        {
            return DialogueOptionDecision.None("option_selection_disabled");
        }

        if (!options.SkipBuiltInPriority)
        {
            var select = FindContains(ordered, lists.SelectOptions);
            if (select is not null)
            {
                return DialogueOptionDecision.Select(select, "select_priority");
            }

            var pause = FindContains(ordered, lists.PauseOptions);
            if (pause is not null)
            {
                return DialogueOptionDecision.Pause(pause, "pause_priority");
            }

            var orange = ordered.FirstOrDefault(candidate => candidate.IsOrange);
            if (orange is not null)
            {
                var reason = orange.Text.Contains("每日", StringComparison.Ordinal) ||
                             orange.Text.Contains("委托", StringComparison.Ordinal)
                    ? "daily_reward_option"
                    : orange.Text.Contains("探索", StringComparison.Ordinal) ||
                      orange.Text.Contains("派遣", StringComparison.Ordinal)
                        ? "reexplore_option"
                        : "orange_option";
                return DialogueOptionDecision.Select(orange, reason);
            }

            var defaultPause = FindContains(ordered, lists.DefaultPauseOptions);
            if (defaultPause is not null)
            {
                return DialogueOptionDecision.Pause(defaultPause, "default_pause_priority");
            }
        }

        var selected = options.Strategy switch
        {
            DialogueOptionStrategy.First => ordered[0],
            DialogueOptionStrategy.Last => ordered[^1],
            DialogueOptionStrategy.Random => ordered[Math.Abs(deterministicRandomIndex % ordered.Length)],
            _ => null,
        };
        return selected is null
            ? DialogueOptionDecision.None("option_selection_disabled")
            : DialogueOptionDecision.Select(selected, $"fallback_{options.Strategy.ToString().ToLowerInvariant()}");
    }

    public static string NormalizeOcrText(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : string.Concat(text.Where(character => character is not ('\r' or '\n'))).Trim();

    private static DialogueOptionCandidate? FindContains(
        IEnumerable<DialogueOptionCandidate> candidates,
        IReadOnlyList<string> keywords) =>
        candidates.FirstOrDefault(candidate =>
            keywords.Any(keyword =>
                !string.IsNullOrWhiteSpace(keyword) &&
                candidate.Text.Contains(keyword.Trim(), StringComparison.Ordinal)));

    private static IReadOnlyList<string> NormalizeCustomOptions(IReadOnlyList<string> values) =>
        values
            .SelectMany(value => value.Split(['\r', '\n', ';', '；'], StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
}

public sealed record BetterGiAutoSkipLists(
    IReadOnlyList<string> SelectOptions,
    IReadOnlyList<string> PauseOptions,
    IReadOnlyList<string> DefaultPauseOptions);

public sealed record DialogueOptionRuleOptions(
    DialogueOptionStrategy Strategy,
    bool CustomPriorityEnabled,
    IReadOnlyList<string> CustomPriorityOptions,
    bool SkipBuiltInPriority);

public enum DialogueOptionStrategy
{
    First,
    Last,
    Random,
    None,
}

public enum DialogueOptionKind
{
    Standard,
    Exclamation,
    HangoutSelected,
    HangoutUnselected,
}

public sealed record DialogueOptionCandidate(
    string Text,
    RegionOfInterest Region,
    bool IsOrange = false,
    DialogueOptionKind Kind = DialogueOptionKind.Standard);

public sealed record DialogueOptionDecision(
    bool ShouldSelect,
    bool ShouldPause,
    string Reason,
    DialogueOptionCandidate? Candidate)
{
    public static DialogueOptionDecision Select(DialogueOptionCandidate candidate, string reason) =>
        new(true, false, reason, candidate);

    public static DialogueOptionDecision Pause(DialogueOptionCandidate candidate, string reason) =>
        new(false, true, reason, candidate);

    public static DialogueOptionDecision None(string reason) => new(false, false, reason, null);
}
