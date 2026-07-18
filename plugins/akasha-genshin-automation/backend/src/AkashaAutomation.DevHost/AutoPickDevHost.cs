using System.Diagnostics;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.Ocr;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.AutoPick;

namespace AkashaAutomation.DevHost;

public sealed class AutoPickDevHost(DevHostOptions options)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = new SystemClock();
        var diagnostics = new NullDiagnosticsSink();
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var windowLocator = new WindowsGameWindowLocator();
        var contextProvider = new GameContextProvider(windowLocator, clock);
        await using var capture = new WindowsBitBltCaptureSource(windowLocator, clock);
        using var recognizer = new BetterGiAutoPickRecognizer(new OpenCvTemplateMatcher(), resolver);
        await using var ocr = new PaddleOcrEngine(
            BetterGiPaddleOcrAssets.CreateV4Options(resolver),
            new PaddleOnnxOcrSessionFactory());
        await using var input = new ObserveOnlyInputService();
        var arbiter = new InputArbiter(input, diagnostics, clock);
        var controller = new AutoPickController(resolver);
        controller.SetOptions(
            new AutoPickOptions
            {
                Enabled = true,
                PickKey = options.PickKey,
                BlackListEnabled = options.BlacklistEnabled,
                WhiteListEnabled = options.UserWhitelist.Count > 0,
                UserExactBlacklist = options.UserExactBlacklist,
                UserFuzzyBlacklist = options.UserFuzzyBlacklist,
                UserWhitelist = options.UserWhitelist,
            });
        var feature = new AutoPickFeature(controller, recognizer, ocr, diagnostics, clock);
        var scheduler = new SingleFrameScheduler(
            capture,
            contextProvider,
            [feature],
            arbiter,
            diagnostics,
            clock);

        Console.WriteLine("Akasha Automation AutoPick DevHost");
        Console.WriteLine("模式: OBSERVE-ONLY（不会发送任何键盘或鼠标输入）");
        Console.WriteLine($"拾取键: {options.PickKey}  帧间隔: {options.IntervalMilliseconds} ms");
        Console.WriteLine("正在等待原神窗口；按 Ctrl+C 安全停止。\n");

        string? previousSignature = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var result = await scheduler.RunOnceAsync(cancellationToken).ConfigureAwait(false);
                    var signature = PrintResult(result, controller.Status, options.ShowAllFrames, previousSignature);
                    previousSignature = signature ?? previousSignature;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    var signature = $"runtime_error:{exception.GetType().Name}:{exception.Message}";
                    if (options.ShowAllFrames || !string.Equals(signature, previousSignature, StringComparison.Ordinal))
                    {
                        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] runtime_error {exception.Message}");
                        previousSignature = signature;
                    }
                }

                var remaining = TimeSpan.FromMilliseconds(options.IntervalMilliseconds) - Stopwatch.GetElapsedTime(started);
                if (remaining > TimeSpan.Zero)
                {
                    await clock.DelayAsync(remaining, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            await arbiter.EmergencyStopAsync(CancellationToken.None).ConfigureAwait(false);
            controller.SetEnabled(false);
            Console.WriteLine($"\n已停止。观察到 {input.ObservedGroups} 组本应执行的输入动作，实际发送 0 组。");
        }
    }

    private static string PrintResult(
        SingleFrameScheduleResult result,
        AutoPickRuntimeStatus status,
        bool showAll,
        string? previousSignature)
    {
        if (!result.Captured)
        {
            var unavailable = $"waiting:{result.Arbitration.Reason}";
            if (showAll || !string.Equals(unavailable, previousSignature, StringComparison.Ordinal))
            {
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] waiting reason={result.Arbitration.Reason}");
            }

            return unavailable;
        }

        var decision = result.Decisions.FirstOrDefault();
        var reason = decision?.Reason ?? "no_decision";
        var text = status.LastFrameSequence == result.FrameSequence ? status.LastRecognizedText : null;
        var wouldAct = decision?.ShouldAct == true;
        var signature = $"{text}|{reason}|{wouldAct}|{result.Arbitration.Reason}";
        if (showAll || !string.Equals(signature, previousSignature, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] frame={result.FrameSequence} " +
                $"text={Quote(text)} reason={reason} wouldPress={wouldAct.ToString().ToLowerInvariant()} " +
                $"arbiter={result.Arbitration.Reason}");
        }

        return signature;
    }

    private static string Quote(string? text) =>
        text is null ? "<none>" : $"\"{text.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
