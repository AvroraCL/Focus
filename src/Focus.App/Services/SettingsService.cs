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
            if (File.Exists(DataPaths.SettingsFile))
            {
                File.Copy(DataPaths.SettingsFile, DataPaths.SettingsBackup, overwrite: true);
            }
            var temp = DataPaths.SettingsFile + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, DataPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings save failed: " + ex.Message);
        }
    }
}
