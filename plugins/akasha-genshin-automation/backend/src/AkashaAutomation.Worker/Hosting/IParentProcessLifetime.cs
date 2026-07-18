namespace AkashaAutomation.Worker.Hosting;

public interface IParentProcessLifetime : IDisposable
{
    bool IsAlive { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);
}
