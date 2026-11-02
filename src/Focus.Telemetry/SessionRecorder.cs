using Focus.Core.Sessions;
using Focus.Telemetry.Hardware;

namespace Focus.Telemetry;

/// <summary>Collects raw frame times for a performance test session. Never estimates missing data.</summary>
public sealed class SessionRecorder
{
    private readonly object _gate = new();
    private readonly List<double> _frames = new();
    private double _gpuSum, _cpuSum;
    private double? _gpuTempPeak, _gpuPowerPeak, _cpuPowerPeak;
    private int _gpuSamples, _cpuSamples;
    private volatile bool _recording;

    public bool IsRecording { get => _recording; private set => _recording = value; }
    public DateTimeOffset StartedAt { get; private set; }
    public int FrameCount { get { lock (_gate) return _frames.Count; } }

    public void Start()
    {
        lock (_gate)
        {
            _frames.Clear();
            _gpuSum = _cpuSum = 0;
            _gpuTempPeak = _gpuPowerPeak = _cpuPowerPeak = null;
            _gpuSamples = _cpuSamples = 0;
            StartedAt = DateTimeOffset.Now;
            IsRecording = true;
        }
    }

    public void AddFrame(double deltaMs)
    {
        if (!IsRecording) return;
        if (!double.IsFinite(deltaMs) || deltaMs <= 0) return;
        lock (_gate)
        {
            if (!IsRecording) return;
            if (_frames.Count < 8_000_000) _frames.Add(deltaMs);
        }
    }

    public void ObserveHardware(HardwareSnapshot snapshot)
    {
        if (!IsRecording) return;
        lock (_gate)
        {
            if (!IsRecording) return;
            if (snapshot.GpuUsage is { } gpu && float.IsFinite(gpu)) { _gpuSum += gpu; _gpuSamples++; }
            if (snapshot.CpuUsage is { } cpu && float.IsFinite(cpu)) { _cpuSum += cpu; _cpuSamples++; }
            if (snapshot.GpuTemperature is { } t && float.IsFinite(t)) _gpuTempPeak = Math.Max(_gpuTempPeak ?? t, t);
            if (snapshot.GpuPower is { } gp && float.IsFinite(gp)) _gpuPowerPeak = Math.Max(_gpuPowerPeak ?? gp, gp);
            if (snapshot.CpuPower is { } cp && float.IsFinite(cp)) _cpuPowerPeak = Math.Max(_cpuPowerPeak ?? cp, cp);
        }
    }

    public PerformanceSession Stop(string name, string? processName, int processId, string? profileId)
    {
        double[] frames;
        SessionStats stats;
        double? gpuAvg, cpuAvg, gpuTemp, gpuPower, cpuPower;
        DateTimeOffset started;

        lock (_gate)
        {
            IsRecording = false;
            frames = _frames.ToArray();
            started = StartedAt;
            stats = SessionCalculator.Compute(frames);
            gpuAvg = _gpuSamples > 0 ? _gpuSum / _gpuSamples : null;
            cpuAvg = _cpuSamples > 0 ? _cpuSum / _cpuSamples : null;
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
