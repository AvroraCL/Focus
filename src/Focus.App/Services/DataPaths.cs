using System.IO;

namespace Focus.App.Services;

public static class DataPaths
{
    public static string Root { get; } = BuildRoot();

    private static string BuildRoot()
    {
        string folder = "Focus";
        if (Core.FocusInstance.Suffix != "v1") folder += "-" + Core.FocusInstance.Suffix;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folder);
    }

    public static string ProfilesDir => Path.Combine(Root, "profiles");
    public static string SessionsDir => Path.Combine(Root, "sessions");
    public static string LogsDir => Path.Combine(Root, "logs");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string SettingsBackup => Path.Combine(Root, "settings.backup.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(SessionsDir);
        Directory.CreateDirectory(LogsDir);
    }

    public static string ProfilePath(string id) => Path.Combine(ProfilesDir, id + ".json");

    public static string SessionPath(string id) => Path.Combine(SessionsDir, id + ".fss");

    /// <summary>Deletes day logs older than <paramref name="days"/> days.</summary>
    public static void CleanupOldLogs(int days = 7)
    {
        try
        {
            if (!Directory.Exists(LogsDir)) return;
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var file in Directory.EnumerateFiles(LogsDir, "focus-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
                }
                catch
                {
                    // A file in use is skipped.
                }
            }
        }
        catch
        {
            // Never fail startup because of log cleanup.
        }
    }

    public static void AppendLog(string message)
    {
        try
        {
            Directory.CreateDirectory(LogsDir);
            var file = Path.Combine(LogsDir, $"focus-{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
