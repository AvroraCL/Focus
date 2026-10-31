using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;

namespace Focus.Telemetry.Hardware;

public sealed class HardwareSnapshot
{
    public string? CpuName { get; init; }
    public string? GpuName { get; init; }
    public bool Available { get; init; }

    public float? CpuUsage { get; init; }
    public float? CpuMaxCore { get; init; }
    public float? CpuTemperature { get; init; }
    public float? CpuPower { get; init; }
    public float? CpuClock { get; init; }
    public float[] CoreUsage { get; init; } = Array.Empty<float>();

    public float? GpuUsage { get; init; }
    public float? GpuD3DUsage { get; init; }
    public float? GpuTemperature { get; init; }
    public float? GpuHotSpot { get; init; }
    public float? GpuCoreClock { get; init; }
    public float? GpuMemoryClock { get; init; }
    public float? GpuMemoryUsedMb { get; init; }
    public float? GpuMemoryTotalMb { get; init; }
    public float? VramPercent { get; init; }
    public float? GpuPower { get; init; }
    public float? GpuFanRpm { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;

    public static HardwareSnapshot Empty { get; } = new();
}

/// <summary>
/// Wraps LibreHardwareMonitor. A background thread refreshes sensors on an interval and publishes an
/// immutable snapshot so the UI and HUD never block on hardware I/O.
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly object _gate = new();
    private Computer? _computer;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private volatile HardwareSnapshot _snapshot = HardwareSnapshot.Empty;
    private int _intervalMs = 500;

    public HardwareSnapshot Current => _snapshot;
    public string? LastError { get; private set; }
    public bool IsRunning => _thread is { IsAlive: true };

    public int IntervalMs
    {
        get => _intervalMs;
        set => _intervalMs = Math.Clamp(value, 200, 5000);
    }

    public bool Start()
    {
        lock (_gate)
        {
            if (IsRunning) return true;
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = false,
                    IsStorageEnabled = false,
                    IsNetworkEnabled = false,
                };
                _computer.Open();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _computer = null;
                return false;
            }

