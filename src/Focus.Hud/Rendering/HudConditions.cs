using Focus.Core.Metrics;
using Focus.Core.Profiles;

namespace Focus.Hud.Rendering;

public static class HudConditions
{
    public static double? GetValue(IHudDataSource data, MetricId metric, int coreIndex)
    {
        if (metric == MetricId.CpuCoreUsage && coreIndex >= 0 && coreIndex < data.CoreUsage.Count)
            return data.CoreUsage[coreIndex];
        return data.Get(metric, coreIndex);
    }

    /// <summary>First matching rule for a color target wins; otherwise the element color is used.</summary>
    public static string Resolve(HudElement element, ColorTarget target, IHudDataSource data, string fallback)
    {
        foreach (var rule in element.Conditions)
        {
            if (rule.Target != target) continue;
            var metric = rule.Metric == MetricId.None ? element.Metric : rule.Metric;
            int coreIndex = rule.CoreIndex >= 0 ? rule.CoreIndex : element.CoreIndex;
            double? value = GetValue(data, metric, coreIndex);
            if (value is null) continue;

            bool match = rule.Operator switch
            {
                ComparisonOperator.Greater => value > rule.Threshold,
                ComparisonOperator.GreaterOrEqual => value >= rule.Threshold,
                ComparisonOperator.Less => value < rule.Threshold,
                ComparisonOperator.LessOrEqual => value <= rule.Threshold,
                _ => false,
            };
            if (match) return rule.Color;
        }
        return fallback;
    }
}
