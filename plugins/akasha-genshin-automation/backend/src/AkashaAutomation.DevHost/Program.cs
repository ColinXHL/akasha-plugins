using System.Text;

namespace AkashaAutomation.DevHost;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            Console.WriteLine(DevHostOptions.Usage);
            return 0;
        }

        DevHostOptions options;
        try
        {
            options = DevHostOptions.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(DevHostOptions.Usage);
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (options.Feature == "auto-dialogue")
            {
                await new AutoDialogueDevHost(options).RunAsync(cancellation.Token).ConfigureAwait(false);
            }
            else
            {
                await new AutoPickDevHost(options).RunAsync(cancellation.Token).ConfigureAwait(false);
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"DevHost 启动失败: {exception.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
