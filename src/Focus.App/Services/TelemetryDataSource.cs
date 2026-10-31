using Focus.Core.Metrics;
using Focus.Hud;
using Focus.Telemetry;

namespace Focus.App.Services;

/// <summary>Exposes live telemetry to the shared HUD renderer.</summary>
public sealed class TelemetryDataSource : IHudDataSource
{
    private readonly TelemetryHub _hub;

    public TelemetryDataSource(TelemetryHub hub) => _hub = hub;

    public double? Get(MetricId metric, int coreIndex = -1)
    {
        if (metric == MetricId.CpuCoreUsage && coreIndex >= 0)
        {
            var cores = _hub.Current.CoreUsage;
            return coreIndex < cores.Count ? cores[coreIndex] : null;
        }
        return _hub.Current.Get(metric);
    }

    public float[] GetHistory(MetricId metric, int windowSeconds) => _hub.GetHistory(metric, windowSeconds);

    public IReadOnlyList<double> CoreUsage => _hub.Current.CoreUsage;

    public string TargetName => _hub.TargetName;

    public bool Capturing => _hub.Capturing;
}
