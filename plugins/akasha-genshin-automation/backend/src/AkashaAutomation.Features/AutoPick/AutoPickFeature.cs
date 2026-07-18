using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Upstream.AutoPick;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Scheduling;

namespace AkashaAutomation.Features.AutoPick;

public sealed class AutoPickFeature : IAutomationFeature
{
    public const string FeatureId = "autoPick";
    private readonly IAutoPickController _controller;
    private readonly BetterGiAutoPickRecognizer _recognizer;
    private readonly IOcrEngine _ocrEngine;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly IClock _clock;

    public AutoPickFeature(
        IAutoPickController controller,
        BetterGiAutoPickRecognizer recognizer,
        IOcrEngine ocrEngine,
        IDiagnosticsSink diagnostics,
        IClock clock)
    {
        _controller = controller;
        _recognizer = recognizer;
        _ocrEngine = ocrEngine;
        _diagnostics = diagnostics;
        _clock = clock;
    }

    public string Id => FeatureId;

    public int Priority => 30;

    public async ValueTask<FeatureDecision> EvaluateAsync(
        CapturedFrame frame,
        GameContextSnapshot context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);
        var configuration = _controller.Snapshot;
        var options = configuration.Options;
        if (!options.Enabled)
        {
            return NoAction(frame, null, "disabled");
        }

        if (context.IsTalk)
        {
            return NoAction(frame, null, "dialogue_active");
        }

        var interaction = _recognizer.FindInteraction(frame, options.PickKey);
        if (!interaction.IsMatch || interaction.Region is null)
        {
            return NoAction(frame, null, "interaction_key_not_found");
        }

        if (_recognizer.HasSpecialL(frame))
        {
            return NoAction(frame, null, "special_l_key");
        }

        var excludeIcon = _recognizer.FindExcludeIcon(
            frame,
            interaction.Region.Value,
            options.ItemIconLeftOffset,
            options.ItemTextLeftOffset);
        if (!options.WhiteListEnabled && excludeIcon.IsExcludeIcon)
        {
            return NoAction(frame, null, excludeIcon.Kind ?? "exclude_icon");
        }

        if (!options.WhiteListEnabled && !options.BlackListEnabled)
        {
            return Pick(frame, options, null, "lists_disabled");
        }

        RegionOfInterest textRegion;
        try
        {
            textRegion = BetterGiAutoPickRecognizer.TextRegion(
                frame.Size,
                interaction.Region.Value,
                options.ItemTextLeftOffset,
                options.ItemTextRightOffset);
        }
        catch (ArgumentOutOfRangeException)
        {
            return NoAction(frame, null, "text_roi_out_of_range");
        }

        var textAnalysis = BetterGiTextRectExtractor.Analyze(frame, textRegion);
        if (textAnalysis.IsPickAnimationInProgress)
        {
            return NoAction(frame, null, "pick_animation_in_progress");
        }

        var ocr = textAnalysis.UseDetector
            ? await _ocrEngine.RecognizeAsync(frame, textRegion, cancellationToken).ConfigureAwait(false)
            : await _ocrEngine.RecognizeSingleLineAsync(frame, textAnalysis.Region, cancellationToken).ConfigureAwait(false);
        var rule = BetterGiAutoPickRules.Decide(
            ocr.Text,
            excludeIcon.IsExcludeIcon,
            options.BlackListEnabled,
            options.WhiteListEnabled,
            configuration.Lists);
        return rule.ShouldPick
            ? Pick(frame, options, rule.Text, rule.Reason)
            : NoAction(frame, rule.Text, rule.Reason);
    }

    private FeatureDecision Pick(CapturedFrame frame, AutoPickOptions options, string? text, string reason)
    {
        Report(frame, text, reason, intentSubmitted: true);
        return FeatureDecision.Act(
            new AutomationIntent(
                FeatureId,
                Priority,
                new InputActionGroup(
                    "auto_pick",
                    [InputAction.KeyPress(BetterGiAutoPickRecognizer.ToVirtualKey(options.PickKey))]),
                reason));
    }

    private FeatureDecision NoAction(CapturedFrame frame, string? text, string reason)
    {
        Report(frame, text, reason, intentSubmitted: false);
        return FeatureDecision.NoAction(FeatureId, reason);
    }

    private void Report(CapturedFrame frame, string? text, string reason, bool intentSubmitted)
    {
        var timestamp = _clock.UtcNow;
        _controller.Report(frame.Sequence, text, reason, intentSubmitted, timestamp);
        _diagnostics.Write(
            new DiagnosticEvent(
                timestamp,
                "auto_pick",
                "evaluated",
                new Dictionary<string, object?>
                {
                    ["frameSequence"] = frame.Sequence,
                    ["text"] = text,
                    ["reason"] = reason,
                    ["intentSubmitted"] = intentSubmitted,
                }));
    }
}
