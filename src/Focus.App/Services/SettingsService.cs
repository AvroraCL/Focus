using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Focus.Core.App;

namespace Focus.App.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(DataPaths.SettingsFile))
            {
                var json = File.ReadAllText(DataPaths.SettingsFile);
                Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings load failed: " + ex.Message);
            TryRecoverBackup();
        }
        Current.Normalize();
    }

    private void TryRecoverBackup()
    {
        try
        {
            if (!File.Exists(DataPaths.SettingsBackup)) return;
            var json = File.ReadAllText(DataPaths.SettingsBackup);
            Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            DataPaths.AppendLog("settings restored from backup");
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings backup restore failed: " + ex.Message);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            DataPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(Current, Options);
            var path = DataPaths.SettingsFile;

            if (File.Exists(path))
            {
                try
                {
                    File.Copy(path, DataPaths.SettingsBackup, overwrite: true);
                }
                catch (Exception ex)
                {
                    DataPaths.AppendLog("settings backup failed: " + ex.Message);
                }
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);

            // Antivirus / indexing services sometimes hold the file for a few milliseconds.
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    Thread.Sleep(40);
                }
                catch (UnauthorizedAccessException) when (attempt < 3)
                {
                    Thread.Sleep(40);
                }
            }

            // Last resort: write in place so the user's settings are never silently lost.
            File.WriteAllText(path, json);
            try { File.Delete(temp); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings save failed: " + ex.Message);
        }
    }
}
