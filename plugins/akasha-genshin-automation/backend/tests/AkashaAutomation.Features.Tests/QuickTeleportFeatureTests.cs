using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.QuickTeleport;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.QuickTeleport;
using OpenCvSharp;

namespace AkashaAutomation.Features.Tests;

public sealed class QuickTeleportFeatureTests
{
    [Fact]
    public async Task BigMapDetector_ShouldRecognizeScaleButton()
    {
        using var frame = CreateFrame((image, resolver) =>
            PlaceTemplate(image, resolver.Resolve(BetterGiAssetPaths.QuickTeleportMapScaleButton), 35, 500));
        await using var ocr = new FixedOcrEngine("传送锚点");
        using var recognizer = CreateRecognizer(ocr);

        var category = await recognizer.DetectAsync(frame, BigMapContext());

        Assert.Equal(GameUiCategory.BigMap, category);
    }

    [Fact]
    public async Task DirectDetailsPanel_ShouldSubmitTeleportClickImmediately()
    {
        using var frame = CreateFrame((image, resolver) =>
            PlaceTemplate(image, resolver.Resolve(BetterGiAssetPaths.QuickTeleportButton), 1460, 990));
        await using var ocr = new FixedOcrEngine("unused");
        using var recognizer = CreateRecognizer(ocr);
        var clock = new FakeClock();
        var controller = EnabledController();
        var feature = new QuickTeleportFeature(controller, recognizer, clock);

        var decision = await feature.EvaluateAsync(frame, BigMapContext());

        Assert.True(decision.ShouldAct);
        Assert.Equal("click_teleport_button", decision.Reason);
        AssertClick(decision);
        Assert.Equal(QuickTeleportState.Cooldown.ToString(), controller.Status.State);
        Assert.Equal(0, ocr.CallCount);
    }

    [Fact]
    public async Task CandidatePath_ShouldWaitWithoutBlockingThenClickCandidateAndTeleport()
    {
        using var candidateFrame = CreateFrame((image, resolver) =>
            PlaceTemplate(image, resolver.Resolve(BetterGiAssetPaths.QuickTeleportWaypoint), 1280, 260));
        using var panelFrame = CreateFrame((image, resolver) =>
            PlaceTemplate(image, resolver.Resolve(BetterGiAssetPaths.QuickTeleportButton), 1460, 990));
        await using var ocr = new FixedOcrEngine("传送锚点");
        using var recognizer = CreateRecognizer(ocr);
        var clock = new FakeClock();
        var controller = EnabledController();
        var feature = new QuickTeleportFeature(controller, recognizer, clock);

        var initial = await feature.EvaluateAsync(candidateFrame, BigMapContext());
        var beforeDelay = await feature.EvaluateAsync(candidateFrame, BigMapContext());
        clock.Advance(TimeSpan.FromMilliseconds(200));
        var candidateClick = await feature.EvaluateAsync(candidateFrame, BigMapContext());
        var candidateStatus = controller.Status;
        var beforePanelDelay = await feature.EvaluateAsync(panelFrame, BigMapContext());
        clock.Advance(TimeSpan.FromMilliseconds(50));
        var teleportClick = await feature.EvaluateAsync(panelFrame, BigMapContext());

        Assert.False(initial.ShouldAct);
        Assert.Equal("candidate_wait_started", initial.Reason);
        Assert.False(beforeDelay.ShouldAct);
        Assert.Equal("candidate_click_delay", beforeDelay.Reason);
        Assert.True(candidateClick.ShouldAct);
        Assert.Equal("click_candidate", candidateClick.Reason);
        AssertClick(candidateClick);
        Assert.False(beforePanelDelay.ShouldAct);
        Assert.Equal("teleport_panel_delay", beforePanelDelay.Reason);
        Assert.True(teleportClick.ShouldAct);
        Assert.Equal("click_teleport_button", teleportClick.Reason);
        AssertClick(teleportClick);
        Assert.Equal("传送锚点", candidateStatus.LastCandidateText);
        Assert.Equal(2, ocr.CallCount);
    }

    [Fact]
    public async Task LeavingBigMap_ShouldResetPendingCandidate()
    {
        using var frame = CreateFrame((image, resolver) =>
            PlaceTemplate(image, resolver.Resolve(BetterGiAssetPaths.QuickTeleportWaypoint), 1280, 260));
        await using var ocr = new FixedOcrEngine("传送锚点");
        using var recognizer = CreateRecognizer(ocr);
        var clock = new FakeClock();
        var controller = EnabledController();
        var feature = new QuickTeleportFeature(controller, recognizer, clock);

        _ = await feature.EvaluateAsync(frame, BigMapContext());
        var outsideMap = await feature.EvaluateAsync(frame, BigMapContext() with { UiCategory = GameUiCategory.Unknown });
        clock.Advance(TimeSpan.FromSeconds(1));
        var resumed = await feature.EvaluateAsync(frame, BigMapContext());

        Assert.Equal("big_map_not_active", outsideMap.Reason);
        Assert.False(resumed.ShouldAct);
        Assert.Equal("candidate_wait_started", resumed.Reason);
    }

    private static BetterGiQuickTeleportRecognizer CreateRecognizer(IOcrEngine ocr) =>
        new(new OpenCvTemplateMatcher(), new RootedAssetPathResolver(AppContext.BaseDirectory), ocr);

    private static QuickTeleportController EnabledController()
    {
        var controller = new QuickTeleportController();
        controller.SetOptions(new QuickTeleportOptions { Enabled = true });
        return controller;
    }

    private static GameContextSnapshot BigMapContext() =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameWindowInfo(1, 1, "GenshinImpact", "Genshin Impact", new CaptureSize(1920, 1080), true))
        {
            UiCategory = GameUiCategory.BigMap,
        };

    private static CapturedFrame CreateFrame(Action<Mat, IAssetPathResolver> configure)
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var image = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.All(15));
        configure(image, resolver);
        return CapturedFrame.TakeOwnership(image, 1, DateTimeOffset.UnixEpoch, "quick-teleport-test");
    }

    private static void PlaceTemplate(Mat target, string path, int x, int y)
    {
        using var template = Cv2.ImRead(path, ImreadModes.Color);
        using var destination = new Mat(target, new Rect(x, y, template.Width, template.Height));
        template.CopyTo(destination);
    }

    private static void AssertClick(FeatureDecision decision)
    {
        var actions = Assert.IsType<AutomationIntent>(decision.Intent).Actions.Actions;
        Assert.Collection(
            actions,
            move => Assert.Equal(InputActionKind.MouseMoveClient, move.Kind),
            click => Assert.Equal(InputActionKind.MouseLeftClick, click.Kind));
    }

    private sealed class FixedOcrEngine(string text) : IOcrEngine
    {
        public int CallCount { get; private set; }

        public ValueTask<OcrResult> RecognizeAsync(
            CapturedFrame frame,
            RegionOfInterest? region = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult(new OcrResult(text, [], TimeSpan.Zero));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
