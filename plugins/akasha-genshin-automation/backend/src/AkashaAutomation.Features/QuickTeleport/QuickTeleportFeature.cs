using AkashaAutomation.BetterGiPort.Compatibility.QuickTeleport;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Features.QuickTeleport;

public sealed class QuickTeleportFeature : IAutomationFeature
{
    public const string FeatureId = "quickTeleport";
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(300);
    private readonly IQuickTeleportController _controller;
    private readonly BetterGiQuickTeleportRecognizer _recognizer;
    private readonly IClock _clock;
    private QuickTeleportState _state = QuickTeleportState.Scanning;
    private DateTimeOffset _nextScanUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _stateDueUtc = DateTimeOffset.MinValue;
    private string? _pendingCandidateText;

    public QuickTeleportFeature(
        IQuickTeleportController controller,
        BetterGiQuickTeleportRecognizer recognizer,
        IClock clock)
    {
        _controller = controller;
        _recognizer = recognizer;
        _clock = clock;
        _controller.Disabled += Reset;
    }

    public string Id => FeatureId;

    public int Priority => 80;

    public async ValueTask<FeatureDecision> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);
        var options = _controller.Options;
        var now = _clock.UtcNow;

        if (!options.Enabled)
        {
            Reset();
            return NoAction(frame, "disabled");
        }

        if (!BetterGiQuickTeleportRecognizer.IsSupportedCapture(frame.Size))
        {
            Reset();
            return NoAction(frame, "unsupported_capture_size");
        }

        if (!context.IsBigMap)
        {
            Reset();
            return NoAction(frame, "big_map_not_active");
        }

        switch (_state)
        {
            case QuickTeleportState.CandidateDelay:
                if (now < _stateDueUtc)
                {
                    return NoAction(frame, "candidate_click_delay");
                }

                var candidate = await _recognizer
                    .FindFirstValidCandidateAsync(frame, cancellationToken)
                    .ConfigureAwait(false);
                if (candidate is null)
                {
                    BeginScanning(now);
                    return NoAction(frame, "candidate_disappeared");
                }

                _pendingCandidateText = candidate.Text;
                _state = QuickTeleportState.AwaitTeleportPanel;
                _stateDueUtc = now.AddMilliseconds(options.WaitTeleportPanelDelayMilliseconds);
                return Act(frame, candidate.Text, "click_candidate", Click("quick-teleport-candidate", candidate.ClickRegion, frame.Size));

            case QuickTeleportState.AwaitTeleportPanel:
                if (now < _stateDueUtc)
                {
                    return NoAction(frame, "teleport_panel_delay");
                }

                var awaitedButton = _recognizer.FindTeleportButton(frame);
                if (awaitedButton.IsMatch && awaitedButton.Region is { } awaitedButtonRegion)
                {
                    BeginCooldown(now);
                    return Act(frame, null, "click_teleport_button", Click("quick-teleport-button", awaitedButtonRegion, frame.Size));
                }

                BeginScanning(now);
                return NoAction(frame, "teleport_panel_not_found");

            case QuickTeleportState.Cooldown:
                if (now < _stateDueUtc)
                {
                    return NoAction(frame, "action_cooldown");
                }

                BeginScanning(DateTimeOffset.MinValue);
                break;
        }

        if (now < _nextScanUtc)
        {
            return NoAction(frame, "scan_throttled");
        }

        _nextScanUtc = now + ScanInterval;
        var button = _recognizer.FindTeleportButton(frame);
        if (button.IsMatch && button.Region is { } buttonRegion)
        {
            BeginCooldown(now);
            return Act(frame, null, "click_teleport_button", Click("quick-teleport-button", buttonRegion, frame.Size));
        }

        if (_recognizer.IsMapSelectionIdle(frame))
        {
            return NoAction(frame, "map_selection_idle");
        }

        var firstCandidate = await _recognizer
            .FindFirstValidCandidateAsync(frame, cancellationToken)
            .ConfigureAwait(false);
        if (firstCandidate is null)
        {
            return NoAction(frame, "candidate_not_found");
        }

        _pendingCandidateText = firstCandidate.Text;
        _state = QuickTeleportState.CandidateDelay;
        _stateDueUtc = now.AddMilliseconds(options.TeleportListClickDelayMilliseconds);
        return NoAction(frame, "candidate_wait_started");
    }

    private FeatureDecision Act(
        CapturedFrame frame,
        string? candidateText,
        string reason,
        InputActionGroup actions)
    {
        Report(frame, candidateText, reason, intentSubmitted: true);
        return FeatureDecision.Act(new AutomationIntent(FeatureId, Priority, actions, reason));
    }

    private FeatureDecision NoAction(CapturedFrame frame, string reason)
    {
        Report(frame, _pendingCandidateText, reason, intentSubmitted: false);
        return FeatureDecision.NoAction(FeatureId, reason);
    }

    private void Report(CapturedFrame frame, string? candidateText, string reason, bool intentSubmitted) =>
        _controller.Report(
            frame.Sequence,
            _state.ToString(),
            candidateText,
            reason,
            intentSubmitted,
            _clock.UtcNow);

    private void BeginScanning(DateTimeOffset now)
    {
        _state = QuickTeleportState.Scanning;
        _stateDueUtc = DateTimeOffset.MinValue;
        _pendingCandidateText = null;
        _nextScanUtc = now == DateTimeOffset.MinValue ? DateTimeOffset.MinValue : now + ScanInterval;
    }

    private void BeginCooldown(DateTimeOffset now)
    {
        _state = QuickTeleportState.Cooldown;
        _stateDueUtc = now + ScanInterval;
        _pendingCandidateText = null;
    }

    private void Reset()
    {
        _state = QuickTeleportState.Scanning;
        _nextScanUtc = DateTimeOffset.MinValue;
        _stateDueUtc = DateTimeOffset.MinValue;
        _pendingCandidateText = null;
    }

    private static InputActionGroup Click(
        string name,
        RegionOfInterest region,
        CaptureSize referenceSize) =>
        new(
            name,
            [
                InputAction.MouseMoveClient(
                    region.X + region.Width / 2,
                    region.Y + region.Height / 2,
                    referenceSize.Width,
                    referenceSize.Height),
                InputAction.MouseLeftClick(),
            ]);
}

public enum QuickTeleportState
{
    Scanning,
    CandidateDelay,
    AwaitTeleportPanel,
    Cooldown,
}
