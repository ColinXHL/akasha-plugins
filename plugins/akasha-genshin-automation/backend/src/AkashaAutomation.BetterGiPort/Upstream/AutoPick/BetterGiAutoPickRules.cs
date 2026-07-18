using System.Text;
using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.BetterGiPort.Upstream.AutoPick;

public sealed record BetterGiAutoPickLists(
    IReadOnlySet<string> ExactBlacklist,
    IReadOnlyList<string> FuzzyBlacklist,
    IReadOnlySet<string> Whitelist);

public sealed record BetterGiAutoPickRuleDecision(bool ShouldPick, string Reason, string Text);

public static class BetterGiAutoPickRules
{
    public static BetterGiAutoPickLists LoadLists(
        IAssetPathResolver assetPathResolver,
        IEnumerable<string>? userExactBlacklist = null,
        IEnumerable<string>? userFuzzyBlacklist = null,
        IEnumerable<string>? userWhitelist = null)
    {
        ArgumentNullException.ThrowIfNull(assetPathResolver);
        var exactBlacklist = BetterGiJsonList
            .Load(
                assetPathResolver.Resolve(
                    BetterGiAssetPaths.DefaultPickBlacklist))
            .ToHashSet(StringComparer.Ordinal);
        exactBlacklist.UnionWith(NonEmpty(userExactBlacklist));
        return new BetterGiAutoPickLists(
            exactBlacklist,
            NonEmpty(userFuzzyBlacklist).ToArray(),
            NonEmpty(userWhitelist).ToHashSet(StringComparer.Ordinal));
    }

    public static BetterGiAutoPickRuleDecision Decide(
        string? ocrText,
        bool isExcludeIcon,
        bool blacklistEnabled,
        bool whitelistEnabled,
        BetterGiAutoPickLists lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        var text = ProcessOcrText(ocrText ?? string.Empty);
        if (text.Length == 0)
        {
            return new(false, "ocr_empty", text);
        }

        if (DoNotPick(text))
        {
            return new(false, "hardcoded_exclusion", text);
        }

        if (text.Length <= 1)
        {
            return new(false, "ocr_too_short", text);
        }

        if (whitelistEnabled && lists.Whitelist.Contains(text))
        {
            return new(true, "whitelist_exact", text);
        }

        if (isExcludeIcon)
        {
            return new(false, "exclude_icon", text);
        }

        if (blacklistEnabled && lists.ExactBlacklist.Contains(text))
        {
            return new(false, "blacklist_exact", text);
        }

        if (blacklistEnabled && lists.FuzzyBlacklist.Any(text.Contains))
        {
            return new(false, "blacklist_fuzzy", text);
        }

        return new(true, "pick", text);
    }

    public static bool DoNotPick(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains("长时间", StringComparison.Ordinal))
        {
            return true;
        }

        if (text.Contains("我在", StringComparison.Ordinal) &&
            (text.Contains("声望", StringComparison.Ordinal) ||
             text.Contains("回声", StringComparison.Ordinal) ||
             text.Contains("悬木人", StringComparison.Ordinal) ||
             text.Contains("流泉", StringComparison.Ordinal)))
        {
            return true;
        }

        if (text.Contains("聚所", StringComparison.Ordinal) ||
            (text.Contains("霜月", StringComparison.Ordinal) && text.Contains("坊", StringComparison.Ordinal)) ||
            text.Contains("叮铃", StringComparison.Ordinal) ||
            text.Contains("眶螂", StringComparison.Ordinal) ||
            (text.Contains("蛋卷", StringComparison.Ordinal) && text.Contains("坊", StringComparison.Ordinal)) ||
            text.Contains("西风成垒", StringComparison.Ordinal) ||
            text.Contains("望崖营壁", StringComparison.Ordinal) ||
            text.Contains("魔女的花园", StringComparison.Ordinal) ||
            text.Contains("月谕圣牌", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public static string ProcessOcrText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var normalized = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            normalized.Append(character switch
            {
                '【' or '[' => '「',
                '】' or ']' => '」',
                _ => character,
            });
        }

        var start = 0;
        var end = normalized.Length - 1;
        while (start <= end && normalized[start] != '「' && !IsCjk(normalized[start]))
        {
            start++;
        }

        while (end >= start && normalized[end] != '」' && normalized[end] != '！' && !IsCjk(normalized[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        var cleaned = normalized.ToString(start, end - start + 1);
        var hasLeftQuote = cleaned.Contains('「');
        var hasRightQuote = cleaned.Contains('」');
        if (hasLeftQuote && !hasRightQuote)
        {
            return cleaned + '」';
        }

        return hasRightQuote && !hasLeftQuote ? '「' + cleaned : cleaned;
    }

    private static IEnumerable<string> NonEmpty(IEnumerable<string>? values) =>
        values?.Where(value => !string.IsNullOrEmpty(value)) ?? [];

    private static bool IsCjk(char value) => value is >= '\u4E00' and <= '\u9FFF';
}
