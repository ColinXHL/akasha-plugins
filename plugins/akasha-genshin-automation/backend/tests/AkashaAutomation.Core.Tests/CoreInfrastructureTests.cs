using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using OpenCvSharp;

namespace AkashaAutomation.Core.Tests;

public sealed class CoordinateTransformTests
{
    [Theory]
    [InlineData(1920, 1080, 960, 540, 480, 270, 960, 540)]
    [InlineData(2560, 1440, 960, 540, 640, 360, 1280, 720)]
    public void Scale_MapsReferenceCoordinatesTo1080pAnd1440p(
        int actualWidth,
        int actualHeight,
        int referenceX,
        int referenceY,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var transform = new CoordinateTransform(
            new CaptureSize(3840, 2160),
            new CaptureSize(actualWidth, actualHeight));

        var actual = transform.Scale(new RegionOfInterest(referenceX, referenceY, 1920, 1080));

        Assert.Equal(new RegionOfInterest(expectedX, expectedY, expectedWidth, expectedHeight), actual);
    }

    [Fact]
    public void Scale_ClampsRoundedRegionToActualFrame()
    {
        var transform = new CoordinateTransform(new CaptureSize(1920, 1080), new CaptureSize(1365, 767));

        var actual = transform.Scale(new RegionOfInterest(1900, 1060, 20, 20));

        Assert.True(actual.FitsWithin(transform.ActualSize));
        Assert.Equal(transform.ActualSize.Width, actual.Right);
        Assert.Equal(transform.ActualSize.Height, actual.Bottom);
    }
}

public sealed class CapturedFrameTests
{
    [Fact]
    public void CloneRegion_OwnsIndependentPixelsAfterParentIsDisposed()
    {
        var baseline = CapturedFrame.ActiveOwnedFrames;
        using var image = new Mat(10, 10, MatType.CV_8UC3, Scalar.Black);
        image.Set(3, 2, new Vec3b(1, 2, 3));
        var parent = CapturedFrame.TakeOwnership(image.Clone(), 7, DateTimeOffset.UnixEpoch, "test");
        var region = parent.CloneRegion(new RegionOfInterest(2, 3, 4, 4));

        parent.Dispose();

        Assert.Equal(new Vec3b(1, 2, 3), region.UseImage(mat => mat.At<Vec3b>(0, 0)));
        region.Dispose();
        Assert.Equal(baseline, CapturedFrame.ActiveOwnedFrames);
    }

    [Fact]
    public void CloneRegion_RejectsRegionOutsideFrame()
    {
        using var frame = CreateFrame(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => frame.CloneRegion(new RegionOfInterest(9, 9, 2, 2)));
    }

    private static CapturedFrame CreateFrame(int width, int height) =>
        CapturedFrame.TakeOwnership(
            new Mat(height, width, MatType.CV_8UC3, Scalar.Black),
            1,
            DateTimeOffset.UnixEpoch,
            "test");
}

