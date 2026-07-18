using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Features.AutoDialogue;

public sealed class AutoDialogueFeature : IAutomationFeature
{
    public const string FeatureId = "autoDialogue";
    private static readonly TimeSpan MinimumActionInterval = TimeSpan.FromMilliseconds(200);
    private readonly IAutoDialogueController _controller;
    private readonly BetterGiAutoDialogueRecognizer _recognizer;
    private readonly IDialogueOptionVoiceWaiter _voiceWaiter;
    private readonly RewardDialogueSceneHandler _rewardHandler;
    private readonly HangoutDialogueSceneHandler _hangoutHandler;
    private readonly IReadOnlyList<IAutoDialogueSceneHandler> _nonTalkHandlers;
    private readonly IClock _clock;
    private DateTimeOffset? _lastTalkUtc;
    private DateTimeOffset? _talkStartedUtc;
    private DateTimeOffset _nextActionUtc = DateTimeOffset.MinValue;
    private string? _pendingOptionFingerprint;
    private string? _readyOptionFingerprint;

    public AutoDialogueFeature(
        IAutoDialogueController controller,
        BetterGiAutoDialogueRecognizer recognizer,
        IDialogueOptionVoiceWaiter voiceWaiter,
        RewardDialogueSceneHandler rewardHandler,
        HangoutDialogueSceneHandler hangoutHandler,
        IEnumerable<IAutoDialogueSceneHandler> sceneHandlers,
        IClock clock)
    {
        _controller = controller;
        _recognizer = recognizer;
        _voiceWaiter = voiceWaiter;
        _rewardHandler = rewardHandler;
        _hangoutHandler = hangoutHandler;
        _nonTalkHandlers = sceneHandlers
            .Where(handler => handler is not RewardDialogueSceneHandler and not HangoutDialogueSceneHandler)
            .ToArray();
        _clock = clock;
        _controller.Disabled += CancelWait;
    }

    public string Id => FeatureId;

    public int Priority => 100;

