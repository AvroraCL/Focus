using System.Text.Json;
using System.Text.Json.Serialization;

namespace Focus.Core.Profiles;

public static class ProfileCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(HudProfile profile) => JsonSerializer.Serialize(profile, Options);

    public static HudProfile Parse(string json)
    {
        var profile = JsonSerializer.Deserialize<HudProfile>(json, Options)
                      ?? throw new InvalidDataException("Profile JSON is empty.");
        profile.Normalize();
        return profile;
    }

    public static HudProfile Load(string path) => Parse(File.ReadAllText(path));

    public static void Save(HudProfile profile, string path)
    {
        profile.UpdatedAt = DateTimeOffset.Now;
        var json = Serialize(profile);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        Focus.Core.Native.AtomicFile.WriteAllText(path, json);
    }

    public static string Export(HudProfile profile) => Serialize(profile);

    public static HudProfile Import(string json) => Parse(json);
}
