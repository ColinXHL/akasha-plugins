namespace AkashaAutomation.Worker.Configuration;

public static class PluginResourcePaths
{
    public const string DataDirectoryEnvironmentVariable = "AKASHA_PLUGIN_DATA_DIR";

    public static string? ResolvePickBlacklistPath(string? pluginDataDirectory = null)
    {
        pluginDataDirectory ??=
            Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(pluginDataDirectory))
        {
            return null;
        }

        return Path.Combine(
            Path.GetFullPath(pluginDataDirectory),
            "pick-blacklist",
            "current.json");
    }
}
