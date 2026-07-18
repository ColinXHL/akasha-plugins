using System.Diagnostics;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Compatibility.Ocr;
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

namespace AkashaAutomation.LiveTestHost;

internal sealed class LiveAutomationHost(LiveTestHostOptions options)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var clock = new SystemClock();
        var diagnostics = new ConsoleDiagnosticsSink();
        var resolver = new RootedAssetPathResolver(AppContext.BaseDirectory);
        var windowLocator = new WindowsGameWindowLocator();
        var contextProvider = new GameContextProvider(windowLocator, clock);
        await using var capture = new WindowsBitBltCaptureSource(windowLocator, clock);
        await using var ocr = new PaddleOcrEngine(
            BetterGiPaddleOcrAssets.CreateV4Options(resolver),
            new PaddleOnnxOcrSessionFactory());
        using var pickRecognizer = new BetterGiAutoPickRecognizer(new OpenCvTemplateMatcher(), resolver);
        using var dialogueRecognizer = new BetterGiAutoDialogueRecognizer(
            new OpenCvTemplateMatcher(),
            resolver,
            ocr);
        await using var input = new LiveInputService(new WindowsSendInputService());
        var arbiter = new InputArbiter(input, diagnostics, clock);

        var pickController = new AutoPickController(resolver);
        pickController.SetOptions(
            new AutoPickOptions
            {
                Enabled = options.AutoPickEnabled,
                PickKey = "F",
                BlackListEnabled = true,
            });
        var pickFeature = new AutoPickFeature(pickController, pickRecognizer, ocr, diagnostics, clock);

        var dialogueController = new AutoDialogueController(resolver);
        dialogueController.SetOptions(
            new AutoDialogueOptions
            {
                Enabled = options.AutoDialogueEnabled,
                InteractionKey = "F",
                OptionStrategy = "First",
                CustomPriorityOptionsEnabled = false,
                CustomPriorityOptions = [],
                AdvanceKey = "Space",
                AutoWaitDialogueVoiceEnabled = false,
                AutoHangoutEnabled = false,
                HangoutEnding = string.Empty,
            });
        await using IDialogueOptionVoiceWaiter voiceWaiter = new FixedDelayDialogueOptionVoiceWaiter(clock);
        var reward = new RewardDialogueSceneHandler(dialogueRecognizer);
        var hangout = new HangoutDialogueSceneHandler(dialogueRecognizer);
        IAutoDialogueSceneHandler[] handlers =
        [
            reward,
            hangout,
            new PopupDialogueSceneHandler(dialogueRecognizer),
            new SubmitGoodsDialogueSceneHandler(dialogueRecognizer),
            new BlackScreenDialogueSceneHandler(dialogueRecognizer),
        ];
        var dialogueFeature = new AutoDialogueFeature(
            dialogueController,
            dialogueRecognizer,
            voiceWaiter,
            reward,
            hangout,
            handlers,
            clock);
        var scheduler = new SingleFrameScheduler(
            capture,
            contextProvider,
            [dialogueFeature, pickFeature],
            arbiter,
            diagnostics,
            clock,
            dialogueRecognizer);

        Console.WriteLine(
            $"已启动：自动拾取 {State(options.AutoPickEnabled)}，自动剧情 {State(options.AutoDialogueEnabled)}，" +
            $"触发周期 {options.IntervalMilliseconds} ms。");
        string? previousSignature = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var result = await scheduler.RunOnceAsync(cancellationToken).ConfigureAwait(false);
                    previousSignature = Print(result, pickController.Status, dialogueController.Status, previousSignature);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await arbiter.EmergencyStopAsync(CancellationToken.None).ConfigureAwait(false);
            pickController.SetEnabled(false);
            dialogueController.SetEnabled(false);
            Console.WriteLine($"已停止，实际执行 {input.ExecutedGroups} 组动作。");
        }
    }

    private string Print(
        SingleFrameScheduleResult result,
        AutoPickRuntimeStatus pickStatus,
        AutoDialogueRuntimeStatus dialogueStatus,
        string? previousSignature)
    {
        var selectedFeature = result.Arbitration.SelectedIntent?.FeatureId;
        var dialogueDecision = result.Decisions.FirstOrDefault(decision => decision.FeatureId == AutoDialogueFeature.FeatureId);
        var pickDecision = result.Decisions.FirstOrDefault(decision => decision.FeatureId == AutoPickFeature.FeatureId);
        var useDialogue = options.AutoDialogueEnabled &&
                          (dialogueStatus.LastFrameSequence == result.FrameSequence &&
                           !string.Equals(dialogueDecision?.Reason, "dialogue_not_active", StringComparison.Ordinal));
        var decision = selectedFeature == AutoDialogueFeature.FeatureId || useDialogue ? dialogueDecision : pickDecision;
        var pickText = pickStatus.LastFrameSequence == result.FrameSequence ? pickStatus.LastRecognizedText : null;
        var dialogueOptions = dialogueStatus.LastFrameSequence == result.FrameSequence
            ? string.Join(" | ", dialogueStatus.LastRecognizedOptions)
            : string.Empty;
        var mouseTarget = result.Arbitration.SelectedIntent?.Actions.Actions
            .FirstOrDefault(action => action.Kind == InputActionKind.MouseMoveClient);
        var targetText = mouseTarget is null
            ? string.Empty
            : $" target=({mouseTarget.X},{mouseTarget.Y})@{mouseTarget.ReferenceWidth}x{mouseTarget.ReferenceHeight}";
        var reason = decision?.Reason ?? result.Arbitration.Reason;
        var signature = $"{dialogueStatus.UiCategory}|{pickText}|{dialogueOptions}|{reason}|{result.Arbitration.Reason}|{targetText}";
        if (options.ShowAllFrames || !string.Equals(signature, previousSignature, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] frame={result.FrameSequence?.ToString() ?? "<none>"} " +
                $"ui={dialogueStatus.UiCategory} pick={Quote(pickText)} options=\"{Escape(dialogueOptions)}\" " +
                $"reason={reason} arbiter={result.Arbitration.Reason}{targetText}");
        }

        return signature;
    }

    private static string State(bool enabled) => enabled ? "开" : "关";

    private static string Quote(string? text) => text is null ? "<none>" : $"\"{Escape(text)}\"";

    private static string Escape(string text) => text.Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed class ConsoleDiagnosticsSink : IDiagnosticsSink
    {
        public void Write(DiagnosticEvent diagnosticEvent)
        {
            ArgumentNullException.ThrowIfNull(diagnosticEvent);
            if (diagnosticEvent.Category == "input" && diagnosticEvent.Name == "executed")
            {
                Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] LIVE INPUT EXECUTED");
            }
        }
    }
}
