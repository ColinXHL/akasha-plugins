using System.Diagnostics;
using AkashaAutomation.Core.Abstractions;
using AkashaAutomation.Core.Diagnostics;
using AkashaAutomation.Core.Scheduling;
using AkashaAutomation.Features.AutoPick;
using AkashaAutomation.Features.AutoDialogue;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AkashaAutomation.Worker.Hosting;

public sealed class AutomationSchedulerHostedService : BackgroundService, IWorkerRuntimeResource
{
    private readonly SingleFrameScheduler _scheduler;
    private readonly IAutoPickController _autoPickController;
    private readonly IAutoDialogueController _autoDialogueController;
    private readonly IOcrEngine _ocrEngine;
    private readonly IClock _clock;
    private readonly IDiagnosticsSink _diagnostics;
    private readonly ILogger<AutomationSchedulerHostedService> _logger;

    public AutomationSchedulerHostedService(
        SingleFrameScheduler scheduler,
        IAutoPickController autoPickController,
        IAutoDialogueController autoDialogueController,
        IOcrEngine ocrEngine,
        IClock clock,
        IDiagnosticsSink diagnostics,
        ILogger<AutomationSchedulerHostedService> logger)
    {
        _scheduler = scheduler;
        _autoPickController = autoPickController;
        _autoDialogueController = autoDialogueController;
        _ocrEngine = ocrEngine;
        _clock = clock;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        try
        {
            var started = Stopwatch.GetTimestamp();
            await _ocrEngine.WarmUpAsync(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation(
                "OCR warm-up completed in {ElapsedMilliseconds:F0} ms",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "OCR warm-up failed; recognition will retry on demand");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_autoPickController.Options.Enabled && !_autoDialogueController.Options.Enabled)
            {
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(100), stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                var started = Stopwatch.GetTimestamp();
                var result = await _scheduler.RunOnceAsync(stoppingToken).ConfigureAwait(false);
                var cadence = result.Captured ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(250);
                var remaining = cadence - Stopwatch.GetElapsedTime(started);
                if (remaining > TimeSpan.Zero)
                {
                    await _clock.DelayAsync(remaining, stoppingToken).ConfigureAwait(false);
                }
                else
                {
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _diagnostics.Write(
                    new DiagnosticEvent(
                        _clock.UtcNow,
                        "scheduler",
                        "loop_failed",
                        new Dictionary<string, object?>
                        {
                            ["exceptionType"] = exception.GetType().FullName,
                            ["message"] = exception.Message,
                        }));
                _logger.LogError(exception, "Automation scheduler iteration failed");
                await _clock.DelayAsync(TimeSpan.FromMilliseconds(250), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    async ValueTask IWorkerRuntimeResource.StopAsync(CancellationToken cancellationToken) =>
        await StopAsync(cancellationToken).ConfigureAwait(false);
}
