using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.Audio;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Upstream.AutoSkip;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Features.AutoPick;
using OpenCvSharp;

namespace AkashaAutomation.Features.Tests;

public sealed class AutoDialogueRuleTests
{
    private static readonly RegionOfInterest FirstRegion = new(100, 100, 200, 40);
    private static readonly RegionOfInterest LastRegion = new(100, 300, 200, 40);

    [Fact]
    public void Priority_Custom_ShouldWinBeforeBuiltInSelect()
    {
        var candidates = new[] { Candidate("内置选择", FirstRegion), Candidate("用户目标", LastRegion) };
        var decision = BetterGiAutoSkipRules.Decide(
            candidates,
            Lists(select: ["内置"]),
            Options(custom: ["用户目标"]));

        Assert.Equal("custom_priority", decision.Reason);
        Assert.Equal("用户目标", decision.Candidate!.Text);
    }

    [Fact]
    public void Priority_SelectPauseOrangeDefaultPause_ShouldMatchBetterGiOrder()
    {
        var select = BetterGiAutoSkipRules.Decide(
            [Candidate("选择我", FirstRegion), Candidate("暂停我", LastRegion, orange: true)],
            Lists(select: ["选择"], pause: ["暂停"], defaultPause: ["默认"]),
            Options());
        Assert.Equal("select_priority", select.Reason);

        var pause = BetterGiAutoSkipRules.Decide(
            [Candidate("暂停我", FirstRegion, orange: true)],
            Lists(pause: ["暂停"], defaultPause: ["暂停"]),
            Options());
        Assert.True(pause.ShouldPause);
        Assert.Equal("pause_priority", pause.Reason);

        var orange = BetterGiAutoSkipRules.Decide(
            [Candidate("橙色", FirstRegion, orange: true), Candidate("默认暂停", LastRegion)],
            Lists(defaultPause: ["默认暂停"]),
            Options());
        Assert.Equal("orange_option", orange.Reason);

        var defaultPause = BetterGiAutoSkipRules.Decide(
            [Candidate("默认暂停", FirstRegion)],
            Lists(defaultPause: ["默认暂停"]),
            Options());
        Assert.True(defaultPause.ShouldPause);
        Assert.Equal("default_pause_priority", defaultPause.Reason);
    }

    [Theory]
    [InlineData(DialogueOptionStrategy.First, "top")]
    [InlineData(DialogueOptionStrategy.Last, "bottom")]
    [InlineData(DialogueOptionStrategy.Random, "bottom")]
    public void FallbackStrategy_ShouldSelectExpectedCandidate(DialogueOptionStrategy strategy, string expected)
    {
        var decision = BetterGiAutoSkipRules.Decide(
            [Candidate("bottom", LastRegion), Candidate("top", FirstRegion)],
            Lists(),
            Options(strategy),
            deterministicRandomIndex: 1);

        Assert.Equal(expected, decision.Candidate!.Text);
    }

    [Fact]
    public void NoneStrategy_ShouldStillAllowCustomPriorityButOtherwisePause()
    {
        var custom = BetterGiAutoSkipRules.Decide(
            [Candidate("指定", FirstRegion)],
            Lists(),
            Options(DialogueOptionStrategy.None, ["指定"]));
        Assert.True(custom.ShouldSelect);

        var none = BetterGiAutoSkipRules.Decide(
            [Candidate("普通", FirstRegion)],
            Lists(),
            Options(DialogueOptionStrategy.None));
        Assert.False(none.ShouldSelect);
        Assert.Equal("option_selection_disabled", none.Reason);
    }

    private static DialogueOptionCandidate Candidate(string text, RegionOfInterest region, bool orange = false) =>
        new(text, region, orange);

    private static BetterGiAutoSkipLists Lists(
        IReadOnlyList<string>? select = null,
        IReadOnlyList<string>? pause = null,
        IReadOnlyList<string>? defaultPause = null) =>
        new(select ?? [], pause ?? [], defaultPause ?? []);

    private static DialogueOptionRuleOptions Options(
        DialogueOptionStrategy strategy = DialogueOptionStrategy.First,
        IReadOnlyList<string>? custom = null) =>
        new(strategy, custom is not null, custom ?? [], false);
}