public sealed class ReplayAndRecognitionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"akasha-replay-{Guid.NewGuid():N}");

    public ReplayAndRecognitionTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task ReplayCaptureSource_LoadsFramesInOrderAndThenEnds()
    {
        var paths = new[] { CreateImage("one.png", 12, 8), CreateImage("two.png", 20, 10) };
        var clock = new FakeClock();
        await using var source = new ReplayCaptureSource(paths, clock);

        using var first = await source.CaptureAsync();
        using var second = await source.CaptureAsync();
        var finished = await source.CaptureAsync();

        Assert.Equal(1, first!.Sequence);
        Assert.Equal(new CaptureSize(12, 8), first.Size);
        Assert.Equal(2, second!.Sequence);
        Assert.Equal(new CaptureSize(20, 10), second.Size);
        Assert.Null(finished);
    }

    [Fact]
    public void OpenCvTemplateMatcher_FindsTemplateInsideRoi()
    {
        using var targetMat = new Mat(30, 40, MatType.CV_8UC3, Scalar.Black);
        using var templateMat = new Mat(4, 5, MatType.CV_8UC3, Scalar.Black);
        templateMat.Set(0, 0, new Vec3b(255, 255, 255));
        templateMat.Set(1, 3, new Vec3b(40, 120, 220));
        templateMat.Set(3, 4, new Vec3b(180, 20, 80));
        using (var destination = new Mat(targetMat, new Rect(17, 11, 5, 4)))
        {
            templateMat.CopyTo(destination);
        }

        using var target = CapturedFrame.TakeOwnership(targetMat.Clone(), 1, DateTimeOffset.UnixEpoch, "target");
        using var template = CapturedFrame.TakeOwnership(templateMat.Clone(), 1, DateTimeOffset.UnixEpoch, "template");

        var result = new OpenCvTemplateMatcher().Match(
            target,
            template,
            new RegionOfInterest(10, 5, 20, 20),
            0.99);

        Assert.True(result.IsMatch);
        Assert.True(result.Confidence >= 0.99);
        Assert.Equal(new RegionOfInterest(17, 11, 5, 4), result.Region);
    }

    [Fact]
    public void RepeatedTemplateMatching_DoesNotLeakOwnedFrames()
    {
        var baseline = CapturedFrame.ActiveOwnedFrames;
        using var target = CapturedFrame.TakeOwnership(
            new Mat(50, 50, MatType.CV_8UC3, Scalar.Black),
            1,
            DateTimeOffset.UnixEpoch,
            "target");
        using var template = CapturedFrame.TakeOwnership(
            new Mat(5, 5, MatType.CV_8UC3, new Scalar(10, 20, 30)),
            1,
            DateTimeOffset.UnixEpoch,
            "template");
        var expectedDuringTest = baseline + 2;
        var matcher = new OpenCvTemplateMatcher();

        for (var iteration = 0; iteration < 200; iteration++)
        {
            _ = matcher.Match(target, template, new RegionOfInterest(0, 0, 40, 40));
        }

        Assert.Equal(expectedDuringTest, CapturedFrame.ActiveOwnedFrames);
    }

    [Fact]
    public async Task ContinuousReplay_ReleasesEveryOwnedFrame()
    {
        var path = CreateImage("continuous.png", 64, 36);
        var baseline = CapturedFrame.ActiveOwnedFrames;
        await using var source = new ReplayCaptureSource(Enumerable.Repeat(path, 250), new FakeClock());

        for (var index = 0; index < 250; index++)
        {
            using var frame = await source.CaptureAsync();
            Assert.NotNull(frame);
        }

        Assert.Null(await source.CaptureAsync());
        Assert.Equal(baseline, CapturedFrame.ActiveOwnedFrames);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string CreateImage(string name, int width, int height)
    {
        var path = Path.Combine(_directory, name);
        using var image = new Mat(height, width, MatType.CV_8UC3, new Scalar(20, 40, 60));
        Assert.True(Cv2.ImWrite(path, image));
        return path;
    }
}

public sealed class ClockAndInputTests
{
    [Fact]
    public void MouseClientPoint_ShouldScaleCapturedFrameCoordinatesToCurrentClientSize()
    {
        var action = InputAction.MouseMoveClient(1280, 720, 2560, 1440);

        var point = WindowsSendInputService.MapToClient(action, new CaptureSize(1920, 1080));

        Assert.Equal(new WindowsClientPoint(960, 540), point);
    }

    [Fact]
    public void MouseAbsolutePoint_ShouldUseTheWholeVirtualDesktopIncludingNegativeOrigin()
    {
        var leftEdge = WindowsSendInputService.DescribeVirtualDesktopMouseMove(-1920, 0, -1920, 0, 3840, 1080);
        var rightEdge = WindowsSendInputService.DescribeVirtualDesktopMouseMove(1919, 1079, -1920, 0, 3840, 1080);

        Assert.Equal(new WindowsMouseMoveDescriptor(0, 0), leftEdge);
        Assert.Equal(new WindowsMouseMoveDescriptor(65535, 65535), rightEdge);
    }

    [Fact]
    public void WindowsKeyboardInput_ShouldMatchBetterGiVirtualKeyAndScanCodeShape()
    {
        var press = WindowsSendInputService.DescribeKeyboardPress(0x46);
        var rightArrow = WindowsSendInputService.DescribeKeyboardInput(0x27, keyUp: false);

        Assert.Equal(2, press.Length);
        Assert.All(press, input =>
        {
            Assert.Equal(0x46, input.VirtualKey);
            Assert.NotEqual(0, input.ScanCode);
            Assert.False(input.IsExtended);
        });
        Assert.False(press[0].IsKeyUp);
        Assert.True(press[1].IsKeyUp);
        Assert.Equal(press[0].ScanCode, press[1].ScanCode);
        Assert.True(rightArrow.IsExtended);
        Assert.NotEqual(0, rightArrow.ScanCode);
    }

    [Fact]
    public async Task FakeClock_CompletesDelayOnlyAfterVirtualTimeAdvances()
    {
        var clock = new FakeClock();
        var delay = clock.DelayAsync(TimeSpan.FromSeconds(5)).AsTask();

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(delay.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));

