using System.Text.Json;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class AutoDialogueWorkerIntegrationTests
{
    [Fact]
    public async Task Commands_ShouldRoundTripOptionsAndEnabledState()
    {
        var controller = CreateController();
        var emergency = new EmergencyStopController();
        var handler = new WorkerCommandHandler(
            new WorkerStatusProvider(new WorkerStateMachine(), emergency, autoDialogueController: controller),
            emergency,
            autoDialogueController: controller);
        var options = new AutoDialogueOptions
        {
            Enabled = true,
            AdvanceKey = "interaction",
            InteractionKey = "e",
            OptionStrategy = "last",
            CustomPriorityOptionsEnabled = true,
            CustomPriorityOptions = ["特殊选项"],
            AutoWaitDialogueVoiceEnabled = true,
        };

        var set = await handler.HandleAsync(
            Command("set", "features.autoDialogue.setOptions", JsonSerializer.SerializeToElement(options, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var disabled = await handler.HandleAsync(
            Command("disable", "features.autoDialogue.setEnabled", JsonSerializer.SerializeToElement(new { enabled = false }, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var get = await handler.HandleAsync(
            Command("get", "features.autoDialogue.getOptions"),
            CancellationToken.None);

        Assert.Null(set.Error);
        Assert.Equal("Interaction", set.Payload!.Value.GetProperty("advanceKey").GetString());
        Assert.Equal("E", set.Payload.Value.GetProperty("interactionKey").GetString());
        Assert.Equal("Last", set.Payload.Value.GetProperty("optionStrategy").GetString());
        Assert.False(disabled.Payload!.Value.GetProperty("enabled").GetBoolean());
        Assert.Equal("特殊选项", get.Payload!.Value.GetProperty("customPriorityOptions")[0].GetString());
    }

    [Fact]
    public async Task InvalidOptions_ShouldBeRejectedWithoutChangingConfiguration()
    {
        var controller = CreateController();
        var emergency = new EmergencyStopController();
        var handler = new WorkerCommandHandler(
            new WorkerStatusProvider(new WorkerStateMachine(), emergency, autoDialogueController: controller),
            emergency,
            autoDialogueController: controller);

        var response = await handler.HandleAsync(
            Command(
                "invalid",
                "features.autoDialogue.setOptions",
                JsonSerializer.SerializeToElement(new AutoDialogueOptions { Enabled = true, OptionStrategy = "Middle" }, CompanionProtocol.JsonOptions)),
            CancellationToken.None);

        Assert.Equal("invalid_payload", response.Error!.Code);
        Assert.False(controller.Options.Enabled);
        Assert.Equal("First", controller.Options.OptionStrategy);
    }

    [Fact]
    public void Status_ShouldExposeDialogueRecognitionVoiceAndIntentState()
    {
        var controller = CreateController();
        controller.SetEnabled(true);
        controller.Report(77, "Talk", ["选项一", "选项二"], "custom_priority", true, true, false, DateTimeOffset.UnixEpoch);
        var provider = new WorkerStatusProvider(
            new WorkerStateMachine(),
            new EmergencyStopController(),
            autoDialogueController: controller);

        var status = provider.GetStatus(
            new WorkerLaunchOptions("pipe", "0123456789abcdef0123456789abcdef", 123, 1),
            "test",
            DateTimeOffset.UnixEpoch);

        Assert.True(status.Features.AutoDialogue.IsEnabled);
        var recognition = Assert.IsType<AutoDialogueRecognitionStatus>(status.Features.AutoDialogue.DialogueRecognition);
        Assert.Equal("Talk", recognition.UiCategory);
        Assert.Equal(["选项一", "选项二"], recognition.Options);
        Assert.True(recognition.IntentSubmitted);
        Assert.True(recognition.VoiceWaitActive);
        Assert.False(recognition.VoiceWaitFallback);
        Assert.Equal(77, recognition.FrameSequence);
    }

    private static AutoDialogueController CreateController() =>
        new(new RootedAssetPathResolver(AppContext.BaseDirectory));

    private static WorkerCommandContext Command(string id, string method, JsonElement? payload = null) =>
        new(
            new CompanionEnvelope
            {
                Type = CompanionProtocol.Request,
                CorrelationId = id,
                Method = method,
                Payload = payload,
            },
            new WorkerLaunchOptions("pipe", "0123456789abcdef0123456789abcdef", 123, 1),
            "test",
            DateTimeOffset.UnixEpoch);
}
