using Focus.Core.Metrics;

namespace Focus.Hud;

/// <summary>Read-only metric access for anything that draws a HUD.</summary>
public interface IHudDataSource
{
    /// <summary>Latest value, or null when the metric is unavailable right now.</summary>
    double? Get(MetricId metric, int coreIndex = -1);

    /// <summary>Recent samples (oldest first) for graph elements.</summary>
    float[] GetHistory(MetricId metric, int windowSeconds);

    IReadOnlyList<double> CoreUsage { get; }

    string TargetName { get; }

    bool Capturing { get; }
}

public sealed class EmptyHudDataSource : IHudDataSource
{
    public static readonly EmptyHudDataSource Instance = new();

    public double? Get(MetricId metric, int coreIndex = -1) => null;

    public float[] GetHistory(MetricId metric, int windowSeconds) => Array.Empty<float>();

    public IReadOnlyList<double> CoreUsage => Array.Empty<double>();

    public string TargetName => string.Empty;

    public bool Capturing => false;
}
