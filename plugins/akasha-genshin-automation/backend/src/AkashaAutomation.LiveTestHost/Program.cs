using System.Text;

namespace AkashaAutomation.LiveTestHost;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            Console.WriteLine("用法：以管理员身份直接运行 AkashaAutomation.LiveTestHost.exe，然后按菜单选择功能。");
            return 0;
        }

        if (args.Length == 0)
        {
            return await LiveTestConsole.RunAsync(RunSessionAsync).ConfigureAwait(false);
        }

        Console.Error.WriteLine("无需命令行参数；请直接运行 AkashaAutomation.LiveTestHost.exe。");
        return 2;
    }

    internal static async Task<int> RunSessionAsync(LiveTestHostOptions options)
    {
        if (!ProcessElevation.IsAdministrator())
        {
            Console.Error.WriteLine("拒绝启动：LiveTestHost 必须从管理员 PowerShell/Windows Terminal 运行，否则原神会忽略 SendInput。");
            return 4;
        }

        using var runCancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            runCancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            Console.WriteLine("将持续运行到手动停止；切出游戏会自动拒绝输入。");
            for (var remaining = 3; remaining > 0; remaining--)
            {
                Console.WriteLine($"{remaining} 秒后启用真实输入；请切回原神。按 Ctrl+C 可取消。");
                await Task.Delay(TimeSpan.FromSeconds(1), runCancellation.Token).ConfigureAwait(false);
            }

            Console.WriteLine("LIVE INPUT ARMED");
            var emergencyMonitor = GlobalEmergencyStopMonitor.RunAsync(runCancellation);
            await new LiveAutomationHost(options).RunAsync(runCancellation.Token).ConfigureAwait(false);
            await emergencyMonitor.ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LiveTestHost 启动失败: {exception.Message}");
            return 1;
        }
        finally
        {
            runCancellation.Cancel();
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