public sealed class AutoDialogueFeatureTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public async Task Replay_TalkOption_ShouldClassifyAndSubmitClientClick(int width, int height)
    {
        await using var scenario = await DialogueScenario.CreateAsync(width, height, includeOption: true, "继续前进");

        var result = await scenario.Scheduler.RunOnceAsync();

        Assert.True(result.Captured);
        var decision = Assert.Single(result.Decisions);
        Assert.True(decision.ShouldAct);
        Assert.Equal("fallback_first", decision.Reason);
        var recording = Assert.Single(scenario.Input.Recordings);
        Assert.Collection(
            recording.Actions.Actions,
            action =>
            {
                Assert.Equal(InputActionKind.MouseMoveClient, action.Kind);
                Assert.Equal(width, action.ReferenceWidth);
                Assert.Equal(height, action.ReferenceHeight);
            },
            action => Assert.Equal(InputActionKind.MouseLeftClick, action.Kind));
        Assert.Equal(GameUiCategory.Talk.ToString(), scenario.Controller.Status.UiCategory);
        Assert.Equal("继续前进", Assert.Single(scenario.Controller.Status.LastRecognizedOptions));
    }

    [Fact]
    public async Task Replay_TalkWithoutOption_ShouldSubmitSpaceAdvance()
    {
        await using var scenario = await DialogueScenario.CreateAsync(1920, 1080, includeOption: false, string.Empty);

        _ = await scenario.Scheduler.RunOnceAsync();

        var action = Assert.Single(Assert.Single(scenario.Input.Recordings).Actions.Actions);
        Assert.Equal(InputActionKind.KeyPress, action.Kind);
        Assert.Equal((ushort)0x20, action.VirtualKey);
    }

    [Fact]
    public async Task Replay_OptionBubbleWithoutOcr_ShouldStillClickFirstBubble()
    {
        await using var scenario = await DialogueScenario.CreateAsync(
            1920,
            1080,
            includeOption: true,
            text: string.Empty);

        var result = await scenario.Scheduler.RunOnceAsync();

        var decision = Assert.Single(result.Decisions);
        Assert.True(decision.ShouldAct);
        Assert.Equal("fallback_first", decision.Reason);
        Assert.Collection(
            Assert.Single(scenario.Input.Recordings).Actions.Actions,
            action => Assert.Equal(InputActionKind.MouseMoveClient, action.Kind),
            action => Assert.Equal(InputActionKind.MouseLeftClick, action.Kind));
    }

    [Fact]
    public async Task Replay_TalkInteractionPrompt_ShouldUseInteractionKey()
    {
        await using var scenario = await DialogueScenario.CreateAsync(
            1920,
            1080,
            includeOption: false,
            text: string.Empty,
            dialogueInteractionKey: "F");

        var result = await scenario.Scheduler.RunOnceAsync();

        var decision = Assert.Single(result.Decisions);
        Assert.Equal("dialogue_interaction_key", decision.Reason);
        var action = Assert.Single(Assert.Single(scenario.Input.Recordings).Actions.Actions);
        Assert.Equal(InputActionKind.KeyPress, action.Kind);
        Assert.Equal((ushort)'F', action.VirtualKey);
    }

    [Fact]
    public async Task Replay_NonTalkSimilarFrame_ShouldNotSubmitDialogueIntent()
    {
        await using var scenario = await DialogueScenario.CreateAsync(1920, 1080, includeOption: true, "不应选择", includeTalkMarker: false);

        var result = await scenario.Scheduler.RunOnceAsync();

        Assert.False(Assert.Single(result.Decisions).ShouldAct);
        Assert.Empty(scenario.Input.Recordings);
        Assert.Equal("dialogue_not_active", scenario.Controller.Status.LastDecisionReason);
    }

    [Fact]
    public async Task AutoPick_ShouldBeSuppressedByTalkContextBeforeRecognition()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var controller = new AutoPickController(resolver);
        controller.SetEnabled(true);
        using var recognizer = new BetterGiAutoPickRecognizer(new OpenCvTemplateMatcher(), resolver);
        await using var ocr = new FakeOcrEngine("unused");
        var feature = new AutoPickFeature(controller, recognizer, ocr, new InMemoryDiagnosticsSink(), new FakeClock());
        using var frame = CapturedFrame.TakeOwnership(new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black), 1, DateTimeOffset.UnixEpoch, "talk");
        var context = DialogueScenario.Context(1920, 1080) with { UiCategory = GameUiCategory.Talk };

        var decision = await feature.EvaluateAsync(frame, context);

        Assert.Equal("dialogue_active", decision.Reason);
        Assert.Equal(0, ocr.CallCount);
    }

    [Fact]
    public void FixedDelayWaiter_ShouldUseVirtualTimeWithoutSleeping()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var waiter = new FixedDelayDialogueOptionVoiceWaiter(clock);

        Assert.True(waiter.Start(1, TimeSpan.Zero, TimeSpan.FromSeconds(2)));
        Assert.False(waiter.Update());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(waiter.Update());
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public async Task SileroWaiter_MissingModel_ShouldFallBackAndReleaseImmediatelyOnCancel()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        await using var waiter = new SileroDialogueOptionVoiceWaiter(clock, new MissingAssetResolver());

        Assert.True(waiter.Start(123, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2)));
        Assert.True(waiter.IsFallback);
        Assert.False(waiter.Update());
        waiter.Cancel();
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public async Task SileroWaiter_Cancel_ShouldDisposeActiveProcessLoopbackDetector()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var detector = new TrackingDialogueVoiceDetector(123);
        await using var waiter = new SileroDialogueOptionVoiceWaiter(
            clock,
            new RootedAssetPathResolver(AppContext.BaseDirectory),
            (processId, _) =>
            {
                Assert.Equal(123, processId);
                return detector;
            });

        Assert.True(waiter.Start(123, TimeSpan.FromSeconds(30), TimeSpan.Zero));
        Assert.False(detector.IsDisposed);

        waiter.Cancel();

        Assert.False(waiter.IsWaiting);
        Assert.True(detector.IsDisposed);
    }

    [Fact]
    public async Task SileroWaiter_CancelDuringUpdate_ShouldWaitBeforeDisposingDetector()
    {
        var detector = new BlockingDialogueVoiceDetector(123);
        await using var waiter = new SileroDialogueOptionVoiceWaiter(
            new FakeClock(DateTimeOffset.UnixEpoch),
            new RootedAssetPathResolver(AppContext.BaseDirectory),
            (_, _) => detector);
        Assert.True(waiter.Start(123, TimeSpan.FromSeconds(30), TimeSpan.Zero));

        var updateTask = Task.Run(waiter.Update);
        Assert.True(detector.UpdateEntered.Wait(TimeSpan.FromSeconds(5)));
        using var cancelStarted = new ManualResetEventSlim(false);
        var cancelTask = Task.Run(() =>
        {
            cancelStarted.Set();
            waiter.Cancel();
        });
        Assert.True(cancelStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.False(cancelTask.IsCompleted);
        Assert.False(detector.IsDisposed);

        detector.AllowUpdateToComplete.Set();
        await Task.WhenAll(updateTask, cancelTask);

        Assert.True(detector.IsDisposed);
        Assert.False(waiter.IsWaiting);
    }

    [Fact]
    public async Task SileroWaiter_NewWaitForSameProcess_ShouldReuseDetector()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var detector = new TrackingDialogueVoiceDetector(123);
        var factoryCalls = 0;
        await using var waiter = new SileroDialogueOptionVoiceWaiter(
            clock,
            new RootedAssetPathResolver(AppContext.BaseDirectory),
            (_, _) =>
            {
                factoryCalls++;
                return detector;
            });

        Assert.True(waiter.Start(123, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(waiter.Update());
        Assert.True(waiter.Start(123, TimeSpan.FromSeconds(1), TimeSpan.Zero));

        Assert.Equal(1, factoryCalls);
        Assert.Equal(2, detector.ResetCount);
        Assert.False(detector.IsDisposed);
    }

    [Fact]
    public async Task DisablingController_ShouldImmediatelyCancelActiveVoiceWait()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var controller = new AutoDialogueController(resolver);
        var waiter = new TrackingVoiceWaiter();
        var reward = new RewardDialogueSceneHandler(recognizer);
        var hangout = new HangoutDialogueSceneHandler(recognizer);
        _ = new AutoDialogueFeature(controller, recognizer, waiter, reward, hangout, [reward, hangout], new FakeClock());
        Assert.True(waiter.Start(1, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)));

        controller.SetEnabled(false);

        Assert.Equal(1, waiter.CancelCount);
        Assert.False(waiter.IsWaiting);
        await waiter.DisposeAsync();
    }

    [Fact]
    public async Task BlackScreenHandler_PositiveAndSimilarNegative_ShouldRemainSeparate()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var handler = new BlackScreenDialogueSceneHandler(recognizer);
        var controller = new AutoDialogueController(resolver);
        controller.SetEnabled(true);
        var evaluation = new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, false, 1);

        using var positiveImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        Cv2.Rectangle(positiveImage, new Rect(0, 360, 1152, 360), Scalar.Black, -1);
        using var positive = CapturedFrame.TakeOwnership(positiveImage.Clone(), 1, DateTimeOffset.UnixEpoch, "black-positive");
        var positiveResult = await handler.EvaluateAsync(positive, Context(1920, 1080), evaluation);
        Assert.True(positiveResult.Handled);
        Assert.Equal("black_screen", positiveResult.Reason);

        using var negative = CapturedFrame.TakeOwnership(new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black), 2, DateTimeOffset.UnixEpoch, "black-negative");
        var negativeResult = await handler.EvaluateAsync(negative, Context(1920, 1080), evaluation with { FrameSequence = 2 });
        Assert.False(negativeResult.Handled);
    }

    [Fact]
    public async Task PopupHandler_PageClosePositiveAndInactiveNegative_ShouldBeDistinct()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var handler = new PopupDialogueSceneHandler(recognizer);
        var controller = new AutoDialogueController(resolver);
        controller.SetEnabled(true);
        using var image = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(image, BetterGiAssetPaths.AutoSkipPageClose, 1, 1800, 40);
        using var frame = CapturedFrame.TakeOwnership(image.Clone(), 1, DateTimeOffset.UnixEpoch, "popup");

        var positive = await handler.EvaluateAsync(
            frame,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, true, 1));
        var negative = await handler.EvaluateAsync(
            frame,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, false, 1));

        Assert.True(positive.Handled);
        Assert.Equal("popup_page_close", positive.Reason);
        Assert.False(negative.Handled);
    }

    [Fact]
    public async Task SubmitGoodsHandler_PositiveAndMissingExclamationNegative_ShouldBeDistinct()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var controller = new AutoDialogueController(resolver);
        controller.SetEnabled(true);
        var evaluation = new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, true, 1);
        using var positiveImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(positiveImage, BetterGiAssetPaths.AutoSkipSubmitExclamation, 1, 300, 100);
        PlaceTemplate(positiveImage, BetterGiAssetPaths.AutoSkipSubmitGoods, 1, 400, 200);
        using var positive = CapturedFrame.TakeOwnership(positiveImage.Clone(), 1, DateTimeOffset.UnixEpoch, "submit-positive");
        var positiveHandler = new SubmitGoodsDialogueSceneHandler(recognizer);

        var positiveResult = await positiveHandler.EvaluateAsync(positive, Context(1920, 1080), evaluation);
        Assert.True(positiveResult.Handled);
        Assert.Equal("submit_select_goods", positiveResult.Reason);

        using var negativeImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(negativeImage, BetterGiAssetPaths.AutoSkipSubmitGoods, 1, 400, 200);
        using var negative = CapturedFrame.TakeOwnership(negativeImage.Clone(), 2, DateTimeOffset.UnixEpoch, "submit-negative");
        var negativeHandler = new SubmitGoodsDialogueSceneHandler(recognizer);
        var negativeResult = await negativeHandler.EvaluateAsync(negative, Context(1920, 1080), evaluation with { FrameSequence = 2 });
        Assert.False(negativeResult.Handled);
    }

    [Fact]
    public async Task RewardHandler_DailyAndReExploreStateMachines_ShouldUseSeparateFrames()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var controller = new AutoDialogueController(resolver);
        controller.SetEnabled(true);
        var handler = new RewardDialogueSceneHandler(recognizer);

        handler.NotifyOptionSelected("daily_reward_option", DateTimeOffset.UnixEpoch);
        using var dailyNegative = CapturedFrame.TakeOwnership(
            new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20)),
            0,
            DateTimeOffset.UnixEpoch,
            "daily-pending-negative");
        var dailyNegativeResult = await handler.EvaluateAsync(
            dailyNegative,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, false, 0));
        Assert.False(dailyNegativeResult.Handled);
        Assert.Equal("reward_target_not_found", dailyNegativeResult.Reason);

        using var dailyImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(dailyImage, BetterGiAssetPaths.AutoSkipPrimogem, 1, 500, 400);
        using var daily = CapturedFrame.TakeOwnership(dailyImage.Clone(), 1, DateTimeOffset.UnixEpoch, "daily");
        var dailyResult = await handler.EvaluateAsync(
            daily,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, false, 1));
        Assert.Equal("daily_reward_collect", dailyResult.Reason);

        handler.NotifyOptionSelected("reexplore_option", DateTimeOffset.UnixEpoch);
        using var wrongStageImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(wrongStageImage, BetterGiAssetPaths.AutoSkipReExplore, 1, 1100, 850);
        using var wrongStage = CapturedFrame.TakeOwnership(wrongStageImage.Clone(), 2, DateTimeOffset.UnixEpoch, "reexplore-before-collect");
        var wrongStageResult = await handler.EvaluateAsync(
            wrongStage,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch.AddMilliseconds(500), false, 2));
        Assert.False(wrongStageResult.Handled);
        Assert.Equal("reward_target_not_found", wrongStageResult.Reason);

        using var collectImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(collectImage, BetterGiAssetPaths.AutoSkipCollect, 1, 100, 800);
        using var collect = CapturedFrame.TakeOwnership(collectImage.Clone(), 2, DateTimeOffset.UnixEpoch, "collect");
        var collectResult = await handler.EvaluateAsync(
            collect,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch.AddSeconds(1), false, 2));
        Assert.Equal("expedition_collect", collectResult.Reason);

        using var reImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(reImage, BetterGiAssetPaths.AutoSkipReExplore, 1, 1100, 850);
        using var re = CapturedFrame.TakeOwnership(reImage.Clone(), 3, DateTimeOffset.UnixEpoch, "reexplore");
        var reResult = await handler.EvaluateAsync(
            re,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch.AddSeconds(2), false, 3));
        Assert.Equal("expedition_reexplore", reResult.Reason);

        using var blank = CapturedFrame.TakeOwnership(new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20)), 4, DateTimeOffset.UnixEpoch, "blank");
        var inactive = await handler.EvaluateAsync(
            blank,
            Context(1920, 1080),
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch.AddSeconds(3), false, 4));
        Assert.False(inactive.Handled);
    }

    [Fact]
    public async Task HangoutHandler_SkipPositiveAndBlankNegative_ShouldBeDistinct()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var controller = new AutoDialogueController(resolver);
        controller.SetOptions(new AutoDialogueOptions { Enabled = true, AutoHangoutEnabled = true });
        var handler = new HangoutDialogueSceneHandler(recognizer);
        var context = Context(1920, 1080) with { UiCategory = GameUiCategory.Talk };
        var evaluation = new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, true, 1);
        using var image = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(image, BetterGiAssetPaths.AutoSkipHangoutSkip, 1, 100, 50);
        using var positive = CapturedFrame.TakeOwnership(image.Clone(), 1, DateTimeOffset.UnixEpoch, "hangout-skip");

        var positiveResult = await handler.EvaluateAsync(positive, context, evaluation);
        Assert.True(positiveResult.Handled);
        Assert.Equal("hangout_skip", positiveResult.Reason);

        using var blank = CapturedFrame.TakeOwnership(new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20)), 2, DateTimeOffset.UnixEpoch, "hangout-blank");
        var negative = await handler.EvaluateAsync(blank, context, evaluation with { FrameSequence = 2 });
        Assert.False(negative.Handled);
    }

    [Fact]
    public async Task HangoutHandler_ConfiguredEndingKeyword_ShouldSelectMatchingOption()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var initialController = new AutoDialogueController(resolver);
        var configuredEnding = initialController.Snapshot.HangoutOptions.First();
        var keyword = configuredEnding.Value[0];
        await using var ocr = new FakeOcrEngine(keyword);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        var controller = new AutoDialogueController(resolver);
        controller.SetOptions(new AutoDialogueOptions
        {
            Enabled = true,
            AutoHangoutEnabled = true,
            HangoutEnding = configuredEnding.Key,
        });
        var handler = new HangoutDialogueSceneHandler(recognizer);
        var context = Context(1920, 1080) with { UiCategory = GameUiCategory.Talk };
        using var image = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(image, BetterGiAssetPaths.AutoSkipHangoutUnselected, 1, 1000, 300);
        using var frame = CapturedFrame.TakeOwnership(image.Clone(), 1, DateTimeOffset.UnixEpoch, "hangout-option");

        var result = await handler.EvaluateAsync(
            frame,
            context,
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, true, 1));

        Assert.True(result.Handled);
        Assert.Equal("hangout_option", result.Reason);
        Assert.Contains(keyword, Assert.Single(result.RecognizedOptions!));
        Assert.NotNull(result.Actions);

        using var invalidLayoutImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(20));
        PlaceTemplate(invalidLayoutImage, BetterGiAssetPaths.AutoSkipHangoutUnselected, 1, 950, 300);
        using var invalidLayout = CapturedFrame.TakeOwnership(
            invalidLayoutImage.Clone(),
            2,
            DateTimeOffset.UnixEpoch,
            "hangout-option-center-negative");
        var invalidLayoutResult = await handler.EvaluateAsync(
            invalidLayout,
            context,
            new DialogueSceneEvaluation(controller.Snapshot, DateTimeOffset.UnixEpoch, true, 2));
        Assert.False(invalidLayoutResult.Handled);
    }

    [Fact]
    public async Task PopupRecognizer_ItemTriangleAndCharacterBanner_ShouldRejectSimilarShapes()
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        await using var ocr = new FakeOcrEngine(string.Empty);
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);

        using var triangleImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        using (var hsv = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black))
        {
            Cv2.FillConvexPoly(hsv, [new Point(950, 990), new Point(960, 990), new Point(955, 998)], new Scalar(10, 250, 240));
            Cv2.CvtColor(hsv, triangleImage, ColorConversionCodes.HSV2BGR);
        }
        using var triangleFrame = CapturedFrame.TakeOwnership(triangleImage.Clone(), 1, DateTimeOffset.UnixEpoch, "triangle");
        Assert.NotNull(recognizer.FindBottomTriangle(triangleFrame));

        using var rectangleImage = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(rectangleImage, new Rect(950, 990, 10, 8), Scalar.White, -1);
        using var rectangleFrame = CapturedFrame.TakeOwnership(rectangleImage.Clone(), 2, DateTimeOffset.UnixEpoch, "rectangle");
        Assert.Null(recognizer.FindBottomTriangle(rectangleFrame));

        using var bannerHsv = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(bannerHsv, new Rect(40, 380, 920, 310), new Scalar(22, 17, 240), -1);
        Cv2.Rectangle(bannerHsv, new Rect(960, 380, 920, 310), new Scalar(110, 70, 100), -1);
        using var bannerBgr = bannerHsv.CvtColor(ColorConversionCodes.HSV2BGR);
        using var bannerFrame = CapturedFrame.TakeOwnership(bannerBgr.Clone(), 3, DateTimeOffset.UnixEpoch, "character-banner");
        Assert.NotNull(recognizer.FindCharacterPopup(bannerFrame));

        using var smallHsv = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        Cv2.Rectangle(smallHsv, new Rect(500, 450, 600, 100), new Scalar(22, 17, 240), -1);
        using var smallBgr = smallHsv.CvtColor(ColorConversionCodes.HSV2BGR);
        using var smallFrame = CapturedFrame.TakeOwnership(smallBgr.Clone(), 4, DateTimeOffset.UnixEpoch, "small-banner");
        Assert.Null(recognizer.FindCharacterPopup(smallFrame));
    }

    private static GameContextSnapshot Context(int width, int height) => DialogueScenario.Context(width, height);

    private static void PlaceTemplate(Mat target, string relativePath, double scale, int x, int y) =>
        DialogueScenario.PlaceTemplate(target, relativePath, scale, x, y);

    private sealed class DialogueScenario : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly ReplayCaptureSource _capture;
        private readonly BetterGiAutoDialogueRecognizer _recognizer;
        private readonly IOcrEngine _ocr;
        private readonly IDialogueOptionVoiceWaiter _voiceWaiter;

        private DialogueScenario(
            string directory,
            ReplayCaptureSource capture,
            BetterGiAutoDialogueRecognizer recognizer,
            IOcrEngine ocr,
            IDialogueOptionVoiceWaiter voiceWaiter,
            AutoDialogueController controller,
            RecordingInputService input,
            SingleFrameScheduler scheduler)
        {
            _directory = directory;
            _capture = capture;
            _recognizer = recognizer;
            _ocr = ocr;
            _voiceWaiter = voiceWaiter;
            Controller = controller;
            Input = input;
            Scheduler = scheduler;
        }

        public AutoDialogueController Controller { get; }
        public RecordingInputService Input { get; }
        public SingleFrameScheduler Scheduler { get; }

        public static Task<DialogueScenario> CreateAsync(
            int width,
            int height,
            bool includeOption,
            string text,
            bool includeTalkMarker = true,
            string? dialogueInteractionKey = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"akasha-dialogue-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "frame.png");
            var scale = height / 1080d;
            using (var image = new Mat(height, width, MatType.CV_8UC3, Scalar.All(20)))
            {
                if (includeTalkMarker)
                {
                    PlaceTemplate(image, BetterGiAssetPaths.AutoSkipStopAuto, scale, (int)(20 * scale), (int)(20 * scale));
                }

                if (includeOption)
                {
                    PlaceTemplate(image, BetterGiAssetPaths.AutoSkipOptionIcon, scale, (int)(1000 * scale), (int)(300 * scale));
                }

                if (dialogueInteractionKey is not null)
                {
                    var keyAsset = dialogueInteractionKey switch
                    {
                        "E" => BetterGiAssetPaths.AutoPickKeyE,
                        "F" => BetterGiAssetPaths.AutoPickKeyF,
                        "G" => BetterGiAssetPaths.AutoPickKeyG,
                        _ => throw new ArgumentOutOfRangeException(nameof(dialogueInteractionKey)),
                    };
                    PlaceTemplate(image, keyAsset, scale, (int)(1210 * scale), (int)(400 * scale));
                }

                Assert.True(Cv2.ImWrite(path, image));
            }

            var clock = new FakeClock(DateTimeOffset.UnixEpoch);
            var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
            var ocr = new FakeOcrEngine(text);
            var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
            var controller = new AutoDialogueController(resolver);
            controller.SetEnabled(true);
            var voiceWaiter = new FixedDelayDialogueOptionVoiceWaiter(clock);
            var reward = new RewardDialogueSceneHandler(recognizer);
            var hangout = new HangoutDialogueSceneHandler(recognizer);
            IAutoDialogueSceneHandler[] handlers =
            [
                reward,
                hangout,
                new PopupDialogueSceneHandler(recognizer),
                new SubmitGoodsDialogueSceneHandler(recognizer),
                new BlackScreenDialogueSceneHandler(recognizer),
            ];
            var feature = new AutoDialogueFeature(controller, recognizer, voiceWaiter, reward, hangout, handlers, clock);
            var diagnostics = new InMemoryDiagnosticsSink();
            var input = new RecordingInputService();
            var arbiter = new InputArbiter(input, diagnostics, clock);
            var capture = new ReplayCaptureSource([path], clock);
            var scheduler = new SingleFrameScheduler(
                capture,
                new StaticContextProvider(Context(width, height)),
                [feature],
                arbiter,
                diagnostics,
                clock,
                recognizer);
            return Task.FromResult(new DialogueScenario(directory, capture, recognizer, ocr, voiceWaiter, controller, input, scheduler));
        }

        public static GameContextSnapshot Context(int width, int height) =>
            new(
                DateTimeOffset.UnixEpoch,
                new GameWindowInfo(1, 1, "GenshinImpact", "Genshin Impact", new CaptureSize(width, height), true));

        public static void PlaceTemplate(Mat target, string relativePath, double scale, int x, int y)
        {
            using var original = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, relativePath), ImreadModes.Color);
            using var resized = original.Resize(
                new Size(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale))),
                interpolation: scale > 1 ? InterpolationFlags.Linear : InterpolationFlags.Area);
            using var destination = new Mat(target, new Rect(x, y, resized.Width, resized.Height));
            resized.CopyTo(destination);
        }

        public async ValueTask DisposeAsync()
        {
            await _capture.DisposeAsync();
            await _ocr.DisposeAsync();
            await _voiceWaiter.DisposeAsync();
            await Input.DisposeAsync();
            _recognizer.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeOcrEngine(string text) : IOcrEngine
    {
        public int CallCount { get; private set; }

        public ValueTask<OcrResult> RecognizeAsync(
            CapturedFrame frame,
            RegionOfInterest? region = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (string.IsNullOrEmpty(text) || region is null)
            {
                return ValueTask.FromResult(OcrResult.Empty());
            }

            var roi = region.Value;
            var textRegion = new RegionOfInterest(
                roi.X + Math.Min(20, roi.Width - 1),
                roi.Y + Math.Min(30, roi.Height - 1),
                Math.Max(1, Math.Min(200, roi.Width - Math.Min(20, roi.Width - 1))),
                Math.Max(1, Math.Min(40, roi.Height - Math.Min(30, roi.Height - 1))));
            return ValueTask.FromResult(
                new OcrResult(text, [new OcrTextRegion(text, 0.99, textRegion)], TimeSpan.Zero));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StaticContextProvider(GameContextSnapshot context) : IGameContextProvider
    {
        public ValueTask<GameContextSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(context);
    }

    private sealed class MissingAssetResolver : IAssetPathResolver
    {
        public string Resolve(string relativePath) =>
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}", relativePath);
    }

    private sealed class TrackingVoiceWaiter : IDialogueOptionVoiceWaiter
    {
        public bool IsWaiting { get; private set; }
        public bool IsFallback => false;
        public int CancelCount { get; private set; }

        public bool Start(int processId, TimeSpan maximumWait, TimeSpan fallbackDelay)
        {
            IsWaiting = true;
            return true;
        }

        public bool Update() => !IsWaiting;

        public void Cancel()
        {
            CancelCount++;
            IsWaiting = false;
        }

        public ValueTask DisposeAsync()
        {
            IsWaiting = false;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingDialogueVoiceDetector(int processId) : IBetterGiDialogueVoiceDetector
    {
        public int ProcessId { get; } = processId;
        public bool IsDisposed { get; private set; }
        public int ResetCount { get; private set; }

        public void Reset()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            ResetCount++;
        }

        public float Update()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return 0;
        }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class BlockingDialogueVoiceDetector(int processId) : IBetterGiDialogueVoiceDetector
    {
        public int ProcessId { get; } = processId;
        public ManualResetEventSlim UpdateEntered { get; } = new(false);
        public ManualResetEventSlim AllowUpdateToComplete { get; } = new(false);
        public bool IsDisposed { get; private set; }

        public void Reset() => ObjectDisposedException.ThrowIf(IsDisposed, this);

        public float Update()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            UpdateEntered.Set();
            if (!AllowUpdateToComplete.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Test detector was not released.");
            }

            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return 0;
        }

        public void Dispose()
        {
            IsDisposed = true;
            UpdateEntered.Dispose();
            AllowUpdateToComplete.Dispose();
        }
    }
}
