namespace AkashaAutomation.Worker.Logging;

public static class WorkerLogPaths
{
    public static string GetDefaultLogFilePath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            localData = Path.GetTempPath();
        }

        return Path.Combine(localData, "AkashaAutomation", "Logs", "worker.log");
    }
}