            _cts = new CancellationTokenSource();
            _thread = new Thread(() => Loop(_cts.Token))
            {
                IsBackground = true,
                Name = "Focus-Hardware",
                Priority = ThreadPriority.BelowNormal,
            };
            _thread.Start();
            return true;
        }
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                UpdateOnce();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }

            // Sleep in slices so shutdown is instant.
            int slept = 0;
            while (slept < _intervalMs && !token.IsCancellationRequested)
            {
                Thread.Sleep(Math.Min(100, _intervalMs - slept));
                slept += 100;
            }
        }
    }

    private void Update()
    {
        if (_computer is null) return;
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Update();
        }
    }

    private static readonly Regex CoreRegex = new(@"CPU Core #(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void UpdateOnce()
    {
        if (_computer is null) return;
        Update();

        IHardware? cpu = null;
        IHardware? gpu = null;
        foreach (var hardware in _computer.Hardware)
        {
            if (cpu is null && hardware.HardwareType == HardwareType.Cpu) cpu = hardware;
            if (gpu is null && hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) gpu = hardware;
            else if (gpu is not null && hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd &&
                     gpu.HardwareType == HardwareType.GpuIntel)
            {
                gpu = hardware; // prefer a discrete GPU over the integrated one
            }
        }

        float[] coreUsage = Array.Empty<float>();
        float? cpuUsage = null, cpuTemp = null, cpuPower = null, cpuClock = null, cpuMax = null;
        if (cpu is not null)
        {
            var sensors = EnumerateSensors(cpu);

            cpuUsage = Pick(sensors, SensorType.Load, "CPU Total", "Total CPU Load");
            cpuMax = Pick(sensors, SensorType.Load, "CPU Core Max", "CPU Max");
            cpuTemp = Pick(sensors, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core Average", "CPU Core");
            cpuPower = Pick(sensors, SensorType.Power, "CPU Package", "CPU Cores", "Package");
            if (cpuPower is <= 0.5f) cpuPower = null;

            var clocks = sensors.Where(s => s.SensorType == SensorType.Clock && s.Value is > 0 &&
                                            s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) &&
                                            !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase)).ToList();
            if (clocks.Count > 0) cpuClock = clocks.Average(s => s.Value ?? 0);

            var cores = new SortedDictionary<int, List<float>>();
            foreach (var sensor in sensors)
            {
                if (sensor.SensorType != SensorType.Load) continue;
                var match = CoreRegex.Match(sensor.Name);
                if (!match.Success) continue;
                if (!sensor.Value.HasValue) continue;
                int index = int.Parse(match.Groups[1].Value);
                if (!cores.TryGetValue(index, out var list)) cores[index] = list = new List<float>();
                list.Add(sensor.Value.Value);
            }
            if (cores.Count > 0) coreUsage = cores.Values.Select(v => v.Average()).ToArray();

            if (cpuUsage is null && coreUsage.Length > 0) cpuUsage = coreUsage.Average();
            if (cpuMax is null && coreUsage.Length > 0) cpuMax = coreUsage.Max();
        }

        float? gpuUsage = null, gpuD3D = null, gpuTemp = null, gpuHotSpot = null, gpuClock = null, gpuMemClock = null;
        float? vramUsed = null, vramTotal = null, gpuPower = null, gpuFan = null;
        if (gpu is not null)
        {
            var sensors = EnumerateSensors(gpu);
            gpuUsage = Pick(sensors, SensorType.Load, "GPU Core", "D3D 3D");
            gpuD3D = Pick(sensors, SensorType.Load, "D3D 3D");
            gpuTemp = Pick(sensors, SensorType.Temperature, "GPU Core");
            gpuHotSpot = Pick(sensors, SensorType.Temperature, "GPU Hot Spot");
            gpuClock = Pick(sensors, SensorType.Clock, "GPU Core");
            gpuMemClock = Pick(sensors, SensorType.Clock, "GPU Memory");
            vramUsed = Pick(sensors, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            vramTotal = Pick(sensors, SensorType.SmallData, "GPU Memory Total");
            gpuPower = Pick(sensors, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power");
            gpuFan = Pick(sensors, SensorType.Fan, "GPU Fan");
            if (gpuFan is <= 0) gpuFan = null;
        }

        float? vramPercent = null;
        if (vramUsed.HasValue && vramTotal is > 0) vramPercent = vramUsed.Value / vramTotal.Value * 100f;

        _snapshot = new HardwareSnapshot
        {
            Available = cpu is not null || gpu is not null,
            CpuName = cpu?.Name,
            GpuName = gpu?.Name,
            CpuUsage = cpuUsage,
            CpuMaxCore = cpuMax,
            CpuTemperature = cpuTemp,
            CpuPower = cpuPower,
            CpuClock = cpuClock,
            CoreUsage = coreUsage,
            GpuUsage = gpuUsage,
            GpuD3DUsage = gpuD3D,
            GpuTemperature = gpuTemp,
            GpuHotSpot = gpuHotSpot,
            GpuCoreClock = gpuClock,
            GpuMemoryClock = gpuMemClock,
            GpuMemoryUsedMb = vramUsed,
            GpuMemoryTotalMb = vramTotal,
            GpuPower = gpuPower,
            GpuFanRpm = gpuFan,
            UpdatedAt = DateTimeOffset.Now,
            VramPercent = vramPercent,
        };
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors) yield return sensor;
        foreach (var sub in hardware.SubHardware)
            foreach (var sensor in sub.Sensors)
                yield return sensor;
    }

    private static float? Pick(IEnumerable<ISensor> sensors, SensorType type, params string[] names)
    {
        var candidates = new List<ISensor>();
        foreach (var sensor in sensors)
        {
            if (sensor.SensorType != type || sensor.Value is null) continue;
            candidates.Add(sensor);
        }
        if (candidates.Count == 0) return null;

        foreach (var name in names)
        {
            var exact = candidates.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.Value;
            var partial = candidates.FirstOrDefault(s => s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (partial is not null) return partial.Value;
        }
        return candidates[0].Value;
    }

    public void Stop()
    {
        lock (_gate)
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
                // ignore
            }
            try
            {
                _thread?.Join(1500);
            }
            catch
            {
                // ignore
            }
            _thread = null;
            _cts?.Dispose();
            _cts = null;
            try
            {
                _computer?.Close();
            }
            catch
            {
                // ignore
            }
            _computer = null;
            _snapshot = HardwareSnapshot.Empty;
        }
    }

    public void Dispose() => Stop();
}
