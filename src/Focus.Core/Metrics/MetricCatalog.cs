namespace Focus.Core.Metrics;

public static class MetricCatalog
{
    private static readonly Dictionary<MetricId, MetricDefinition> Map = Build();

    public static IReadOnlyCollection<MetricDefinition> All => Map.Values;

    public static MetricDefinition Get(MetricId id) =>
        Map.TryGetValue(id, out var def) ? def : new MetricDefinition(id, MetricCategory.System, MetricUnit.None, 0, false, 999);

    public static bool IsKnown(MetricId id) => Map.ContainsKey(id);

    public static IEnumerable<MetricDefinition> InCategory(MetricCategory category) =>
        Map.Values.Where(d => d.Category == category).OrderBy(d => d.SortOrder);

    /// <summary>Short English label used as a fallback and as the localization key.</summary>
    public static string Key(MetricId id) => "metric." + id;

    private static Dictionary<MetricId, MetricDefinition> Build()
    {
        var list = new List<MetricDefinition>
        {
            new(MetricId.Fps, MetricCategory.Game, MetricUnit.Fps, 0, true, 1),
            new(MetricId.FpsAverage, MetricCategory.Game, MetricUnit.Fps, 1, false, 2),
            new(MetricId.FpsOnePercentLow, MetricCategory.Game, MetricUnit.Fps, 1, false, 3),
            new(MetricId.FpsPointOnePercentLow, MetricCategory.Game, MetricUnit.Fps, 1, false, 4),
            new(MetricId.FrameTime, MetricCategory.Game, MetricUnit.Milliseconds, 2, true, 5),
            new(MetricId.FrameTimeMax, MetricCategory.Game, MetricUnit.Milliseconds, 2, false, 6),
            new(MetricId.FrameCount, MetricCategory.Game, MetricUnit.Frames, 0, false, 7),
            new(MetricId.SessionTime, MetricCategory.Game, MetricUnit.Seconds, 0, false, 8),

            new(MetricId.GpuUsage, MetricCategory.Gpu, MetricUnit.Percent, 0, true, 20),
            new(MetricId.GpuD3DUsage, MetricCategory.Gpu, MetricUnit.Percent, 0, true, 21),
            new(MetricId.GpuTemperature, MetricCategory.Gpu, MetricUnit.Celsius, 0, true, 22),
            new(MetricId.GpuHotSpot, MetricCategory.Gpu, MetricUnit.Celsius, 0, true, 23),
            new(MetricId.GpuCoreClock, MetricCategory.Gpu, MetricUnit.Megahertz, 0, true, 24),
            new(MetricId.GpuMemoryClock, MetricCategory.Gpu, MetricUnit.Megahertz, 0, true, 25),
            new(MetricId.GpuMemoryUsed, MetricCategory.Gpu, MetricUnit.Megabytes, 0, true, 26),
            new(MetricId.GpuMemoryTotal, MetricCategory.Gpu, MetricUnit.Megabytes, 0, false, 27),
            new(MetricId.GpuMemoryPercent, MetricCategory.Gpu, MetricUnit.Percent, 0, true, 28),
            new(MetricId.GpuPower, MetricCategory.Gpu, MetricUnit.Watts, 1, true, 29),
            new(MetricId.GpuFanSpeed, MetricCategory.Gpu, MetricUnit.RevolutionsPerMinute, 0, true, 30),

            new(MetricId.CpuUsage, MetricCategory.Cpu, MetricUnit.Percent, 0, true, 40),
            new(MetricId.CpuTemperature, MetricCategory.Cpu, MetricUnit.Celsius, 0, true, 41),
            new(MetricId.CpuPower, MetricCategory.Cpu, MetricUnit.Watts, 1, true, 42),
            new(MetricId.CpuClock, MetricCategory.Cpu, MetricUnit.Megahertz, 0, true, 43),
            new(MetricId.CpuMaxCore, MetricCategory.Cpu, MetricUnit.Percent, 0, true, 44),
            new(MetricId.CpuCoreUsage, MetricCategory.Cpu, MetricUnit.Percent, 0, true, 45),

            new(MetricId.RamUsed, MetricCategory.System, MetricUnit.Megabytes, 0, true, 60),
            new(MetricId.RamTotal, MetricCategory.System, MetricUnit.Megabytes, 0, false, 61),
            new(MetricId.RamPercent, MetricCategory.System, MetricUnit.Percent, 0, true, 62),
            new(MetricId.ProcessMemory, MetricCategory.System, MetricUnit.Megabytes, 0, true, 63),
            new(MetricId.ProcessWorkingSet, MetricCategory.System, MetricUnit.Megabytes, 0, true, 64),
        };

        return list.ToDictionary(d => d.Id);
    }
}
