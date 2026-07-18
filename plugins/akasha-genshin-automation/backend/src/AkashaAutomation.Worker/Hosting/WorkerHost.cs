using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Capture;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.GameContext;
using AkashaAutomation.Core.Input;
using AkashaAutomation.Core.Recognition;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Core.Ocr;
using AkashaAutomation.BetterGiPort.Compatibility.AutoPick;
using AkashaAutomation.BetterGiPort.Compatibility.AutoSkip;
using AkashaAutomation.BetterGiPort.Compatibility.Ocr;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AkashaAutomation.Worker.Hosting;

public static class WorkerHost
{
    public static async Task<int> RunAsync(
        WorkerLaunchOptions options,
        IParentProcessLifetime parentProcess,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(parentProcess);

        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                ApplicationName = typeof(WorkerHost).Assembly.GetName().Name,
                Args = [],
                DisableDefaults = true,
            });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddProvider(
            new JsonRollingFileLoggerProvider(
                new JsonRollingFileLoggerOptions(WorkerLogPaths.GetDefaultLogFilePath())));

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(parentProcess);
        builder.Services.AddAutomationCore();
        builder.Services.AddHostedService<WorkerExceptionObserver>();
        builder.Services.AddSingleton<WorkerRuntime>(services =>
            new WorkerRuntime(
                services.GetServices<IWorkerRuntimeResource>(),
                services.GetRequiredService<ILoggerFactory>(),
                autoPickController: services.GetRequiredService<IAutoPickController>(),
                autoDialogueController: services.GetRequiredService<IAutoDialogueController>(),
                realInputEnabled: true));
        builder.Services.AddSingleton<WorkerApplication>(services =>
            new WorkerApplication(
                services.GetRequiredService<IParentProcessLifetime>(),
                services.GetRequiredService<WorkerRuntime>(),
                services.GetRequiredService<ILogger<WorkerApplication>>(),
                inputArbiter: services.GetRequiredService<IInputArbiter>(),
                autoPickController: services.GetRequiredService<IAutoPickController>(),
                autoDialogueController: services.GetRequiredService<IAutoDialogueController>()));

        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await host.Services
                    .GetRequiredService<WorkerApplication>()
                    .RunAsync(options, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                host.Dispose();
            }
        }
    }

    public static IServiceCollection AddAutomationCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDiagnosticsSink, InMemoryDiagnosticsSink>();
        services.AddSingleton<IAssetPathResolver>(_ => new RootedAssetPathResolver(AppContext.BaseDirectory));
        services.AddSingleton<IGameWindowLocator>(_ => new WindowsGameWindowLocator());
        services.AddSingleton<IGameContextProvider, GameContextProvider>();
        services.AddSingleton<ITemplateMatcher, OpenCvTemplateMatcher>();
        services.AddSingleton<BetterGiAutoPickRecognizer>();
        services.AddSingleton<IPaddleOcrSessionFactory, PaddleOnnxOcrSessionFactory>();
        services.AddSingleton(services => BetterGiPaddleOcrAssets.CreateV4Options(
            services.GetRequiredService<IAssetPathResolver>()));
        services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
        services.AddSingleton<BetterGiAutoDialogueRecognizer>();
        services.AddSingleton<IGameUiContextClassifier>(services => services.GetRequiredService<BetterGiAutoDialogueRecognizer>());
        services.AddSingleton<IAutoPickController>(services =>
            new AutoPickController(
                services.GetRequiredService<IAssetPathResolver>(),
                PluginResourcePaths.ResolvePickBlacklistPath()));
        services.AddSingleton<AutoPickFeature>();
        services.AddSingleton<IAutomationFeature>(services => services.GetRequiredService<AutoPickFeature>());
        services.AddSingleton<IAutoDialogueController, AutoDialogueController>();
        services.AddSingleton<IDialogueOptionVoiceWaiter, SileroDialogueOptionVoiceWaiter>();
        services.AddSingleton<RewardDialogueSceneHandler>();
        services.AddSingleton<HangoutDialogueSceneHandler>();
        services.AddSingleton<PopupDialogueSceneHandler>();
        services.AddSingleton<BlackScreenDialogueSceneHandler>();
        services.AddSingleton<SubmitGoodsDialogueSceneHandler>();
        services.AddSingleton<IAutoDialogueSceneHandler>(services => services.GetRequiredService<RewardDialogueSceneHandler>());
        services.AddSingleton<IAutoDialogueSceneHandler>(services => services.GetRequiredService<HangoutDialogueSceneHandler>());
        services.AddSingleton<IAutoDialogueSceneHandler>(services => services.GetRequiredService<PopupDialogueSceneHandler>());
        services.AddSingleton<IAutoDialogueSceneHandler>(services => services.GetRequiredService<BlackScreenDialogueSceneHandler>());
        services.AddSingleton<IAutoDialogueSceneHandler>(services => services.GetRequiredService<SubmitGoodsDialogueSceneHandler>());
        services.AddSingleton<AutoDialogueFeature>();
        services.AddSingleton<IAutomationFeature>(services => services.GetRequiredService<AutoDialogueFeature>());
        services.AddSingleton<ICaptureSource, WindowsBitBltCaptureSource>();
        services.AddSingleton<IInputService, WindowsSendInputService>();
        services.AddSingleton<InputArbiter>();
        services.AddSingleton<IInputArbiter>(services => services.GetRequiredService<InputArbiter>());
        services.AddSingleton<IWorkerRuntimeResource, AutoDialogueRuntimeResource>();
        services.AddSingleton<IWorkerRuntimeResource, AutomationRecognitionRuntimeResource>();
        services.AddSingleton<IWorkerRuntimeResource, AutomationInputRuntimeResource>();
        services.AddSingleton<SingleFrameScheduler>();
        services.AddSingleton<AutomationSchedulerHostedService>();
        services.AddSingleton<IHostedService>(services => services.GetRequiredService<AutomationSchedulerHostedService>());
        services.AddSingleton<IWorkerRuntimeResource>(services => services.GetRequiredService<AutomationSchedulerHostedService>());
        return services;
    }
}
