namespace AkashaAutomation.Worker.Hosting;

public interface IWorkerRuntimeResource
{
    ValueTask StopAsync(CancellationToken cancellationToken);
}
