using AkashaAutomation.BetterGiPort.Assets;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Upstream.AutoPick;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.AutoPick;
using OpenCvSharp;

namespace AkashaAutomation.Features.Tests;

public sealed class AutoPickFeatureTests
{
    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    public async Task Replay_PositiveFrame_ShouldSubmitPickIntent(int width, int height)
    {
        await using var scenario = await ReplayScenario.CreateAsync(width, height, "甜甜花", excludeIcon: null);

        var result = await scenario.RunOnceAsync();

        Assert.True(result.Captured);
        var decision = Assert.Single(result.Decisions);
        Assert.True(decision.ShouldAct);
        Assert.Equal("pick", decision.Reason);
        var recording = Assert.Single(scenario.Input.Recordings);
        var action = Assert.Single(recording.Actions.Actions);
        Assert.Equal(InputActionKind.KeyPress, action.Kind);
        Assert.Equal((ushort)'F', action.VirtualKey);
    }

    [Theory]
    [InlineData(1920, 1080, "chat")]
    [InlineData(2560, 1440, "settings")]
    public async Task Replay_ExcludeIconFrame_ShouldNotSubmitIntent(int width, int height, string icon)
    {
        await using var scenario = await ReplayScenario.CreateAsync(width, height, "与某人对话", icon);

        var result = await scenario.RunOnceAsync();

        var decision = Assert.Single(result.Decisions);
        Assert.False(decision.ShouldAct);
        Assert.Equal(icon == "chat" ? "chat_icon" : "settings_icon", decision.Reason);
        Assert.Empty(scenario.Input.Recordings);
        Assert.Equal(0, scenario.Ocr.CallCount);
    }

    [Fact]
    public async Task WhitelistExact_ShouldOverrideExcludeIcon()
    {
        await using var scenario = await ReplayScenario.CreateAsync(
            1920,
            1080,
            "与凯瑟琳对话",
            "chat",
            options => options with
            {
                WhiteListEnabled = true,
                UserWhitelist = ["与凯瑟琳对话"],
            });

        var result = await scenario.RunOnceAsync();

        Assert.Equal("whitelist_exact", Assert.Single(result.Decisions).Reason);
        Assert.Single(scenario.Input.Recordings);
        Assert.Equal(1, scenario.Ocr.CallCount);
    }

    [Fact]
    public async Task ListsDisabled_ShouldPickWithoutCallingOcr()
    {
        await using var scenario = await ReplayScenario.CreateAsync(
            1920,
            1080,
            "unused",
            excludeIcon: null,
            options => options with { BlackListEnabled = false, WhiteListEnabled = false });

        var result = await scenario.RunOnceAsync();

        Assert.Equal("lists_disabled", Assert.Single(result.Decisions).Reason);
        Assert.Single(scenario.Input.Recordings);
        Assert.Equal(0, scenario.Ocr.CallCount);
    }

    [Fact]
    public async Task CustomInteractionTemplate_ShouldUseMatchingVirtualKey()
    {
        await using var scenario = await ReplayScenario.CreateAsync(
            1920,
            1080,
            "甜甜花",
            excludeIcon: null,
            options => options with { PickKey = "E" },
            pickKey: "E");

        _ = await scenario.RunOnceAsync();

        Assert.Equal((ushort)'E', Assert.Single(Assert.Single(scenario.Input.Recordings).Actions.Actions).VirtualKey);
    }

    [Fact]
    public async Task DisabledFeature_ShouldNotSubmitIntent()
    {
        await using var scenario = await ReplayScenario.CreateAsync(
            1920,
            1080,
            "甜甜花",
            excludeIcon: null,
            options => options with { Enabled = false });

        var result = await scenario.RunOnceAsync();

        Assert.Equal("disabled", Assert.Single(result.Decisions).Reason);
        Assert.Empty(scenario.Input.Recordings);
    }

    [Fact]
    public async Task EmergencyStop_ShouldRejectFeatureIntentBeforeInput()
    {
        await using var scenario = await ReplayScenario.CreateAsync(1920, 1080, "甜甜花", excludeIcon: null);
        await scenario.Arbiter.EmergencyStopAsync();

        var result = await scenario.RunOnceAsync();

        Assert.True(Assert.Single(result.Decisions).ShouldAct);
        Assert.Equal("emergency_stop", result.Arbitration.Reason);
        Assert.Empty(scenario.Input.Recordings);
    }

