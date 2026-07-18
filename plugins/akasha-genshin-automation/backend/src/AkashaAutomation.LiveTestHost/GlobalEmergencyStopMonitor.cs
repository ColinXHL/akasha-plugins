using System.Runtime.InteropServices;

namespace AkashaAutomation.LiveTestHost;

public static class GlobalEmergencyStopMonitor
{
    private const int VirtualKeyControl = 0x11;
    private const int VirtualKeyMenu = 0x12;
    private const int VirtualKeyF12 = 0x7B;

    public static Task RunAsync(CancellationTokenSource runCancellation) =>
        RunAsync(runCancellation, IsDown, TimeSpan.FromMilliseconds(25));

    public static async Task RunAsync(
        CancellationTokenSource runCancellation,
        Func<int, bool> isKeyDown,
        TimeSpan pollInterval)
    {
        ArgumentNullException.ThrowIfNull(runCancellation);
        ArgumentNullException.ThrowIfNull(isKeyDown);
        if (pollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        try
        {
            while (!runCancellation.IsCancellationRequested)
            {
                if (isKeyDown(VirtualKeyControl) &&
                    isKeyDown(VirtualKeyMenu) &&
                    isKeyDown(VirtualKeyF12))
                {
                    Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss.fff}] EMERGENCY STOP: Ctrl+Alt+F12");
                    runCancellation.Cancel();
                    return;
                }

                await Task.Delay(pollInterval, runCancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
