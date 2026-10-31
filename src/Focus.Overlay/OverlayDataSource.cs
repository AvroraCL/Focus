using Focus.Core.Ipc;
using Focus.Core.Metrics;
using Focus.Hud;

namespace Focus.Overlay;

/// <summary>Adapter that exposes the last overlay state payload through <see cref="IHudDataSource"/>.</summary>
public sealed class OverlayDataSource : IHudDataSource
{
    private OverlayState _state = new();

    public void Update(OverlayState state) => _state = state;

    public double? Get(MetricId metric, int coreIndex = -1)
    {
        if (metric == MetricId.CpuCoreUsage && coreIndex >= 0)
        {
            return coreIndex < _state.CoreUsage.Length ? _state.CoreUsage[coreIndex] : null;
        }
        return _state.Values.TryGetValue(metric.ToString(), out double value) ? value : null;
    }

    public float[] GetHistory(MetricId metric, int windowSeconds)
    {
        if (!_state.History.TryGetValue(metric.ToString(), out var history)) return Array.Empty<float>();
        int wanted = Math.Max(8, windowSeconds * 20);
        if (history.Length <= wanted) return history;
        return history[^wanted..];
    }

    public IReadOnlyList<double> CoreUsage => _state.CoreUsage;

    public string TargetName => _state.TargetName;

    public bool Capturing => _state.Capturing;
}
