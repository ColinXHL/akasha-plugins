namespace AkashaAutomation.Features.QuickTeleport;

public sealed class QuickTeleportController : IQuickTeleportController
{
    private readonly object _gate = new();
    private QuickTeleportOptions _options = NormalizeAndValidate(new QuickTeleportOptions());
    private QuickTeleportRuntimeStatus _status = new(
        false,
        false,
        QuickTeleportState.Scanning.ToString(),
        null,
        "not_evaluated",
        false,
        null,
        null);

    public event Action? Disabled;

    public string FeatureId => QuickTeleportFeature.FeatureId;

    public bool IsEnabled => Options.Enabled;

    public QuickTeleportOptions Options
    {
        get
        {
            lock (_gate)
            {
                return _options;
            }
        }
    }

    public QuickTeleportRuntimeStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _options = _options with { Enabled = enabled };
            _status = _status with { Enabled = enabled, IsRunning = false };
        }

        if (!enabled)
        {
            Disabled?.Invoke();
        }
    }

    public void SetOptions(QuickTeleportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var normalized = NormalizeAndValidate(options);
        lock (_gate)
        {
            _options = normalized;
            _status = _status with { Enabled = normalized.Enabled, IsRunning = false };
        }

        if (!normalized.Enabled)
        {
            Disabled?.Invoke();
        }
    }

    public void Report(
        long frameSequence,
        string state,
        string? candidateText,
        string reason,
        bool intentSubmitted,
        DateTimeOffset timestampUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_gate)
        {
            _status = new QuickTeleportRuntimeStatus(
                _options.Enabled,
                _options.Enabled,
                state,
                candidateText,
                reason,
                intentSubmitted,
                frameSequence,
                timestampUtc);
        }
    }

    private static QuickTeleportOptions NormalizeAndValidate(QuickTeleportOptions options)
    {
        if (options.TeleportListClickDelayMilliseconds is < 0 or > 5_000 ||
            options.WaitTeleportPanelDelayMilliseconds is < 0 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "QuickTeleport timing values must be between 0 and 5000 milliseconds.");
        }

        return options;
    }
}
