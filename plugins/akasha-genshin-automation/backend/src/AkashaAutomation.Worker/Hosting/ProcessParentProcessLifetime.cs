using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;

namespace AkashaAutomation.Worker.Hosting;

public sealed class ProcessParentProcessLifetime : IParentProcessLifetime
{
    private readonly Process _process;

    private ProcessParentProcessLifetime(Process process)
    {
        _process = process;
    }

    public bool IsAlive
    {
        get
        {
            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static bool TryCreate(
        int processId,
        [NotNullWhen(true)] out ProcessParentProcessLifetime? lifetime)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                process.Dispose();
                lifetime = null;
                return false;
            }

            lifetime = new ProcessParentProcessLifetime(process);
            return true;
        }
        catch (ArgumentException)
        {
            lifetime = null;
            return false;
        }
        catch (InvalidOperationException)
        {
            lifetime = null;
            return false;
        }
        catch (Win32Exception)
        {
            lifetime = null;
            return false;
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void Dispose() => _process.Dispose();
}
