using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AkashaAutomation.Worker.IntegrationTests;

public sealed class AutomationCoreRegistrationTests
{
    [Fact]
    public void PluginResourcePaths_ShouldResolvePickBlacklistBelowPassedDataDirectory()
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"akasha-plugin-data-{Guid.NewGuid():N}");

        var result = PluginResourcePaths.ResolvePickBlacklistPath(dataDirectory);

        Assert.Equal(
            Path.Combine(Path.GetFullPath(dataDirectory), "pick-blacklist", "current.json"),
            result);
    }

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
        Assert.IsType<AutoPickController>(provider.GetRequiredService<IAutoPickController>());
        Assert.IsType<AutoDialogueController>(provider.GetRequiredService<IAutoDialogueController>());
        Assert.IsType<BetterGiAutoDialogueRecognizer>(provider.GetRequiredService<IGameUiContextClassifier>());
        Assert.Contains(provider.GetServices<IAutomationFeature>(), feature => feature is AutoPickFeature);
        Assert.Contains(provider.GetServices<IAutomationFeature>(), feature => feature is AutoDialogueFeature);
        Assert.IsType<SingleFrameScheduler>(provider.GetRequiredService<SingleFrameScheduler>());
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is AutomationSchedulerHostedService);
    }
}
