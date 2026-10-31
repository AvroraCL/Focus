namespace Focus.Core.Metrics;

/// <summary>Immutable view of everything the HUD and dashboard can render at a point in time.</summary>
public sealed class MetricsSnapshot
{
    private readonly Dictionary<MetricId, double> _values;
    private readonly double[] _coreUsage;

    public MetricsSnapshot(
        long sequence,
        DateTimeOffset timestamp,
        int targetPid,
        string targetName,
        Dictionary<MetricId, double> values,
        double[] coreUsage,
        bool capturing)
    {
        Sequence = sequence;
        Timestamp = timestamp;
        TargetPid = targetPid;
        TargetName = targetName;
        _values = values;
        _coreUsage = coreUsage;
        Capturing = capturing;
    }

    public long Sequence { get; }
    public DateTimeOffset Timestamp { get; }
    public int TargetPid { get; }
    public string TargetName { get; }
    public bool Capturing { get; }
    public IReadOnlyList<double> CoreUsage => _coreUsage;

    public bool TryGet(MetricId id, out double value) => _values.TryGetValue(id, out value);

    public double? Get(MetricId id) => _values.TryGetValue(id, out var v) ? v : null;

    public IReadOnlyDictionary<MetricId, double> Values => _values;

    public static MetricsSnapshot Empty(long sequence = 0) => new(
        sequence,
        DateTimeOffset.Now,
        0,
        string.Empty,
        new Dictionary<MetricId, double>(),
        Array.Empty<double>(),
        false);
}
