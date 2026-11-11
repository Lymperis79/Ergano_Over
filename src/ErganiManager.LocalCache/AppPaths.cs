namespace ErganiManager.LocalCache;

/// <summary>
/// Resolves OS-appropriate locations for app config and the local cache DB.
/// Windows: %AppData%/ErganiManager/
/// Linux:   ~/.config/ErganiManager/
/// </summary>
public static class AppPaths
{
    public const string AppFolderName = "ErganiManager";

    public static string GetAppDataFolder()
    {
        string baseFolder;

        if (OperatingSystem.IsWindows())
        {
            baseFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }
        else
        {
            // Linux / macOS: follow XDG convention, fall back to ~/.config
            var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            baseFolder = !string.IsNullOrWhiteSpace(xdgConfig)
                ? xdgConfig
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var fullPath = Path.Combine(baseFolder, AppFolderName);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    public static string GetConnectionConfigPath() =>
        Path.Combine(GetAppDataFolder(), "connection.json");

    public static string GetLocalCacheDbPath() =>
        Path.Combine(GetAppDataFolder(), "local_cache.db");

    public static string GetLogsFolder()
    {
        var path = Path.Combine(GetAppDataFolder(), "logs");
        Directory.CreateDirectory(path);
        return path;
    }
}
