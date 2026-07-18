using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;

namespace AkashaAutomation.DevHost;

public sealed record DevHostOptions(
    string Feature,
    string PickKey,
    int IntervalMilliseconds,
    bool BlacklistEnabled,
    bool ShowAllFrames,
    IReadOnlyList<string> UserExactBlacklist,
    IReadOnlyList<string> UserFuzzyBlacklist,
    IReadOnlyList<string> UserWhitelist,
    string OptionStrategy,
    IReadOnlyList<string> CustomPriorityOptions,
    string AdvanceKey,
    bool VoiceWaitEnabled,
    string HangoutEnding)
{
    public static DevHostOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var pickKey = "F";
        var feature = "auto-pick";
        var interval = 50;
        var blacklistEnabled = true;
        var showAllFrames = false;
        var exactBlacklist = new List<string>();
        var fuzzyBlacklist = new List<string>();
        var whitelist = new List<string>();
        var optionStrategy = "First";
        var customPriorityOptions = new List<string>();
        var advanceKey = "Space";
        var voiceWaitEnabled = false;
        var hangoutEnding = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--feature":
                    feature = ReadValue(args, ref index, argument).ToLowerInvariant();
                    if (feature is not ("auto-pick" or "auto-dialogue"))
                    {
                        throw new ArgumentException("--feature must be auto-pick or auto-dialogue.");
                    }

                    break;
                case "--pick-key":
                    pickKey = ReadValue(args, ref index, argument);
                    break;
                case "--interval-ms":
                    var value = ReadValue(args, ref index, argument);
                    if (!int.TryParse(value, out interval) || interval is < 25 or > 2000)
                    {
                        throw new ArgumentException("--interval-ms must be between 25 and 2000.");
                    }

                    break;
                case "--no-blacklist":
                    blacklistEnabled = false;
                    break;
                case "--show-all":
                    showAllFrames = true;
                    break;
                case "--exact-blacklist":
                    exactBlacklist.Add(ReadValue(args, ref index, argument));
                    break;
                case "--fuzzy-blacklist":
                    fuzzyBlacklist.Add(ReadValue(args, ref index, argument));
                    break;
                case "--whitelist":
                    whitelist.Add(ReadValue(args, ref index, argument));
                    break;
                case "--option-strategy":
                    optionStrategy = ReadValue(args, ref index, argument);
                    if (!Enum.TryParse<AkashaAutomation.BetterGiPort.Upstream.AutoSkip.DialogueOptionStrategy>(optionStrategy, true, out var parsedStrategy))
                    {
                        throw new ArgumentException("--option-strategy must be first, last, random, or none.");
                    }

                    optionStrategy = parsedStrategy.ToString();
                    break;
                case "--custom-option":
                    customPriorityOptions.Add(ReadValue(args, ref index, argument));
                    break;
                case "--advance-key":
                    advanceKey = ReadValue(args, ref index, argument);
                    if (!advanceKey.Equals("space", StringComparison.OrdinalIgnoreCase) &&
                        !advanceKey.Equals("interaction", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ArgumentException("--advance-key must be space or interaction.");
                    }

                    advanceKey = advanceKey.Equals("space", StringComparison.OrdinalIgnoreCase) ? "Space" : "Interaction";
                    break;
                case "--voice-wait":
                    voiceWaitEnabled = true;
                    break;
                case "--hangout-ending":
                    hangoutEnding = ReadValue(args, ref index, argument);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        return new DevHostOptions(
            feature,
            BetterGiAutoPickRecognizer.NormalizePickKey(pickKey),
            interval,
            blacklistEnabled,
            showAllFrames,
            exactBlacklist,
            fuzzyBlacklist,
            whitelist,
            optionStrategy,
            customPriorityOptions,
            advanceKey,
            voiceWaitEnabled,
            hangoutEnding);
    }

    public static string Usage => """
        AkashaAutomation.DevHost — observe-only real-game host

        Usage:
          AkashaAutomation.DevHost.exe [options]

        Options:
          --feature auto-pick|auto-dialogue  Feature to observe (default: auto-pick)
          --pick-key E|F|G          Interaction key template and virtual key (default: F)
          --interval-ms 25..2000   Target frame cadence (default: 50)
          --no-blacklist           Disable default and user blacklists
          --exact-blacklist TEXT   Add a user exact-blacklist entry; repeatable
          --fuzzy-blacklist TEXT   Add a user fuzzy-blacklist entry; repeatable
          --whitelist TEXT         Add a user whitelist entry; repeatable
          --show-all               Print every frame instead of changes only
          --option-strategy MODE   first, last, random, or none (AutoDialogue)
          --custom-option TEXT     Add a custom priority option; repeatable
          --advance-key MODE       space or interaction (AutoDialogue)
          --voice-wait             Enable process-loopback Silero VAD waiting
          --hangout-ending TEXT    BetterGI hangout ending name
          --help                   Show this help

        Safety:
          This executable is permanently observe-only and contains no real input service.
        """;

    private static string ReadValue(string[] args, ref int index, string argument)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new ArgumentException($"{argument} requires a value.");
        }

        return args[index];
    }
}
