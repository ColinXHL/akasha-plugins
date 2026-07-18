using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;

namespace AkashaAutomation.Features.AutoDialogue;

public interface IAutoDialogueSceneHandler
{
    string Id { get; }

    ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default);
}

public sealed record DialogueSceneEvaluation(
    AutoDialogueConfiguration Configuration,
    DateTimeOffset NowUtc,
    bool RecentlyInTalk,
    long FrameSequence,
    bool? RecentlyInTalkForSubmit = null);

public sealed record DialogueSceneResult(
    bool Handled,
    string Reason,
    InputActionGroup? Actions = null,
    IReadOnlyList<string>? RecognizedOptions = null)
{
    public static DialogueSceneResult NoMatch(string reason) => new(false, reason);

    public static DialogueSceneResult Act(string reason, InputActionGroup actions, IReadOnlyList<string>? options = null) =>
        new(true, reason, actions, options);

    public static DialogueSceneResult Pause(string reason, IReadOnlyList<string>? options = null) =>
        new(true, reason, null, options);
}

public sealed class PopupDialogueSceneHandler(BetterGiAutoDialogueRecognizer recognizer) : IAutoDialogueSceneHandler
{
    private DateTimeOffset _nextItemPopupUtc = DateTimeOffset.MinValue;

    public string Id => "popup";

    public ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!evaluation.RecentlyInTalk || !evaluation.Configuration.Options.ClosePopupPagesEnabled)
        {
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("popup_inactive"));
        }

        var close = recognizer.FindPageClose(frame);
        if (close.IsMatch)
        {
            return ValueTask.FromResult(DialogueSceneResult.Act(
                "popup_page_close",
                new InputActionGroup("auto-dialogue-popup-close", [InputAction.KeyPress(0x1B)])));
        }

        var triangle = evaluation.NowUtc >= _nextItemPopupUtc
            ? recognizer.FindBottomTriangle(frame)
            : null;
        if (triangle is { } triangleRegion)
        {
            _nextItemPopupUtc = evaluation.NowUtc.AddSeconds(1);
            return ValueTask.FromResult(DialogueSceneResult.Act(
                "item_popup_triangle",
                Click("auto-dialogue-item-popup", triangleRegion, frame.Size)));
        }

        if (recognizer.FindCharacterPopup(frame) is not null)
        {
            return ValueTask.FromResult(DialogueSceneResult.Act(
                "character_popup",
                new InputActionGroup(
                    "auto-dialogue-character-popup",
                    [InputAction.MouseMoveClient(100, 100, frame.Size.Width, frame.Size.Height), InputAction.MouseLeftClick()])));
        }

        return ValueTask.FromResult(DialogueSceneResult.NoMatch("popup_not_found"));
    }

    internal static InputActionGroup Click(string name, RegionOfInterest region, CaptureSize referenceSize) =>
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

public sealed class BlackScreenDialogueSceneHandler(BetterGiAutoDialogueRecognizer recognizer) : IAutoDialogueSceneHandler
{
    private DateTimeOffset _nextClickUtc = DateTimeOffset.MinValue;

    public string Id => "black-screen";

    public ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.IsTalk)
        {
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("talk_active"));
        }

        if (evaluation.NowUtc < _nextClickUtc)
        {
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("black_screen_cooldown"));
        }

        var ratio = recognizer.GetBlackScreenRatio(frame);
        if (ratio is < 0.5 or >= 0.98999)
        {
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("black_screen_threshold"));
        }

        _nextClickUtc = evaluation.NowUtc.AddMilliseconds(1200);
        return ValueTask.FromResult(DialogueSceneResult.Act(
            "black_screen",
            new InputActionGroup(
                "auto-dialogue-black-screen",
                [
                    InputAction.MouseMoveClient(
                        frame.Size.Width / 2,
                        frame.Size.Height / 2,
                        frame.Size.Width,
                        frame.Size.Height),
                    InputAction.MouseLeftClick(),
                ])));
    }
}

public sealed class SubmitGoodsDialogueSceneHandler(BetterGiAutoDialogueRecognizer recognizer) : IAutoDialogueSceneHandler
{
    private SubmitStage _stage;
    private DateTimeOffset _stageExpiresUtc;

    public string Id => "submit-goods";

    public ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var recentlyInTalkForSubmit = evaluation.RecentlyInTalkForSubmit ?? evaluation.RecentlyInTalk;
        if (!evaluation.Configuration.Options.SubmitGoodsEnabled || !recentlyInTalkForSubmit)
        {
            ResetIfExpired(evaluation.NowUtc);
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("submit_inactive"));
        }

        ResetIfExpired(evaluation.NowUtc);
        if (_stage is SubmitStage.AwaitPutIn or SubmitStage.AwaitDelivery)
        {
            var confirm = recognizer.FindConfirm(frame);
            if (confirm.IsMatch && confirm.Region is { } confirmRegion)
            {
                var reason = _stage == SubmitStage.AwaitPutIn ? "submit_put_in" : "submit_delivery";
                _stage = _stage == SubmitStage.AwaitPutIn ? SubmitStage.AwaitDelivery : SubmitStage.None;
                _stageExpiresUtc = evaluation.NowUtc.AddSeconds(5);
                return ValueTask.FromResult(DialogueSceneResult.Act(
                    reason,
                    PopupDialogueSceneHandler.Click($"auto-dialogue-{reason}", confirmRegion, frame.Size)));
            }
        }

        if (_stage == SubmitStage.None &&
            recognizer.FindSubmitExclamation(frame).IsMatch &&
            recognizer.FindSubmitGoods(frame) is { IsMatch: true, Region: { } goodsRegion })
        {
            _stage = SubmitStage.AwaitPutIn;
            _stageExpiresUtc = evaluation.NowUtc.AddSeconds(5);
            return ValueTask.FromResult(DialogueSceneResult.Act(
                "submit_select_goods",
                PopupDialogueSceneHandler.Click("auto-dialogue-submit-select", goodsRegion, frame.Size)));
        }

        return ValueTask.FromResult(DialogueSceneResult.NoMatch("submit_not_found"));
    }

    private void ResetIfExpired(DateTimeOffset now)
    {
        if (_stage != SubmitStage.None && now >= _stageExpiresUtc)
        {
            _stage = SubmitStage.None;
        }
    }

    private enum SubmitStage
    {
        None,
        AwaitPutIn,
        AwaitDelivery,
    }
}

