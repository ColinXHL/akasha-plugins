namespace AkashaAutomation.LiveTestHost;

internal static class LiveTestConsole
{
    public static async Task<int> RunAsync(Func<LiveTestHostOptions, Task<int>> runSession)
    {
        ArgumentNullException.ThrowIfNull(runSession);
        Console.WriteLine("Akasha Automation 实机测试");
        Console.WriteLine("只保留两个功能开关；运行时切回本窗口按 Ctrl+C 停止。\n");
        if (!ProcessElevation.IsAdministrator())
        {
            Console.Error.WriteLine("请从管理员 PowerShell/Windows Terminal 启动本程序。");
            return 4;
        }

        var autoPick = true;
        var autoDialogue = true;
        while (true)
        {
            Console.WriteLine($"  1. 自动拾取  [{State(autoPick)}]");
            Console.WriteLine($"  2. 自动剧情  [{State(autoDialogue)}]");
            Console.WriteLine("按 1/2 切换，直接回车开始，输入 0 退出。");
            Console.Write("选择：");

            switch (Console.ReadLine())
            {
                case "1":
                    autoPick = !autoPick;
                    Console.WriteLine();
                    continue;
                case "2":
                    autoDialogue = !autoDialogue;
                    Console.WriteLine();
                    continue;
                case "0":
                case null:
                    return 0;
                case "":
                    if (!autoPick && !autoDialogue)
                    {
                        Console.WriteLine("至少开启一个功能。\n");
                        continue;
                    }

                    break;
                default:
                    Console.WriteLine("无效选择。\n");
                    continue;
            }

            Console.WriteLine("3 秒后开始持续运行；自动剧情固定优先选择第一个普通选项。");
            _ = await runSession(new LiveTestHostOptions(autoPick, autoDialogue)).ConfigureAwait(false);
            Console.WriteLine("本轮已停止。\n");
        }
    }

    private static string State(bool enabled) => enabled ? "开" : "关";
}
