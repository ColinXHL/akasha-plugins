using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Compatibility.QuickTeleport;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Features.QuickTeleport;
using AkashaAutomation.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class AutomationCoreRegistrationTests
{
    [Fact]
    public async Task AddAutomationCore_RegistersForegroundOnlyRealInput()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutomationCore();
        await using var provider = services.BuildServiceProvider();

        var input = provider.GetRequiredService<IInputService>();
        var capture = provider.GetRequiredService<ICaptureSource>();

        Assert.IsType<WindowsSendInputService>(input);
        Assert.IsType<WindowsBitBltCaptureSource>(capture);
        Assert.IsType<InputArbiter>(provider.GetRequiredService<IInputArbiter>());
        var runtimeResources = provider.GetServices<IWorkerRuntimeResource>().ToArray();
        Assert.Contains(runtimeResources, resource => resource is AutomationInputRuntimeResource);
        Assert.Contains(runtimeResources, resource => resource is AutomationSchedulerHostedService);
        Assert.Contains(runtimeResources, resource => resource is AutoDialogueRuntimeResource);
        Assert.Contains(runtimeResources, resource => resource is AutomationRecognitionRuntimeResource);
        Assert.IsType<PaddleOcrEngine>(provider.GetRequiredService<IOcrEngine>());
        Assert.IsType<BetterGiAutoPickRecognizer>(provider.GetRequiredService<BetterGiAutoPickRecognizer>());
        Assert.IsType<BetterGiQuickTeleportRecognizer>(provider.GetRequiredService<BetterGiQuickTeleportRecognizer>());
        Assert.IsType<AutoPickController>(provider.GetRequiredService<IAutoPickController>());
        Assert.IsType<AutoDialogueController>(provider.GetRequiredService<IAutoDialogueController>());
        Assert.IsType<QuickTeleportController>(provider.GetRequiredService<IQuickTeleportController>());
        Assert.IsType<CompositeGameUiContextClassifier>(provider.GetRequiredService<IGameUiContextClassifier>());
        Assert.Contains(
            provider.GetServices<IGameUiContextDetector>(),
            detector => detector is BetterGiAutoDialogueRecognizer);
        Assert.Contains(
            provider.GetServices<IGameUiContextDetector>(),
            detector => detector is BetterGiQuickTeleportRecognizer);
        Assert.Equal(
            [AutoPickFeature.FeatureId, AutoDialogueFeature.FeatureId, QuickTeleportFeature.FeatureId],
            provider.GetServices<IAutomationFeatureControl>()
                .Select(control => control.FeatureId)
                .ToArray());
        Assert.False(provider.GetRequiredService<AutomationFeatureControls>().AnyEnabled);
        Assert.Contains(provider.GetServices<IAutomationFeature>(), feature => feature is AutoPickFeature);
        Assert.Contains(provider.GetServices<IAutomationFeature>(), feature => feature is AutoDialogueFeature);
        Assert.Contains(provider.GetServices<IAutomationFeature>(), feature => feature is QuickTeleportFeature);
        Assert.IsType<SingleFrameScheduler>(provider.GetRequiredService<SingleFrameScheduler>());
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is AutomationSchedulerHostedService);
    }
}
