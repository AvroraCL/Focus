using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Focus.Core.Profiles;

namespace Focus.Core.Ipc;

/// <summary>
/// Everything the overlay process needs for one rendered frame. Sent as JSON through shared memory.
/// </summary>
public sealed class OverlayState
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public long Sequence { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public int TargetPid { get; set; }
    public string TargetName { get; set; } = string.Empty;
    public bool Capturing { get; set; }
    public bool OnlyWhenFocused { get; set; }
    public long FrameCount { get; set; }
    public double SessionSeconds { get; set; }

    public string? ProfileJson { get; set; }
    public int ProfileRevision { get; set; }
    public bool Visible { get; set; } = true;

    /// <summary>MetricId name → value.</summary>
    public Dictionary<string, double> Values { get; set; } = new();

    public double[] CoreUsage { get; set; } = Array.Empty<double>();

    /// <summary>MetricId name → recent samples (oldest first, one per UI tick).</summary>
    public Dictionary<string, float[]> History { get; set; } = new();
}

public static class OverlayStateCodec
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize(OverlayState state) => JsonSerializer.SerializeToUtf8Bytes(state, Options);

    public static OverlayState? Deserialize(ReadOnlySpan<byte> data) =>
        JsonSerializer.Deserialize<OverlayState>(data, Options);

    public static HudProfile? ParseProfile(OverlayState state) =>
        string.IsNullOrWhiteSpace(state.ProfileJson) ? null : ProfileCodec.Parse(state.ProfileJson);
}

/// <summary>
/// Single-producer / single-consumer shared memory channel with a double buffer and a version counter.
/// The overlay polls the version cheaply and only parses when it changes.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class SharedStateChannel : IDisposable
{
    public static readonly string MapName = "Focus.Overlay.State." + FocusInstance.Suffix;
    public static readonly string ExitEventName = "Focus.Overlay.Exit." + FocusInstance.Suffix;

    private const int HeaderSize = 64;
    private const int Capacity = 4 * 1024 * 1024;
    private const int SlotSize = (Capacity - HeaderSize) / 2;
    private const int Magic = 0x53434F46; // "FOCS"
    private const int VersionOffset = 16;

    private readonly Mutex _access = new(false, MapName + ".Access");
    private readonly MemoryMappedFile _map;
    private readonly MemoryMappedViewAccessor _view;
    private byte* _pointer;
    private long _lastVersion = -1;
    private bool _disposed;

    private SharedStateChannel(bool writable)
    {
        IsWriter = writable;
        _map = writable
            ? MemoryMappedFile.CreateOrOpen(MapName, Capacity)
            : MemoryMappedFile.CreateOrOpen(MapName, Capacity);
        _view = _map.CreateViewAccessor(0, Capacity);
        byte* ptr = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _pointer = ptr + _view.PointerOffset;

        if (IsWriter)
        {
            Enter(Timeout.Infinite);
            try
            {
                *(int*)(_pointer + 0) = Magic;
                *(int*)(_pointer + 4) = 0;
                *(int*)(_pointer + 8) = 0;
                *(long*)(_pointer + VersionOffset) = 0;
            }
            finally { _access.ReleaseMutex(); }
        }
    }

    public bool IsWriter { get; }

    public long PublishedVersion => Volatile.Read(ref *(long*)(_pointer + VersionOffset));

    /// <summary>Creates/attaches shared memory for writing. Requires the app process.</summary>
    public static SharedStateChannel CreateWriter() => new(true);

    /// <summary>Attaches to the shared memory written by the app process.</summary>
    public static SharedStateChannel OpenReader() => new(false);

    public bool HasWriter
    {
        get
        {
            if (IsWriter) return true;
            return *(int*)_pointer == Magic;
        }
    }

    public void Write(ReadOnlySpan<byte> payload)
    {
        if (!IsWriter) throw new InvalidOperationException("Channel is read-only.");
        if (payload.Length > SlotSize) throw new ArgumentException("Payload too large for shared state slot.");

        if (!Enter(100)) throw new TimeoutException("Overlay channel is busy.");
        try
        {
            int currentSlot = *(int*)(_pointer + 4);
            int nextSlot = 1 - currentSlot;
            byte* target = _pointer + HeaderSize + nextSlot * SlotSize;
            payload.CopyTo(new Span<byte>(target, payload.Length));
            *(int*)(_pointer + 8) = payload.Length;
            *(int*)(_pointer + 4) = nextSlot;
            Interlocked.Increment(ref *(long*)(_pointer + VersionOffset));
        }
        finally { _access.ReleaseMutex(); }
    }

    /// <summary>Reads the newest payload if it changed since the last call.</summary>
    public bool TryReadNewest(out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (!Enter(0)) return false;
        try
        {
            long version = Volatile.Read(ref *(long*)(_pointer + VersionOffset));
            if (version == _lastVersion || version == 0)
            {
                payload = Array.Empty<byte>();
                return false;
            }

            int slot = *(int*)(_pointer + 4);
            int length = *(int*)(_pointer + 8);
            if (length is <= 0 or > SlotSize)
            {
                payload = Array.Empty<byte>();
                return false;
            }

            payload = new byte[length];
            new ReadOnlySpan<byte>(_pointer + HeaderSize + slot * SlotSize, length).CopyTo(payload);

            // Retry if the writer moved to the other slot while we were copying.
            if (Volatile.Read(ref *(long*)(_pointer + VersionOffset)) != version) return false;
            _lastVersion = version;
            return true;
        }
        finally { _access.ReleaseMutex(); }
    }

    private bool Enter(int milliseconds)
    {
        try { return _access.WaitOne(milliseconds); }
        catch (AbandonedMutexException) { return true; }
    }

    public EventWaitHandle OpenOrCreateExitEvent() =>
        IsWriter
            ? new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName)
            : EventWaitHandle.OpenExisting(ExitEventName);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pointer != null)
        {
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }
        _view.Dispose();
        _map.Dispose();
        _access.Dispose();
    }
}