        await delay;
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(5), clock.UtcNow);
    }

    [Fact]
    public async Task InputArbiter_ExecutesOnlyHighestPriorityIntentPerFrame()
    {
        await using var input = new RecordingInputService();
        var diagnostics = new InMemoryDiagnosticsSink();
        var arbiter = new InputArbiter(input, diagnostics, new FakeClock());
        var context = CreateForegroundContext();
        var low = CreateIntent("autoPick", 10, 0x46);
        var high = CreateIntent("autoDialogue", 100, 0x20);

        var result = await arbiter.SubmitAsync(1, context, [low, high]);

        Assert.True(result.Executed);
        Assert.Equal("autoDialogue", result.SelectedIntent!.FeatureId);
        var recording = Assert.Single(input.Recordings);
        Assert.Equal((ushort)0x20, Assert.Single(recording.Actions.Actions).VirtualKey);
    }

    [Fact]
    public async Task InputArbiter_RejectsEveryIntentAfterEmergencyStop()
    {
        await using var input = new RecordingInputService();
        var arbiter = new InputArbiter(input, new InMemoryDiagnosticsSink(), new FakeClock());
        await arbiter.EmergencyStopAsync();

        var result = await arbiter.SubmitAsync(
            1,
            CreateForegroundContext(),
            [CreateIntent("autoPick", 10, 0x46)]);

        Assert.False(result.Executed);
        Assert.Equal("emergency_stop", result.Reason);
        Assert.Empty(input.Recordings);
    }

    [Fact]
    public async Task InputArbiter_RejectsBackgroundGameAndDuplicateFrame()
    {
        await using var input = new RecordingInputService();
        var arbiter = new InputArbiter(input, new InMemoryDiagnosticsSink(), new FakeClock());
        var background = CreateForegroundContext() with
        {
            Window = CreateForegroundContext().Window! with { IsForeground = false },
        };

        var backgroundResult = await arbiter.SubmitAsync(1, background, [CreateIntent("x", 1, 0x46)]);
        var first = await arbiter.SubmitAsync(1, CreateForegroundContext(), [CreateIntent("x", 1, 0x46)]);
        var duplicate = await arbiter.SubmitAsync(1, CreateForegroundContext(), [CreateIntent("x", 1, 0x46)]);

        Assert.Equal("game_not_foreground", backgroundResult.Reason);
        Assert.True(first.Executed);
        Assert.Equal("frame_already_processed", duplicate.Reason);
        Assert.Single(input.Recordings);
    }

    internal static GameContextSnapshot CreateForegroundContext() =>
        new(
            DateTimeOffset.UnixEpoch,
            new GameWindowInfo(
                (nint)123,
                456,
                "GenshinImpact",
                "Genshin Impact",
                new CaptureSize(1920, 1080),
                true));

    internal static AutomationIntent CreateIntent(string featureId, int priority, ushort key) =>
        new(
            featureId,
            priority,
            new InputActionGroup(featureId, [InputAction.KeyPress(key)]),
            "test");
}

