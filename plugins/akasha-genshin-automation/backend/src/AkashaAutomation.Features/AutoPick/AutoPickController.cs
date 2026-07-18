using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Upstream.AutoPick;
using AkashaAutomation.Core.Abstractions;

namespace AkashaAutomation.Features.AutoPick;

public sealed class AutoPickController : IAutoPickController
{
    private readonly IAssetPathResolver _assetPathResolver;
    private readonly object _gate = new();
    private AutoPickConfiguration _configuration;
    private AutoPickRuntimeStatus _status;

    public AutoPickController(IAssetPathResolver assetPathResolver)
    {
        _assetPathResolver = assetPathResolver;
        var options = NormalizeAndValidate(new AutoPickOptions());
        _configuration = CreateConfiguration(options);
        _status = new(options.Enabled, false, null, "not_evaluated", false, null, null);
    }

    public AutoPickOptions Options
    {
        get
        {
            lock (_gate)
            {
                return _configuration.Options;
            }
        }
    }

    public AutoPickRuntimeStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public AutoPickConfiguration Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _configuration;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            var options = _configuration.Options with { Enabled = enabled };
            _configuration = _configuration with { Options = options };
            _status = _status with { Enabled = enabled, IsRunning = false };
        }
    }

    public void SetOptions(AutoPickOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = NormalizeAndValidate(options);
        var configuration = CreateConfiguration(normalized);
        lock (_gate)
        {
            _configuration = configuration;
            _status = _status with { Enabled = normalized.Enabled, IsRunning = false };
        }
    }

    public void Report(
        long frameSequence,
        string? text,
        string reason,
        bool intentSubmitted,
        DateTimeOffset timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            _status = new AutoPickRuntimeStatus(
                _configuration.Options.Enabled,
                _configuration.Options.Enabled,
                text,
                reason,
                intentSubmitted,
                frameSequence,
                timestampUtc);
        }
    }

    private AutoPickConfiguration CreateConfiguration(AutoPickOptions options) =>
        new(
            options,
            BetterGiAutoPickRules.LoadLists(
                _assetPathResolver,
                options.UserExactBlacklist,
                options.UserFuzzyBlacklist,
                options.UserWhitelist));

    private static AutoPickOptions NormalizeAndValidate(AutoPickOptions options)
    {
        var pickKey = BetterGiAutoPickRecognizer.NormalizePickKey(options.PickKey);
        if (!options.OcrEngine.Equals("Paddle", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Phase 4 supports the packaged Paddle OCR engine only.");
        }

        if (options.ItemIconLeftOffset < 0 ||
            options.ItemTextLeftOffset <= options.ItemIconLeftOffset ||
            options.ItemTextRightOffset <= options.ItemTextLeftOffset ||
            options.ItemTextRightOffset > 1920)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "AutoPick item offsets are invalid.");
        }

        return options with
        {
            PickKey = pickKey,
            OcrEngine = "Paddle",
            UserExactBlacklist = Copy(options.UserExactBlacklist),
            UserFuzzyBlacklist = Copy(options.UserFuzzyBlacklist),
            UserWhitelist = Copy(options.UserWhitelist),
        };
    }

    private static IReadOnlyList<string> Copy(IReadOnlyList<string>? source) =>
        source?.Where(value => !string.IsNullOrEmpty(value)).ToArray() ?? [];
}