    public async ValueTask<FeatureDecision> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);
        var configuration = _controller.Snapshot;
        var now = _clock.UtcNow;
        if (!configuration.Options.Enabled)
        {
            CancelWait();
            return NoAction(frame, context, [], "disabled");
        }

        if (context.IsTalk)
        {
            _lastTalkUtc = now;
            _talkStartedUtc ??= now;
            if (now < _nextActionUtc)
            {
                return NoAction(frame, context, [], "action_cooldown");
            }

            var exclamation = _recognizer.FindExclamationOption(frame);
            if (exclamation is not null && configuration.RuleOptions.Strategy != DialogueOptionStrategy.None)
            {
                return await SelectOptionAsync(
                    frame,
                    context,
                    configuration,
                    exclamation,
                    "exclamation_option",
                    [string.Empty],
                    cancellationToken).ConfigureAwait(false);
            }

            var candidates = await _recognizer.FindDialogueOptionsAsync(frame, cancellationToken).ConfigureAwait(false);
            if (candidates.Count > 0)
            {
                var decision = BetterGiAutoSkipRules.Decide(
                    candidates,
                    configuration.Lists,
                    configuration.RuleOptions,
                    unchecked((int)frame.Sequence));
                var texts = candidates.Select(candidate => candidate.Text).ToArray();
                if (decision.ShouldPause)
                {
                    CancelWait();
                    return NoAction(frame, context, texts, decision.Reason);
                }

                if (decision.ShouldSelect && decision.Candidate is not null)
                {
                    return await SelectOptionAsync(
                        frame,
                        context,
                        configuration,
                        decision.Candidate,
                        decision.Reason,
                        texts,
                        cancellationToken).ConfigureAwait(false);
                }

                CancelWait();
                return NoAction(frame, context, texts, decision.Reason);
            }

            CancelWait();
            var interaction = _recognizer.FindDialogueInteraction(frame, configuration.Options.InteractionKey);
            if (interaction.IsMatch)
            {
                return Act(
                    frame,
                    context,
                    [],
                    "dialogue_interaction_key",
                    new InputActionGroup(
                        "auto-dialogue-interaction",
                        [InputAction.KeyPress(BetterGiAutoPickRecognizer.ToVirtualKey(configuration.Options.InteractionKey))]));
            }

            var hangout = await _hangoutHandler.EvaluateAsync(
                frame,
                context,
                new DialogueSceneEvaluation(configuration, now, true, frame.Sequence, true),
                cancellationToken).ConfigureAwait(false);
            if (hangout.Handled)
            {
                return ToDecision(frame, context, hangout);
            }

            if (!configuration.Options.QuicklyAdvanceEnabled)
            {
                return NoAction(frame, context, [], "advance_disabled");
            }

            var advanceDue = _talkStartedUtc.Value.AddMilliseconds(configuration.Options.BeforeAdvanceDelayMilliseconds);
            if (now < advanceDue)
            {
                return NoAction(frame, context, [], "advance_delay");
            }

            var key = configuration.Options.AdvanceKey == "Space"
                ? (ushort)0x20
                : BetterGiAutoPickRecognizer.ToVirtualKey(configuration.Options.InteractionKey);
            return Act(
                frame,
                context,
                [],
                "advance_dialogue",
                new InputActionGroup("auto-dialogue-advance", [InputAction.KeyPress(key)]));
        }

        _talkStartedUtc = null;
        CancelWait();
        if (now < _nextActionUtc)
        {
            return NoAction(frame, context, [], "action_cooldown");
        }

        var elapsedSinceTalk = _lastTalkUtc is { } lastTalk ? now - lastTalk : TimeSpan.MaxValue;
        var recentlyInTalk = elapsedSinceTalk <= TimeSpan.FromSeconds(10);
        var recentlyInTalkForSubmit = elapsedSinceTalk <= TimeSpan.FromSeconds(3);
        var evaluation = new DialogueSceneEvaluation(
            configuration,
            now,
            recentlyInTalk,
            frame.Sequence,
            recentlyInTalkForSubmit);
        var reward = await _rewardHandler.EvaluateAsync(frame, context, evaluation, cancellationToken).ConfigureAwait(false);
        if (reward.Handled)
        {
            return ToDecision(frame, context, reward);
        }

        foreach (var handler in _nonTalkHandlers)
        {
            var result = await handler.EvaluateAsync(frame, context, evaluation, cancellationToken).ConfigureAwait(false);
            if (result.Handled)
            {
                return ToDecision(frame, context, result);
            }
        }

        return NoAction(frame, context, [], recentlyInTalk ? "recent_dialogue_no_special_scene" : "dialogue_not_active");
    }

    private ValueTask<FeatureDecision> SelectOptionAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        AutoDialogueConfiguration configuration,
        DialogueOptionCandidate candidate,
        string reason,
        IReadOnlyList<string> recognizedOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = $"{candidate.Text}|{candidate.Region.X},{candidate.Region.Y},{candidate.Region.Width},{candidate.Region.Height}|{reason}";
        if (_pendingOptionFingerprint is not null && !fingerprint.Equals(_pendingOptionFingerprint, StringComparison.Ordinal))
        {
            CancelWait();
        }

        if (_voiceWaiter.IsWaiting)
        {
            if (!_voiceWaiter.Update())
            {
                return ValueTask.FromResult(NoAction(frame, context, recognizedOptions, "option_waiting"));
            }

            _pendingOptionFingerprint = null;
            _readyOptionFingerprint = fingerprint;
            return ValueTask.FromResult(NoAction(frame, context, recognizedOptions, "option_wait_completed_recheck"));
        }

        if (!_readyOptionFingerprint?.Equals(fingerprint, StringComparison.Ordinal) ?? true)
        {
            var shouldWait = _voiceWaiter.Start(
                context.Window?.ProcessId ?? 0,
                configuration.Options.AutoWaitDialogueVoiceEnabled
                    ? TimeSpan.FromSeconds(configuration.Options.DialogueVoiceMaxWaitSeconds)
                    : TimeSpan.Zero,
                TimeSpan.FromMilliseconds(configuration.Options.AfterOptionDelayMilliseconds));
            if (shouldWait)
            {
                _pendingOptionFingerprint = fingerprint;
                return ValueTask.FromResult(NoAction(frame, context, recognizedOptions, "option_wait_started"));
            }
        }

        _readyOptionFingerprint = null;
        _pendingOptionFingerprint = null;
        _rewardHandler.NotifyOptionSelected(reason, _clock.UtcNow);
        return ValueTask.FromResult(Act(
            frame,
            context,
            recognizedOptions,
            reason,
            PopupDialogueSceneHandler.Click("auto-dialogue-option", candidate.Region, frame.Size)));
    }

    private FeatureDecision ToDecision(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneResult result) =>
        result.Actions is null
            ? NoAction(frame, context, result.RecognizedOptions ?? [], result.Reason)
            : Act(frame, context, result.RecognizedOptions ?? [], result.Reason, result.Actions);

    private FeatureDecision Act(
        CapturedFrame frame,
        GameContextSnapshot context,
        IReadOnlyList<string> options,
        string reason,
        InputActionGroup actions)
    {
        _nextActionUtc = _clock.UtcNow + MinimumActionInterval;
        _controller.Report(
            frame.Sequence,
            context.UiCategory.ToString(),
            options,
            reason,
            true,
            _voiceWaiter.IsWaiting,
            _voiceWaiter.IsFallback,
            _clock.UtcNow);
        return FeatureDecision.Act(
            new AutomationIntent(FeatureId, Priority, actions, reason));
    }

    private FeatureDecision NoAction(
        CapturedFrame frame,
        GameContextSnapshot context,
        IReadOnlyList<string> options,
        string reason)
    {
        _controller.Report(
            frame.Sequence,
            context.UiCategory.ToString(),
            options,
            reason,
            false,
            _voiceWaiter.IsWaiting,
            _voiceWaiter.IsFallback,
            _clock.UtcNow);
        return FeatureDecision.NoAction(FeatureId, reason);
    }

    private void CancelWait()
    {
        _voiceWaiter.Cancel();
        _pendingOptionFingerprint = null;
        _readyOptionFingerprint = null;
    }
}
