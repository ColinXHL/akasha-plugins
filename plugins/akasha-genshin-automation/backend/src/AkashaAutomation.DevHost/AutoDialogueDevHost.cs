using System.Diagnostics;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Compatibility.Ocr;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.AutoDialogue;

namespace AkashaAutomation.DevHost;

public sealed class AutoDialogueDevHost(DevHostOptions options)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = new SystemClock();
        var diagnostics = new NullDiagnosticsSink();
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var windowLocator = new WindowsGameWindowLocator();
        var contextProvider = new GameContextProvider(windowLocator, clock);
        await using var capture = new WindowsBitBltCaptureSource(windowLocator, clock);
        await using var ocr = new PaddleOcrEngine(
            BetterGiPaddleOcrAssets.CreateV4Options(resolver),
            new PaddleOnnxOcrSessionFactory());
        using var recognizer = new BetterGiAutoDialogueRecognizer(new OpenCvTemplateMatcher(), resolver, ocr);
        await using var input = new ObserveOnlyInputService();
        await using IDialogueOptionVoiceWaiter voiceWaiter = options.VoiceWaitEnabled
            ? new SileroDialogueOptionVoiceWaiter(clock, resolver)
            : new FixedDelayDialogueOptionVoiceWaiter(clock);
        var arbiter = new InputArbiter(input, diagnostics, clock);
        var controller = new AutoDialogueController(resolver);
        controller.SetOptions(
            new AutoDialogueOptions
            {
                Enabled = true,
                InteractionKey = options.PickKey,
                OptionStrategy = options.OptionStrategy,
                CustomPriorityOptionsEnabled = options.CustomPriorityOptions.Count > 0,
                CustomPriorityOptions = options.CustomPriorityOptions,
                AdvanceKey = options.AdvanceKey,
                AutoWaitDialogueVoiceEnabled = options.VoiceWaitEnabled,
                AutoHangoutEnabled = options.HangoutEnding.Length > 0,
                HangoutEnding = options.HangoutEnding,
            });
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
        var feature = new AutoDialogueFeature(
            controller,
            recognizer,
            voiceWaiter,
            reward,
            hangout,
            handlers,
            clock);
        var scheduler = new SingleFrameScheduler(
            capture,
            contextProvider,
            [feature],
            arbiter,
            diagnostics,
            clock,
            recognizer);

        Console.WriteLine("Akasha Automation AutoDialogue DevHost");
        Console.WriteLine("模式: OBSERVE-ONLY（不会发送任何键盘或鼠标输入）");
        Console.WriteLine($"选项策略: {options.OptionStrategy}  推进键: {options.AdvanceKey}  VAD: {(options.VoiceWaitEnabled ? "on" : "off")}");
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
                    var status = controller.Status;
                    var decision = result.Decisions.FirstOrDefault();
                    var optionsText = status.LastFrameSequence == result.FrameSequence
                        ? string.Join(" | ", status.LastRecognizedOptions)
                        : string.Empty;
                    var reason = decision?.Reason ?? result.Arbitration.Reason;
                    var wouldAct = decision?.ShouldAct == true;
                    var signature = $"{status.UiCategory}|{optionsText}|{reason}|{wouldAct}|{status.VoiceWaitActive}|{result.Arbitration.Reason}";
                    if (options.ShowAllFrames || !string.Equals(signature, previousSignature, StringComparison.Ordinal))
                    {
                        Console.WriteLine(
                            $"[{DateTimeOffset.Now:HH:mm:ss.fff}] frame={result.FrameSequence?.ToString() ?? "<none>"} " +
                            $"ui={status.UiCategory} options=\"{optionsText.Replace("\"", "\\\"", StringComparison.Ordinal)}\" " +
                            $"reason={reason} wouldAct={wouldAct.ToString().ToLowerInvariant()} " +
                            $"voiceWait={status.VoiceWaitActive.ToString().ToLowerInvariant()} " +
                            $"fallback={status.VoiceWaitFallback.ToString().ToLowerInvariant()} arbiter={result.Arbitration.Reason}");
                        previousSignature = signature;
                    }
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
}
