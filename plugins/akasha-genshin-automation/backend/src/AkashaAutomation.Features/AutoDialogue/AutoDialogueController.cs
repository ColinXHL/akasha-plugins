using System.Text.Json;
using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Features.AutoDialogue;

public sealed class AutoDialogueController : IAutoDialogueController
{
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly object _gate = new();
    private AutoDialogueConfiguration _configuration;
    private AutoDialogueRuntimeStatus _status;

    public event Action? Disabled;

    public AutoDialogueController(IAssetPathResolver assetPathResolver)
    {
        _assetPathResolver = assetPathResolver;
        var options = NormalizeAndValidate(new AutoDialogueOptions());
        _configuration = CreateConfiguration(options);
        _status = new(options.Enabled, false, "Unknown", [], "not_evaluated", false, false, false, null, null);
    }

    public AutoDialogueOptions Options
    {
        get { lock (_gate) return _configuration.Options; }
    }

    public AutoDialogueRuntimeStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public AutoDialogueConfiguration Snapshot
    {
        get { lock (_gate) return _configuration; }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _configuration = _configuration with { Options = _configuration.Options with { Enabled = enabled } };
            _status = _status with { Enabled = enabled, IsRunning = false, VoiceWaitActive = false };
        }

        if (!enabled)
        {
            Disabled?.Invoke();
        }
    }

    public void SetOptions(AutoDialogueOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuration = CreateConfiguration(NormalizeAndValidate(options));
        lock (_gate)
        {
            _configuration = configuration;
            _status = _status with
            {
                Enabled = configuration.Options.Enabled,
                IsRunning = false,
                VoiceWaitActive = false,
            };
        }

        if (!configuration.Options.Enabled)
        {
            Disabled?.Invoke();
        }
    }

    public void Report(
        long frameSequence,
        string uiCategory,
        IReadOnlyList<string> recognizedOptions,
        string reason,
        bool intentSubmitted,
        bool voiceWaitActive,
        bool voiceWaitFallback,
        DateTimeOffset timestampUtc)
    {
        lock (_gate)
        {
            _status = new AutoDialogueRuntimeStatus(
                _configuration.Options.Enabled,
                _configuration.Options.Enabled,
                uiCategory,
                recognizedOptions.ToArray(),
                reason,
                intentSubmitted,
                voiceWaitActive,
                voiceWaitFallback,
                frameSequence,
                timestampUtc);
        }
    }

    private AutoDialogueConfiguration CreateConfiguration(AutoDialogueOptions options) =>
        new(
            options,
            new DialogueOptionRuleOptions(
                Enum.Parse<DialogueOptionStrategy>(options.OptionStrategy),
                options.CustomPriorityOptionsEnabled,
                options.CustomPriorityOptions,
                options.SkipBuiltInPriority),
            BetterGiAutoSkipRules.LoadLists(_assetPathResolver),
            LoadHangoutOptions());

    private IReadOnlyDictionary<string, IReadOnlyList<string>> LoadHangoutOptions()
    {
        var path = _assetPathResolver.Resolve(BetterGiAssetPaths.HangoutOptions);
        var source = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllBytes(path)) ?? [];
        return source.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value,
            StringComparer.Ordinal);
    }

    private static AutoDialogueOptions NormalizeAndValidate(AutoDialogueOptions options)
    {
        var strategy = options.OptionStrategy.Trim();
        if (!Enum.TryParse<DialogueOptionStrategy>(strategy, ignoreCase: true, out var parsedStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "OptionStrategy must be First, Last, Random, or None.");
        }

        var advanceKey = options.AdvanceKey.Trim();
        if (!advanceKey.Equals("Space", StringComparison.OrdinalIgnoreCase) &&
            !advanceKey.Equals("Interaction", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AdvanceKey must be Space or Interaction.");
        }

        if (options.BeforeAdvanceDelayMilliseconds is < 0 or > 60_000 ||
            options.AfterOptionDelayMilliseconds is < 0 or > 60_000 ||
            options.DialogueVoiceMaxWaitSeconds is < 0 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AutoDialogue timing values are outside the supported range.");
        }

        return options with
        {
            AdvanceKey = advanceKey.Equals("Space", StringComparison.OrdinalIgnoreCase) ? "Space" : "Interaction",
            InteractionKey = BetterGiAutoPickRecognizer.NormalizePickKey(options.InteractionKey),
            OptionStrategy = parsedStrategy.ToString(),
            CustomPriorityOptions = options.CustomPriorityOptions
                ?.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray() ?? [],
            HangoutEnding = options.HangoutEnding?.Trim() ?? string.Empty,
        };
    }
}