public sealed class SchedulerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"akasha-scheduler-{Guid.NewGuid():N}");

    public SchedulerTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task RunOnce_IsolatesFeatureFailureAndExecutesAtMostOneGroup()
    {
        var path = Path.Combine(_directory, "frame.png");
        using (var image = new Mat(10, 10, MatType.CV_8UC3, Scalar.Black))
        {
            Assert.True(Cv2.ImWrite(path, image));
        }

        var clock = new FakeClock();
        await using var capture = new ReplayCaptureSource([path], clock);
        await using var input = new RecordingInputService();
        var diagnostics = new InMemoryDiagnosticsSink();
        var scheduler = new SingleFrameScheduler(
            capture,
            new FixedGameContextProvider(ClockAndInputTests.CreateForegroundContext()),
            [
                new TestFeature("broken", 200, _ => throw new InvalidOperationException("boom")),
                new TestFeature("dialogue", 100, _ => FeatureDecision.Act(ClockAndInputTests.CreateIntent("dialogue", 100, 0x20))),
                new TestFeature("pick", 10, _ => FeatureDecision.Act(ClockAndInputTests.CreateIntent("pick", 10, 0x46))),
            ],
            new InputArbiter(input, diagnostics, clock),
            diagnostics,
            clock);

        var result = await scheduler.RunOnceAsync();

        Assert.True(result.Captured);
        Assert.Equal(3, result.Decisions.Count);
        Assert.True(result.Arbitration.Executed);
        Assert.Equal("dialogue", result.Arbitration.SelectedIntent!.FeatureId);
        Assert.Single(input.Recordings);
        Assert.Contains(diagnostics.Events, entry => entry.Name == "feature_failed");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class FixedGameContextProvider(GameContextSnapshot snapshot) : IGameContextProvider
    {
        public ValueTask<GameContextSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(snapshot);
    }

    private sealed class TestFeature(
        string id,
        int priority,
        Func<CapturedFrame, FeatureDecision> decision) : IAutomationFeature
    {
        public string Id => id;

        public int Priority => priority;

        public ValueTask<FeatureDecision> EvaluateAsync(
            CapturedFrame frame,
            GameContextSnapshot context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(decision(frame));
    }
}

public sealed class PaddleOcrEngineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"akasha-ocr-{Guid.NewGuid():N}");

    public PaddleOcrEngineTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task RecognizeAsync_LazilyCreatesOneSessionOffsetsRoiAndDisposesIt()
    {
        var baseline = PaddleOcrEngine.ActiveSessions;
        var options = CreateOptions();
        var factory = new FakePaddleSessionFactory();
        var engine = new PaddleOcrEngine(options, factory);
        using var frame = CapturedFrame.TakeOwnership(
            new Mat(30, 40, MatType.CV_8UC3, Scalar.Black),
            1,
            DateTimeOffset.UnixEpoch,
            "ocr");

        await engine.WarmUpAsync();
        var first = await engine.RecognizeAsync(frame, new RegionOfInterest(10, 5, 20, 10));
        var second = await engine.RecognizeAsync(frame);
        var singleLine = await engine.RecognizeSingleLineAsync(frame, new RegionOfInterest(4, 6, 12, 8));

        Assert.Equal("20x10", first.Text);
        Assert.Equal(new RegionOfInterest(11, 7, 3, 4), Assert.Single(first.Regions).Region);
        Assert.Equal("40x30", second.Text);
        Assert.Equal("single:12x8", singleLine.Text);
        Assert.Equal(new RegionOfInterest(4, 6, 12, 8), Assert.Single(singleLine.Regions).Region);
        Assert.Equal(2, factory.Session!.SingleLineCount);
        Assert.Equal(3, factory.Session.RecognizeCount);
        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(baseline + 1, PaddleOcrEngine.ActiveSessions);

        await engine.DisposeAsync();

        Assert.Equal(baseline, PaddleOcrEngine.ActiveSessions);
        Assert.True(factory.Session!.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await engine.RecognizeAsync(frame));
    }

    [Fact]
    public async Task RecognizeAsync_RejectsMissingModelBeforeCreatingSession()
    {
        var factory = new FakePaddleSessionFactory();
        await using var engine = new PaddleOcrEngine(
            new PaddleOcrModelOptions("missing-det", "missing-rec", "missing-yml"),
            factory);
        using var frame = CapturedFrame.TakeOwnership(
            new Mat(3, 3, MatType.CV_8UC3, Scalar.Black),
            1,
            DateTimeOffset.UnixEpoch,
            "ocr");

        await Assert.ThrowsAsync<FileNotFoundException>(async () => await engine.RecognizeAsync(frame));
        Assert.Equal(0, factory.CreateCount);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private PaddleOcrModelOptions CreateOptions()
    {
        var detection = Path.Combine(_directory, "det.onnx");
        var recognition = Path.Combine(_directory, "rec.onnx");
        var config = Path.Combine(_directory, "inference.yml");
        File.WriteAllText(detection, "test");
        File.WriteAllText(recognition, "test");
        File.WriteAllText(config, "test");
        return new PaddleOcrModelOptions(detection, recognition, config);
    }

    private sealed class FakePaddleSessionFactory : IPaddleOcrSessionFactory
    {
        public int CreateCount { get; private set; }

        public FakePaddleSession? Session { get; private set; }

        public IPaddleOcrSession Create(PaddleOcrModelOptions options)
        {
            CreateCount++;
            return Session = new FakePaddleSession();
        }
    }

    private sealed class FakePaddleSession : IPaddleOcrSession
    {
        public bool Disposed { get; private set; }

        public int SingleLineCount { get; private set; }

        public int RecognizeCount { get; private set; }

        public OcrResult Recognize(Mat image, CancellationToken cancellationToken = default)
        {
            RecognizeCount++;
            return new(
                $"{image.Width}x{image.Height}",
                [new OcrTextRegion("text", 0.9, new RegionOfInterest(1, 2, 3, 4))],
                TimeSpan.FromMilliseconds(1));
        }

        public OcrResult RecognizeSingleLine(Mat image, CancellationToken cancellationToken = default)
        {
            SingleLineCount++;
            return new OcrResult(
                $"single:{image.Width}x{image.Height}",
                [new OcrTextRegion("single", 0.95, new RegionOfInterest(0, 0, image.Width, image.Height))],
                TimeSpan.FromMilliseconds(1));
        }

        public void Dispose() => Disposed = true;
    }
}

public sealed class AssetResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"akasha-assets-{Guid.NewGuid():N}");

    public AssetResolverTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Resolve_AllowsContainedRelativePathAndRejectsEscape()
    {
        var resolver = new RootedAssetPathResolver(_root);

        Assert.Equal(Path.Combine(_root, "Config", "x.json"), resolver.Resolve("Config/x.json"));
        Assert.Throws<ArgumentException>(() => resolver.Resolve("../outside.json"));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Path.Combine(_root, "absolute.json")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