    [Theory]
    [InlineData("", false, false, "ocr_empty")]
    [InlineData("X", false, false, "ocr_empty")]
    [InlineData("花", false, false, "ocr_too_short")]
    [InlineData("长时间按住", false, false, "hardcoded_exclusion")]
    [InlineData("月谕圣牌", false, false, "hardcoded_exclusion")]
    [InlineData("烹饪", false, false, "blacklist_exact")]
    [InlineData("自定义黑名单", false, false, "blacklist_exact")]
    [InlineData("这是模糊测试条目", false, false, "blacklist_fuzzy")]
    [InlineData("普通机关", true, false, "exclude_icon")]
    [InlineData("普通拾取物", false, true, "pick")]
    public void Rules_ShouldPreserveBetterGiPriority(
        string text,
        bool excludeIcon,
        bool shouldPick,
        string reason)
    {
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var lists = BetterGiAutoPickRules.LoadLists(
            resolver,
            ["自定义黑名单"],
            ["模糊测试"],
            ["普通机关"]);

        var decision = BetterGiAutoPickRules.Decide(text, excludeIcon, true, false, lists);

        Assert.Equal(shouldPick, decision.ShouldPick);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void Rules_HardcodedExclusion_ShouldWinOverWhitelist()
    {
        var lists = new BetterGiAutoPickLists(
            new HashSet<string>(StringComparer.Ordinal),
            [],
            new HashSet<string>(["长时间按住"], StringComparer.Ordinal));

        var decision = BetterGiAutoPickRules.Decide("长时间按住", false, true, true, lists);

        Assert.False(decision.ShouldPick);
        Assert.Equal("hardcoded_exclusion", decision.Reason);
    }

    [Fact]
    public void OcrCleanup_ShouldNormalizeWhitespaceBracketsAndNoise()
    {
        Assert.Equal("「珍贵宝箱」", BetterGiAutoPickRules.ProcessOcrText("  123[珍贵 宝箱]abc\r\n"));
        Assert.Equal("「宝箱」", BetterGiAutoPickRules.ProcessOcrText("abc宝箱]"));
    }

    [Fact]
    public void TextExtraction_BlankRegion_ShouldRequestDetectorFallback()
    {
        using var frame = CapturedFrame.TakeOwnership(
            new Mat(40, 300, MatType.CV_8UC3, Scalar.All(15)),
            1,
            DateTimeOffset.UnixEpoch,
            "blank-pick-text");

        var result = BetterGiTextRectExtractor.Extract(frame, new RegionOfInterest(0, 0, 300, 40));

        Assert.True(result.UseDetector);
        Assert.Equal(new RegionOfInterest(0, 0, 300, 40), result.Region);
    }

    [Fact]
    public void PickAnimation_NegativeTopRowGradient_ShouldBeSuppressed()
    {
        using var image = new Mat(40, 300, MatType.CV_8UC3, Scalar.All(15));
        for (var x = 0; x < image.Width; x++)
        {
            var value = (byte)Math.Clamp(255 - x * 4, 0, 255);
            Cv2.Line(image, new Point(x, 0), new Point(x, 2), new Scalar(value, value, value));
        }

        using var frame = CapturedFrame.TakeOwnership(image.Clone(), 1, DateTimeOffset.UnixEpoch, "pick-animation");

        Assert.True(BetterGiTextRectExtractor.IsPickAnimationInProgress(
            frame,
            new RegionOfInterest(0, 0, 300, 40)));
    }

    private sealed class ReplayScenario : IAsyncDisposable
    {
        private readonly string _directory;
        private readonly ReplayCaptureSource _capture;
        private readonly BetterGiAutoPickRecognizer _recognizer;
        private readonly SingleFrameScheduler _scheduler;

        private ReplayScenario(
            string directory,
            ReplayCaptureSource capture,
            BetterGiAutoPickRecognizer recognizer,
            FakeOcrEngine ocr,
            RecordingInputService input,
            InputArbiter arbiter,
            SingleFrameScheduler scheduler)
        {
            _directory = directory;
            _capture = capture;
            _recognizer = recognizer;
            Ocr = ocr;
            Input = input;
            Arbiter = arbiter;
            _scheduler = scheduler;
        }

        public FakeOcrEngine Ocr { get; }

        public RecordingInputService Input { get; }

        public InputArbiter Arbiter { get; }

        public static Task<ReplayScenario> CreateAsync(
            int width,
            int height,
            string ocrText,
            string? excludeIcon,
            Func<AutoPickOptions, AutoPickOptions>? configure = null,
            string pickKey = "F")
        {
            var directory = Path.Combine(Path.GetTempPath(), $"akasha-autopick-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var framePath = Path.Combine(directory, $"{width}x{height}.png");
            CreateReplayFrame(framePath, width, height, pickKey, excludeIcon);

            var clock = new FakeClock(DateTimeOffset.UnixEpoch);
            var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
            var controller = new AutoPickController(resolver);
            var options = configure?.Invoke(new AutoPickOptions { Enabled = true })
                          ?? new AutoPickOptions { Enabled = true };
            controller.SetOptions(options);
            var recognizer = new BetterGiAutoPickRecognizer(new OpenCvTemplateMatcher(), resolver);
            var ocr = new FakeOcrEngine(ocrText);
            var diagnostics = new InMemoryDiagnosticsSink();
            var feature = new AutoPickFeature(controller, recognizer, ocr, diagnostics, clock);
            var input = new RecordingInputService();
            var arbiter = new InputArbiter(input, diagnostics, clock);
            var capture = new ReplayCaptureSource([framePath], clock);
            var context = new StaticGameContextProvider(
                new GameContextSnapshot(
                    clock.UtcNow,
                    new GameWindowInfo(1, 1, "GenshinImpact", "Genshin Impact", new CaptureSize(width, height), true)));
            var scheduler = new SingleFrameScheduler(capture, context, [feature], arbiter, diagnostics, clock);
            return Task.FromResult(new ReplayScenario(directory, capture, recognizer, ocr, input, arbiter, scheduler));
        }

        public ValueTask<SingleFrameScheduleResult> RunOnceAsync() => _scheduler.RunOnceAsync();

        public async ValueTask DisposeAsync()
        {
            await _capture.DisposeAsync();
            await Ocr.DisposeAsync();
            await Input.DisposeAsync();
            _recognizer.Dispose();
            Directory.Delete(_directory, recursive: true);
        }

        private static void CreateReplayFrame(
            string path,
            int width,
            int height,
            string pickKey,
            string? excludeIcon)
        {
            var scale = (double)height / 1080;
            using var frame = new Mat(height, width, MatType.CV_8UC3, Scalar.All(15));
            var keyPath = pickKey switch
            {
                "E" => BetterGiAssetPaths.AutoPickKeyE,
                "F" => BetterGiAssetPaths.AutoPickKeyF,
                "G" => BetterGiAssetPaths.AutoPickKeyG,
                _ => throw new ArgumentOutOfRangeException(nameof(pickKey)),
            };
            var keyX = (int)(1100 * scale);
            var keyY = (int)(400 * scale);
            PlaceTemplate(frame, Path.Combine(AppContext.BaseDirectory, keyPath), scale, keyX, keyY);

            if (excludeIcon is not null)
            {
                var iconPath = excludeIcon switch
                {
                    "chat" => BetterGiAssetPaths.AutoPickChatIcon,
                    "settings" => BetterGiAssetPaths.AutoPickSettingsIcon,
                    _ => throw new ArgumentOutOfRangeException(nameof(excludeIcon)),
                };
                PlaceTemplate(
                    frame,
                    Path.Combine(AppContext.BaseDirectory, iconPath),
                    scale,
                    keyX + (int)(60 * scale),
                    keyY);
            }

            Assert.True(Cv2.ImWrite(path, frame));
        }

        private static void PlaceTemplate(Mat target, string path, double scale, int x, int y)
        {
            using var original = Cv2.ImRead(path, ImreadModes.Color);
            using var resized = new Mat();
            Cv2.Resize(
                original,
                resized,
                new Size(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale))),
                interpolation: scale > 1 ? InterpolationFlags.Linear : InterpolationFlags.Area);
            using var destination = new Mat(target, new Rect(x, y, resized.Width, resized.Height));
            resized.CopyTo(destination);
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
            return ValueTask.FromResult(new OcrResult(text, [], TimeSpan.Zero));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }


    private sealed class StaticGameContextProvider(GameContextSnapshot snapshot) : IGameContextProvider
    {
        public ValueTask<GameContextSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(snapshot);
    }
}
