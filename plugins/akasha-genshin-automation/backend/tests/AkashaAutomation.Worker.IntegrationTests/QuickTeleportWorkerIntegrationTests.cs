using System.Text.Json;
using AkashaAutomation.Features.QuickTeleport;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class QuickTeleportWorkerIntegrationTests
{
    [Fact]
    public async Task Commands_ShouldRoundTripOptionsAndEnabledState()
    {
        var controller = new QuickTeleportController();
        var emergency = new EmergencyStopController();
        var handler = new WorkerCommandHandler(
            new WorkerStatusProvider(new WorkerStateMachine(), emergency, quickTeleportController: controller),
            emergency,
            quickTeleportController: controller);
        var options = new QuickTeleportOptions
        {
            Enabled = true,
            TeleportListClickDelayMilliseconds = 275,
            WaitTeleportPanelDelayMilliseconds = 75,
        };

        var set = await handler.HandleAsync(
            Command("set", "features.quickTeleport.setOptions", JsonSerializer.SerializeToElement(options, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var disable = await handler.HandleAsync(
            Command("disable", "features.quickTeleport.setEnabled", JsonSerializer.SerializeToElement(new { enabled = false }, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var get = await handler.HandleAsync(
            Command("get", "features.quickTeleport.getOptions"),
            CancellationToken.None);

        Assert.Null(set.Error);
        Assert.Equal(275, set.Payload!.Value.GetProperty("teleportListClickDelayMilliseconds").GetInt32());
        Assert.False(disable.Payload!.Value.GetProperty("enabled").GetBoolean());
        Assert.Equal(75, get.Payload!.Value.GetProperty("waitTeleportPanelDelayMilliseconds").GetInt32());
    }

    [Fact]
    public async Task InvalidTiming_ShouldBeRejectedWithoutChangingOptions()
    {
        var controller = new QuickTeleportController();
        var emergency = new EmergencyStopController();
        var handler = new WorkerCommandHandler(
            new WorkerStatusProvider(new WorkerStateMachine(), emergency, quickTeleportController: controller),
            emergency,
            quickTeleportController: controller);

        var response = await handler.HandleAsync(
            Command(
                "invalid",
                "features.quickTeleport.setOptions",
                JsonSerializer.SerializeToElement(
                    new QuickTeleportOptions { Enabled = true, TeleportListClickDelayMilliseconds = -1 },
                    CompanionProtocol.JsonOptions)),
            CancellationToken.None);

        Assert.Equal("invalid_payload", response.Error!.Code);
        Assert.False(controller.Options.Enabled);
        Assert.Equal(200, controller.Options.TeleportListClickDelayMilliseconds);
    }

    [Fact]
    public void Status_ShouldExposeStateCandidateAndIntent()
    {
        var controller = new QuickTeleportController();
        controller.SetEnabled(true);
        controller.Report(91, "AwaitTeleportPanel", "传送锚点", "click_candidate", true, DateTimeOffset.UnixEpoch);
        var provider = new WorkerStatusProvider(
            new WorkerStateMachine(),
            new EmergencyStopController(),
            quickTeleportController: controller);

        var status = provider.GetStatus(
            new WorkerLaunchOptions("pipe", "0123456789abcdef0123456789abcdef", 123, 1),
            "test",
            DateTimeOffset.UnixEpoch);

        Assert.True(status.Features.QuickTeleport.IsEnabled);
        var recognition = Assert.IsType<QuickTeleportRecognitionStatus>(status.Features.QuickTeleport.QuickTeleportRecognition);
        Assert.Equal("AwaitTeleportPanel", recognition.State);
        Assert.Equal("传送锚点", recognition.CandidateText);
        Assert.Equal("click_candidate", recognition.Reason);
        Assert.True(recognition.IntentSubmitted);
        Assert.Equal(91, recognition.FrameSequence);
    }

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
