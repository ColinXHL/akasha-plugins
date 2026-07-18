using AkashaAutomation.Worker.Configuration;
using AkashaAutomation.Worker.Hosting;

namespace AkashaAutomation.Worker;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        var parseResult = WorkerLaunchOptions.Parse(args);
        if (!parseResult.IsSuccess)
        {
            return (int)WorkerExitCode.InvalidArguments;
        }

        if (!ProcessParentProcessLifetime.TryCreate(parseResult.Options!.ParentProcessId, out var parentProcess))
        {
            return (int)WorkerExitCode.ParentProcessUnavailable;
        }

        using (parentProcess)
        {
            try
            {
                return await WorkerHost
                    .RunAsync(parseResult.Options, parentProcess)
                    .ConfigureAwait(false);
            }
            catch
            {
                return (int)WorkerExitCode.UnexpectedError;
            }
        }
    }
}