public sealed class RewardDialogueSceneHandler(BetterGiAutoDialogueRecognizer recognizer) : IAutoDialogueSceneHandler
{
    private PendingReward _pending;
    private DateTimeOffset _expiresUtc;
    private bool _collected;

    public string Id => "reward";

    public void NotifyOptionSelected(string reason, DateTimeOffset nowUtc)
    {
        _pending = reason switch
        {
            "daily_reward_option" => PendingReward.Daily,
            "reexplore_option" => PendingReward.ReExplore,
            _ => PendingReward.None,
        };
        _collected = false;
        _expiresUtc = nowUtc.AddSeconds(10);
    }

    public ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_pending == PendingReward.None || evaluation.NowUtc >= _expiresUtc)
        {
            _pending = PendingReward.None;
            return ValueTask.FromResult(DialogueSceneResult.NoMatch("reward_inactive"));
        }

        if (_pending == PendingReward.Daily && evaluation.Configuration.Options.AutoGetDailyRewardsEnabled &&
            recognizer.FindDailyReward(frame).IsMatch)
        {
            _pending = PendingReward.None;
            var scale = frame.Size.Height / 1080d;
            var target = new RegionOfInterest((int)(950 * scale), (int)(890 * scale), (int)(20 * scale), (int)(20 * scale));
            return ValueTask.FromResult(DialogueSceneResult.Act(
                "daily_reward_collect",
                PopupDialogueSceneHandler.Click("auto-dialogue-daily-reward", target, frame.Size)));
        }

        if (_pending == PendingReward.ReExplore && evaluation.Configuration.Options.AutoReExploreEnabled)
        {
            if (!_collected && recognizer.FindCollect(frame) is { IsMatch: true, Region: { } collect })
            {
                _collected = true;
                return ValueTask.FromResult(DialogueSceneResult.Act(
                    "expedition_collect",
                    PopupDialogueSceneHandler.Click("auto-dialogue-expedition-collect", collect, frame.Size)));
            }

            if (_collected && recognizer.FindReExplore(frame) is { IsMatch: true, Region: { } reExplore })
            {
                _pending = PendingReward.None;
                return ValueTask.FromResult(DialogueSceneResult.Act(
                    "expedition_reexplore",
                    PopupDialogueSceneHandler.Click("auto-dialogue-expedition-reexplore", reExplore, frame.Size)));
            }
        }

        return ValueTask.FromResult(DialogueSceneResult.NoMatch("reward_target_not_found"));
    }

    private enum PendingReward
    {
        None,
        Daily,
        ReExplore,
    }
}

public sealed class HangoutDialogueSceneHandler(BetterGiAutoDialogueRecognizer recognizer) : IAutoDialogueSceneHandler
{
    private DateTimeOffset _nextActionUtc = DateTimeOffset.MinValue;

    public string Id => "hangout";

    public async ValueTask<DialogueSceneResult> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        DialogueSceneEvaluation evaluation,
        CancellationToken cancellationToken = default)
    {
        var options = evaluation.Configuration.Options;
        if (!context.IsTalk || !options.AutoHangoutEnabled)
        {
            return DialogueSceneResult.NoMatch("hangout_inactive");
        }

        if (evaluation.NowUtc < _nextActionUtc)
        {
            return DialogueSceneResult.NoMatch("hangout_cooldown");
        }

        var candidates = await recognizer.FindHangoutOptionsAsync(frame, cancellationToken).ConfigureAwait(false);
        DialogueOptionCandidate? selected = null;
        if (options.HangoutEnding.Length > 0 &&
            evaluation.Configuration.HangoutOptions.TryGetValue(options.HangoutEnding, out var keywords))
        {
            selected = candidates.FirstOrDefault(candidate =>
                keywords.Any(keyword => candidate.Text.Contains(keyword, StringComparison.Ordinal)));
        }

        selected ??= candidates.FirstOrDefault(candidate => candidate.Kind == DialogueOptionKind.HangoutUnselected);
        selected ??= candidates.FirstOrDefault();
        if (selected is not null)
        {
            _nextActionUtc = evaluation.NowUtc.AddMilliseconds(1200);
            return DialogueSceneResult.Act(
                "hangout_option",
                PopupDialogueSceneHandler.Click("auto-dialogue-hangout-option", selected.Region, frame.Size),
                candidates.Select(candidate => candidate.Text).ToArray());
        }

        var skip = recognizer.FindHangoutSkip(frame);
        if (options.AutoHangoutSkipEnabled && skip.IsMatch && skip.Region is { } skipRegion)
        {
            _nextActionUtc = evaluation.NowUtc.AddMilliseconds(1200);
            return DialogueSceneResult.Act(
                "hangout_skip",
                PopupDialogueSceneHandler.Click("auto-dialogue-hangout-skip", skipRegion, frame.Size));
        }

        return DialogueSceneResult.NoMatch("hangout_not_found");
    }
}
