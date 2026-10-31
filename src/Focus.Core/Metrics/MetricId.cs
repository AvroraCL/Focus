namespace Focus.Core.Metrics;

/// <summary>
/// Every metric Focus can display. Values are the stable identifiers used in profiles and IPC.
/// </summary>
public enum MetricId
{
    None = 0,

    // Frame / game
    Fps,
    FpsAverage,
    FpsOnePercentLow,
    FpsPointOnePercentLow,
    FrameTime,
    FrameTimeMax,
    FrameCount,
    SessionTime,

    // GPU
    GpuUsage,
    GpuD3DUsage,
    GpuTemperature,
    GpuHotSpot,
    GpuCoreClock,
    GpuMemoryClock,
    GpuMemoryUsed,
    GpuMemoryTotal,
    GpuMemoryPercent,
    GpuPower,
    GpuFanSpeed,

    // CPU
    CpuUsage,
    CpuTemperature,
    CpuPower,
    CpuClock,
    CpuMaxCore,
    CpuCoreUsage,

    // System
    RamUsed,
    RamTotal,
    RamPercent,
    ProcessMemory,
    ProcessWorkingSet,
}

public enum MetricUnit
{
    None,
    Fps,
    Milliseconds,
    Percent,
    Celsius,
    Megahertz,
    Watts,
    RevolutionsPerMinute,
    Megabytes,
    Frames,
    Seconds,
    Count,
}

public enum MetricCategory
{
    Game,
    Gpu,
    Cpu,
    System,
}

public sealed record MetricDefinition(
    MetricId Id,
    MetricCategory Category,
    MetricUnit Unit,
    int DefaultDecimals,
    bool Graphable,
    int SortOrder);
