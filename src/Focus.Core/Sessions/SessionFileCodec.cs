using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Focus.Core.Sessions;

/// <summary>
/// Compact binary container for captured sessions: gzip(header magic | metadata json | float32 frame times).
/// </summary>
public static class SessionFileCodec
{
    private const uint Magic = 0x31535346; // "FSS1"
    private const int MaxFrames = 20_000_000;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void Save(PerformanceSession session, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var meta = new SessionMetadata
        {
            Id = session.Id,
            Name = session.Name,
            ProcessName = session.ProcessName,
            ProcessId = session.ProcessId,
            ProfileId = session.ProfileId,
            StartedAt = session.StartedAt,
            EndedAt = session.EndedAt,
            Stats = session.Stats,
            FrameCount = session.FrameTimes.Length,
        };

        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var writer = new BinaryWriter(gzip, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        var metaBytes = JsonSerializer.SerializeToUtf8Bytes(meta, Options);
        writer.Write(metaBytes.Length);
        writer.Write(metaBytes);
        writer.Write(session.FrameTimes.Length);
        foreach (var t in session.FrameTimes) writer.Write((float)t);
    }

    public static PerformanceSession Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadUInt32() != Magic) throw new InvalidDataException("Not a Focus session file.");
        int metaLength = reader.ReadInt32();
        if (metaLength is < 0 or > 8 * 1024 * 1024) throw new InvalidDataException("Corrupt session header.");
        var metaBytes = reader.ReadBytes(metaLength);
        var meta = JsonSerializer.Deserialize<SessionMetadata>(metaBytes, Options)
                   ?? throw new InvalidDataException("Corrupt session metadata.");

        int count = reader.ReadInt32();
        if (count is < 0 or > MaxFrames) throw new InvalidDataException("Corrupt session frame count.");
        var frames = new double[count];
        for (int i = 0; i < count; i++) frames[i] = reader.ReadSingle();

        return new PerformanceSession
        {
            Id = meta.Id ?? Guid.NewGuid().ToString("N"),
            Name = meta.Name ?? "Session",
            ProcessName = meta.ProcessName,
            ProcessId = meta.ProcessId,
            ProfileId = meta.ProfileId,
            StartedAt = meta.StartedAt,
            EndedAt = meta.EndedAt,
            Stats = meta.Stats ?? new SessionStats(),
            FrameTimes = frames,
        };
    }

    public static PerformanceSession LoadMetadataOnly(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt32() != Magic) throw new InvalidDataException();
            int metaLength = reader.ReadInt32();
            if (metaLength is < 0 or > 8 * 1024 * 1024) throw new InvalidDataException();
            var meta = JsonSerializer.Deserialize<SessionMetadata>(reader.ReadBytes(metaLength), Options);
            return new PerformanceSession
            {
                Id = meta?.Id ?? Guid.NewGuid().ToString("N"),
                Name = meta?.Name ?? "Session",
                ProcessName = meta?.ProcessName,
                ProcessId = meta?.ProcessId ?? 0,
                ProfileId = meta?.ProfileId,
                StartedAt = meta?.StartedAt ?? DateTimeOffset.Now,
                EndedAt = meta?.EndedAt ?? DateTimeOffset.Now,
                Stats = meta?.Stats ?? new SessionStats(),
                FrameTimes = Array.Empty<double>(),
            };
        }
        catch
        {
            return new PerformanceSession { Name = Path.GetFileNameWithoutExtension(path) };
        }
    }

    public static void ExportCsv(PerformanceSession session, string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("frame,time_seconds,frametime_ms,fps");
        double t = 0;
        for (int i = 0; i < session.FrameTimes.Length; i++)
        {
            double ft = session.FrameTimes[i];
            t += ft;
            double fps = ft > 0 ? 1000.0 / ft : 0;
            writer.Write(i);
            writer.Write(',');
            writer.Write((t / 1000.0).ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(ft.ToString("F4", System.Globalization.CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.WriteLine(fps.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private sealed class SessionMetadata
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? ProcessName { get; set; }
        public int ProcessId { get; set; }
        public string? ProfileId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset EndedAt { get; set; }
        public SessionStats? Stats { get; set; }
        public int FrameCount { get; set; }
    }
}
