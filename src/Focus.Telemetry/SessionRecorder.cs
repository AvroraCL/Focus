using Focus.Core.Sessions;
using Focus.Telemetry.Hardware;

namespace Focus.Telemetry;

/// <summary>Collects raw frame times for a performance test session. Never estimates missing data.</summary>
public sealed class SessionRecorder
{
    private readonly object _gate = new();
    private readonly List<double> _frames = new();
    private double _gpuSum, _cpuSum, _gpuTempPeak, _gpuPowerPeak, _cpuPowerPeak;
    private int _hardwareSamples;

    public bool IsRecording { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public int FrameCount { get { lock (_gate) return _frames.Count; } }

    public void Start()
    {
        lock (_gate)
        {
            _frames.Clear();
            _gpuSum = _cpuSum = _gpuTempPeak = _gpuPowerPeak = _cpuPowerPeak = 0;
            _hardwareSamples = 0;
            StartedAt = DateTimeOffset.Now;
            IsRecording = true;
        }
    }

    public void AddFrame(double deltaMs)
    {
        if (!IsRecording) return;
        if (deltaMs <= 0 || deltaMs > 5000) return;
        lock (_gate)
        {
            if (_frames.Count < 8_000_000) _frames.Add(deltaMs);
        }
    }

    public void ObserveHardware(HardwareSnapshot snapshot)
    {
        if (!IsRecording) return;
        lock (_gate)
        {
            _gpuSum += snapshot.GpuUsage ?? 0;
            _cpuSum += snapshot.CpuUsage ?? 0;
            _hardwareSamples++;
            if (snapshot.GpuTemperature is { } t && t > _gpuTempPeak) _gpuTempPeak = t;
            if (snapshot.GpuPower is { } gp && gp > _gpuPowerPeak) _gpuPowerPeak = gp;
            if (snapshot.CpuPower is { } cp && cp > _cpuPowerPeak) _cpuPowerPeak = cp;
        }
    }

    public PerformanceSession Stop(string name, string? processName, int processId, string? profileId)
    {
        double[] frames;
        SessionStats stats;
        double gpuAvg, cpuAvg, gpuTemp, gpuPower, cpuPower;
        DateTimeOffset started;

        lock (_gate)
        {
            IsRecording = false;
            frames = _frames.ToArray();
            started = StartedAt;
            stats = SessionCalculator.Compute(frames);
            gpuAvg = _hardwareSamples > 0 ? _gpuSum / _hardwareSamples : 0;
            cpuAvg = _hardwareSamples > 0 ? _cpuSum / _hardwareSamples : 0;
            gpuTemp = _gpuTempPeak;
            gpuPower = _gpuPowerPeak;
            cpuPower = _cpuPowerPeak;
            _frames.Clear();
        }

        stats.AverageGpuUsage = gpuAvg;
        stats.AverageCpuUsage = cpuAvg;
        stats.PeakGpuTemperature = gpuTemp;
        stats.PeakGpuPower = gpuPower;
        stats.PeakCpuPower = cpuPower;

        return new PerformanceSession
        {
            Name = name,
            ProcessName = processName,
            ProcessId = processId,
            ProfileId = profileId,
            StartedAt = started,
            EndedAt = DateTimeOffset.Now,
            Stats = stats,
            FrameTimes = frames,
        };
    }
}
