using System.Text.Json;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Worker.Bridge;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class AutoPickWorkerIntegrationTests
{
    [Fact]
    public async Task AutoPickCommands_ShouldRoundTripOptionsAndEnabledState()
    {
        var controller = CreateController();
        var state = new WorkerStateMachine();
        var emergency = new EmergencyStopController();
        var status = new WorkerStatusProvider(state, emergency, controller);
        var handler = new WorkerCommandHandler(status, emergency, controller);
        var options = new AutoPickOptions
        {
            Enabled = true,
            PickKey = "e",
            BlackListEnabled = true,
            WhiteListEnabled = true,
            UserExactBlacklist = ["不要拾取"],
            UserFuzzyBlacklist = ["模糊"],
            UserWhitelist = ["允许交互"],
        };

        var set = await handler.HandleAsync(
            Command("set", "features.autoPick.setOptions", JsonSerializer.SerializeToElement(options, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var enabled = await handler.HandleAsync(
            Command("disable", "features.autoPick.setEnabled", JsonSerializer.SerializeToElement(new { enabled = false }, CompanionProtocol.JsonOptions)),
            CancellationToken.None);
        var get = await handler.HandleAsync(
            Command("get", "features.autoPick.getOptions"),
            CancellationToken.None);

        Assert.Null(set.Error);
        Assert.Equal("E", set.Payload!.Value.GetProperty("pickKey").GetString());
        Assert.False(enabled.Payload!.Value.GetProperty("enabled").GetBoolean());
        Assert.False(get.Payload!.Value.GetProperty("enabled").GetBoolean());
        Assert.Equal("允许交互", get.Payload.Value.GetProperty("userWhitelist")[0].GetString());
    }

    [Fact]
    public async Task AutoPickSetOptions_ShouldRejectUnsupportedKeyWithoutChangingConfiguration()
    {
        var controller = CreateController();
        var handler = new WorkerCommandHandler(
            new WorkerStatusProvider(new WorkerStateMachine(), new EmergencyStopController(), controller),
            new EmergencyStopController(),
            controller);
        var invalid = new AutoPickOptions { Enabled = true, PickKey = "Q" };

        var response = await handler.HandleAsync(
            Command("invalid", "features.autoPick.setOptions", JsonSerializer.SerializeToElement(invalid, CompanionProtocol.JsonOptions)),
            CancellationToken.None);

        Assert.Equal("invalid_payload", response.Error!.Code);
        Assert.Equal("F", controller.Options.PickKey);
        Assert.False(controller.Options.Enabled);
    }

    [Fact]
    public void WorkerStatus_ShouldExposeLatestAutoPickTextDecisionAndSubmission()
    {
        var controller = CreateController();
        controller.SetEnabled(true);
        controller.Report(42, "甜甜花", "pick", true, DateTimeOffset.UnixEpoch);
        var provider = new WorkerStatusProvider(
            new WorkerStateMachine(),
            new EmergencyStopController(),
            controller);

        var status = provider.GetStatus(
            new WorkerLaunchOptions("pipe", "0123456789abcdef0123456789abcdef", 123, 1),
            "test",
            DateTimeOffset.UnixEpoch);

        Assert.True(status.Features.AutoPick.IsEnabled);
        Assert.True(status.Features.AutoPick.IsRunning);
        var recognition = Assert.IsType<AutoPickRecognitionStatus>(status.Features.AutoPick.Recognition);
        Assert.Equal("甜甜花", recognition.Text);
        Assert.Equal("pick", recognition.Reason);
        Assert.True(recognition.IntentSubmitted);
        Assert.Equal(42, recognition.FrameSequence);
    }

    private static AutoPickController CreateController() =>
        new(new RootedAssetPathResolver(AppContext.BaseDirectory));

    private static WorkerCommandContext Command(
        string correlationId,
        string method,
        JsonElement? payload = null) =>
        new(
            new CompanionEnvelope
            {
                Type = CompanionProtocol.Request,
                CorrelationId = correlationId,
                Method = method,
                Payload = payload,
            },
            new WorkerLaunchOptions("pipe", "0123456789abcdef0123456789abcdef", 123, 1),
            "test",
            DateTimeOffset.UnixEpoch);
}
