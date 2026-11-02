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

    private readonly string _path;
    private readonly string _backup;
    public SettingsService(string? settingsPath = null)
    {
        _path = settingsPath ?? DataPaths.SettingsFile;
        _backup = settingsPath is null ? DataPaths.SettingsBackup : settingsPath + ".backup";
    }

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
            else TryRecoverBackup();
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
            if (!File.Exists(_backup)) return;
            var json = File.ReadAllText(_backup);
            Current = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            DataPaths.AppendLog("settings restored from backup");
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("settings backup restore failed: " + ex.Message);
            Current = new AppSettings();
        }
    }

    private readonly object _saveGate = new();
    public string? LastSaveError { get; private set; }
    public event Action<string>? SaveFailed;
    public bool Save()
    {
        lock (_saveGate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var json = JsonSerializer.Serialize(Current, Options);
                if (File.Exists(_path))
                {
                    // Never overwrite a recoverable backup with malformed JSON.
                    var previous = File.ReadAllText(_path);
                    try
                    {
                        using var valid = JsonDocument.Parse(previous);
                        Focus.Core.Native.AtomicFile.WriteAllText(_backup, previous);
                    }
                    catch (JsonException) { }
                }
                Focus.Core.Native.AtomicFile.WriteAllText(_path, json);
                LastSaveError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                DataPaths.AppendLog("settings save failed: " + ex.Message);
                SaveFailed?.Invoke(ex.Message);
                return false;
            }
        }
    }
}
