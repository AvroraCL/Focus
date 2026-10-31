using System.IO;
using Focus.Core.Profiles;
using Focus.Hud.Presets;

namespace Focus.App.Services;

public sealed class ProfileStore
{
    private readonly List<HudProfile> _custom = new();
    private readonly Dictionary<string, HudProfile> _presets = new();

    public ProfileStore()
    {
        foreach (var preset in PresetLibrary.CreateAll())
        {
            _presets[preset.Id] = preset;
        }
    }

    public event Action? Changed;

    public IReadOnlyList<HudProfile> Custom => _custom;
    public IReadOnlyList<HudProfile> Presets => _presets.Values.ToList();

    public void Load()
    {
        _custom.Clear();
        try
        {
            DataPaths.EnsureCreated();
            foreach (var file in Directory.EnumerateFiles(DataPaths.ProfilesDir, "*.json"))
            {
                try
                {
                    var profile = ProfileCodec.Load(file);
                    if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Path.GetFileNameWithoutExtension(file);
                    if (PresetLibrary.IsPresetId(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
                    _custom.RemoveAll(p => p.Id == profile.Id);
                    _custom.Add(profile);
                }
                catch (Exception ex)
                {
                    DataPaths.AppendLog($"profile load failed ({Path.GetFileName(file)}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("profile scan failed: " + ex.Message);
        }

        _custom.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
    }

    public HudProfile? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _custom.FirstOrDefault(p => p.Id == id) ?? _presets.GetValueOrDefault(id);
    }

    public bool IsPreset(string? id) => id is not null && _presets.ContainsKey(id);

    public HudProfile SaveNewFrom(HudProfile source, string? name = null)
    {
        var copy = source.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = name ?? BuildCopyName(source.Name);
        copy.GameExecutable = null;
        _custom.Add(copy);
        Save(copy);
        Changed?.Invoke();
        return copy;
    }

    public HudProfile CreateBlank(string? name = null)
    {
        var profile = new HudProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name ?? "New HUD",
            CanvasWidth = 360,
            CanvasHeight = 140,
        };
        _custom.Add(profile);
        Save(profile);
        Changed?.Invoke();
        return profile;
    }

    private string BuildCopyName(string baseName)
    {
        string name = baseName + " Copy";
        int index = 2;
        while (_custom.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            name = $"{baseName} Copy {index++}";
        }
        return name;
    }

    public void Save(HudProfile profile)
    {
        try
        {
            DataPaths.EnsureCreated();
            ProfileCodec.Save(profile, DataPaths.ProfilePath(profile.Id));
            var existing = _custom.FindIndex(p => p.Id == profile.Id);
            if (existing < 0) _custom.Add(profile);
            else _custom[existing] = profile;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("profile save failed: " + ex.Message);
        }
    }

    public void Delete(string id)
    {
        _custom.RemoveAll(p => p.Id == id);
        try
        {
            var path = DataPaths.ProfilePath(id);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("profile delete failed: " + ex.Message);
        }
        Changed?.Invoke();
    }

    public bool Import(string file, out HudProfile? profile, out string? error)
    {
        profile = null;
        error = null;
        try
        {
            var imported = ProfileCodec.Load(file);
            imported.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(imported.Name)) imported.Name = Path.GetFileNameWithoutExtension(file);
            imported.Name += " (Imported)";
            _custom.Add(imported);
            Save(imported);
            profile = imported;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Export(HudProfile profile, string file) => ProfileCodec.Save(profile, file);

    public HudProfile? FindByGame(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        return _custom.FirstOrDefault(p =>
            !string.IsNullOrWhiteSpace(p.GameExecutable) &&
            string.Equals(p.GameExecutable, executable, StringComparison.OrdinalIgnoreCase));
    }

    public HudProfile? DefaultProfile
    {
        get
        {
            var settings = SettingsAccessor.Current?.Current;
            var configured = Get(settings?.ActiveProfileId);
            return configured ?? _custom.FirstOrDefault() ?? _presets.GetValueOrDefault(PresetLibrary.ClassicId);
        }
    }
}

/// <summary>Bridge that lets the store read the active default profile without a circular dependency.</summary>
public static class SettingsAccessor
{
    public static SettingsService? Current { get; set; }
}
