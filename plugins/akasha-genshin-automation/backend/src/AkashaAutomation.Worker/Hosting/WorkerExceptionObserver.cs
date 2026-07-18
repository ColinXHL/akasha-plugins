using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AkashaAutomation.Worker.Hosting;

public sealed class WorkerExceptionObserver(
    ILogger<WorkerExceptionObserver> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= HandleUnhandledException;
        return Task.CompletedTask;
    }

    public void HandleUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        logger.LogError(
            eventArgs.Exception,
            "An unobserved Worker task exception was reported");
        eventArgs.SetObserved();
    }

    private void HandleUnhandledException(
        object sender,
        UnhandledExceptionEventArgs eventArgs)
    {
        logger.LogCritical(
            eventArgs.ExceptionObject as Exception,
            "An unhandled Worker exception was reported. Terminating: {IsTerminating}",
            eventArgs.IsTerminating);
    }
}
