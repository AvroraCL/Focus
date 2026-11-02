using System.Collections.Concurrent;
using System.Diagnostics;
using Focus.Core.Metrics;
using Focus.Telemetry.Frames;
using Focus.Telemetry.Hardware;
using Focus.Telemetry.System;

namespace Focus.Telemetry;

/// <summary>
/// Owns every data source and turns them into immutable <see cref="MetricsSnapshot"/> values at a
/// fixed cadence. All heavy work happens on background threads.
/// </summary>
public sealed class TelemetryHub : IDisposable
{
    private const int TickMs = 50;
    private const int FrameCapacity = 60_000;

    private readonly object _gate = new();
    private readonly Dictionary<MetricId, FloatHistory> _slowHistory = new();
    private readonly FloatHistory _fpsFast = new(2048);
    private readonly FloatHistory _frameTimeFast = new(2048);

    private Thread? _tickThread;
    private CancellationTokenSource? _cts;
    private int _ticks;
    private float _latestFrameTime;
    private FrameStats _stats = new();
    private DateTimeOffset _statsComputedAt;
    private double _lastPresentMs = -1;
    private DateTime _lastPresentWall = DateTime.UtcNow;
    private long _sequence;
    private int _targetPid;
    private readonly ConcurrentDictionary<int, int> _presentCounts = new();
    private Dictionary<int, int> _presentRates = new();
    private string _targetName = string.Empty;
    private string? _targetExe;

    public TelemetryHub()
    {
        Capture.Present += OnPresent;
    }

    public FrameCaptureService Capture { get; } = new();
    public HardwareMonitorService Hardware { get; } = new();
    public SystemMetricsService System { get; } = new();
    public FrameTimeline Timeline { get; } = new(FrameCapacity);
    public SessionRecorder Recorder { get; } = new();

    public MetricsSnapshot Current { get; private set; } = MetricsSnapshot.Empty();

    public int TargetPid { get { lock (_gate) return _targetPid; } }
    public string TargetName { get { lock (_gate) return _targetName; } }
    public string? TargetExecutable { get { lock (_gate) return _targetExe; } }
    public bool HasTarget => TargetPid > 0;
    public bool Capturing => TargetPid > 0 && Capture.IsRunning;

    public event Action<int, string>? TargetLost;
    public event Action? CaptureStateChanged;

    public int HardwareIntervalMs
    {
        get => Hardware.IntervalMs;
        set => Hardware.IntervalMs = value;
    }

    public bool Start()
    {
        Hardware.Start();
        bool etw = Capture.Start();
        CaptureStateChanged?.Invoke();

        _cts = new CancellationTokenSource();
        _tickThread = new Thread(() => TickLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "Focus-Telemetry",
            Priority = ThreadPriority.BelowNormal,
        };
        _tickThread.Start();
        return etw;
    }

    /// <summary>Processes with the highest present rate in the last second (for diagnostics).</summary>
    public IReadOnlyList<(int Pid, int Rate)> GetTopPresenters(int count = 5)
    {
        var rates = _presentRates;
        return rates.OrderByDescending(kv => kv.Value).Take(count).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    public string DiagnosticsString()
    {
        var top = string.Join(", ", GetTopPresenters().Select(t => $"{t.Pid}:{t.Rate}"));
        return $"etw={Capture.IsRunning} events={Capture.EventsSeen} presents={Capture.PresentsSeen} " +
               $"target={TargetPid} rate={GetPresentRate(TargetPid)} top=[{top}]";
    }

    /// <summary>Presents per second for any process, measured over the last one second window.</summary>
    public int GetPresentRate(int pid)
    {
        var rates = _presentRates;
        return rates.TryGetValue(pid, out int value) ? value : 0;
    }

    public void SetTarget(int pid, string name, string? executablePath)
    {
        lock (_gate)
        {
            if (_targetPid == pid) return;
            _targetPid = pid;
            _targetName = name;
            _targetExe = executablePath;
            Timeline.Clear();
            _fpsFast.Clear();
            _frameTimeFast.Clear();
            foreach (var history in _slowHistory.Values) history.Clear();
            _lastPresentMs = -1;
            _latestFrameTime = 0;
            _stats = new FrameStats();
            _statsComputedAt = DateTimeOffset.MinValue;
        }
    }

    public void ClearTarget() => SetTarget(0, string.Empty, null);

    private void OnPresent(int pid, double timeMs)
    {
        lock (_gate)
        {
            _presentCounts.AddOrUpdate(pid, 1, static (_, value) => value + 1);
            if (pid != _targetPid || !double.IsFinite(timeMs) || timeMs <= _lastPresentMs) return;

            double previous = _lastPresentMs;
            _lastPresentMs = timeMs;
            _lastPresentWall = DateTime.UtcNow;
            Timeline.Add(timeMs);

            if (previous >= 0 && timeMs > previous)
            {
                double delta = timeMs - previous;
                if (double.IsFinite(delta) && delta > 0)
                {
                    Recorder.AddFrame(delta);
                    _latestFrameTime = (float)delta;
                }
            }
        }
    }


    private void TickLoop(CancellationToken token)
    {
        var sw = Stopwatch.StartNew();
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch
            {
                // Telemetry must never take the app down.
            }

            int elapsed = (int)sw.ElapsedMilliseconds;
            int wait = TickMs - (elapsed % TickMs);
            token.WaitHandle.WaitOne(wait > 0 ? wait : 1);
        }
    }

    private void Tick()
    {
        lock (_gate) TickLocked();
    }

    private void TickLocked()
    {
        _ticks++;
        int pid = TargetPid;
        string name = TargetName;

        if (_ticks % 20 == 0)
        {
            _presentRates = new Dictionary<int, int>(_presentCounts);
            _presentCounts.Clear();
        }

        if (pid > 0 && _ticks % 20 == 0 && !ProcessLocator.IsRunning(pid))
        {
            ClearTarget();
            TargetLost?.Invoke(pid, name);
            pid = 0;
        }

        double nowRef = ReferenceNowMs();
        if ((DateTimeOffset.Now - _statsComputedAt).TotalMilliseconds >= 500)
        {
            _stats = Timeline.ComputeStats(nowRef);
            _statsComputedAt = DateTimeOffset.Now;
        }

        var hardware = Hardware.Current;
        var system = System.Sample(pid);
        var values = new Dictionary<MetricId, double>(32);

        if (pid > 0)
        {
            if (Timeline.Count > 1)
            {
                values[MetricId.Fps] = _stats.CurrentFps;
                values[MetricId.FpsAverage] = _stats.AverageFps;
                values[MetricId.FpsOnePercentLow] = _stats.OnePercentLow;
                values[MetricId.FpsPointOnePercentLow] = _stats.PointOnePercentLow;
                values[MetricId.FrameTimeMax] = _stats.MaxFrameTimeMs;
                values[MetricId.SessionTime] = _stats.ElapsedSeconds;
                values[MetricId.FrameCount] = Timeline.TotalFrames;
            }
            if (_latestFrameTime is > 0 and var lastFrameTime)
            {
                values[MetricId.FrameTime] = lastFrameTime;
            }
        }

        Add(values, MetricId.GpuUsage, hardware.GpuUsage);
        Add(values, MetricId.GpuD3DUsage, hardware.GpuD3DUsage);
        Add(values, MetricId.GpuTemperature, hardware.GpuTemperature);
        Add(values, MetricId.GpuHotSpot, hardware.GpuHotSpot);
        Add(values, MetricId.GpuCoreClock, hardware.GpuCoreClock);
        Add(values, MetricId.GpuMemoryClock, hardware.GpuMemoryClock);
        Add(values, MetricId.GpuMemoryUsed, hardware.GpuMemoryUsedMb);
        Add(values, MetricId.GpuMemoryTotal, hardware.GpuMemoryTotalMb);
        Add(values, MetricId.GpuMemoryPercent, hardware.VramPercent);
        Add(values, MetricId.GpuPower, hardware.GpuPower);
        Add(values, MetricId.GpuFanSpeed, hardware.GpuFanRpm);

        Add(values, MetricId.CpuUsage, hardware.CpuUsage);
        Add(values, MetricId.CpuTemperature, hardware.CpuTemperature);
        Add(values, MetricId.CpuPower, hardware.CpuPower);
        Add(values, MetricId.CpuClock, hardware.CpuClock);
        Add(values, MetricId.CpuMaxCore, hardware.CpuMaxCore);

        if (system.TotalMemoryMb > 0)
        {
            values[MetricId.RamUsed] = system.UsedMemoryMb;
            values[MetricId.RamTotal] = system.TotalMemoryMb;
            values[MetricId.RamPercent] = system.MemoryPercent;
        }
        if (system.ProcessWorkingSetMb > 0) values[MetricId.ProcessWorkingSet] = system.ProcessWorkingSetMb;
        if (system.ProcessPrivateMb > 0) values[MetricId.ProcessMemory] = system.ProcessPrivateMb;

        var coreUsage = new double[hardware.CoreUsage.Length];
        for (int i = 0; i < coreUsage.Length; i++) coreUsage[i] = hardware.CoreUsage[i];

        var snapshot = new MetricsSnapshot(
            ++_sequence,
            DateTimeOffset.Now,
            pid,
            name,
            values,
            coreUsage,
            Capturing);

        if (values.TryGetValue(MetricId.Fps, out var fps)) _fpsFast.Add((float)fps);
        if (values.TryGetValue(MetricId.FrameTime, out var ft)) _frameTimeFast.Add((float)ft);
        Current = snapshot;
        RecordHistory(values);
        Recorder.ObserveHardware(hardware);
    }

    private void RecordHistory(Dictionary<MetricId, double> values)
    {
        foreach (var (id, value) in values)
        {
            if (MetricHistory.IsFrameMetric(id)) continue;
            if (!MetricCatalog.Get(id).Graphable) continue;
            if (!_slowHistory.TryGetValue(id, out var history))
            {
                history = new FloatHistory(1200); // 60 seconds at 20 Hz
                _slowHistory[id] = history;
            }
            history.Add((float)value);
        }
    }

    private double ReferenceNowMs()
    {
        if (Timeline.Count == 0) return 0;
        double sinceLastPresent = (DateTime.UtcNow - _lastPresentWall).TotalMilliseconds;
        if (sinceLastPresent < 0) sinceLastPresent = 0;
        // If presents stop, the reference clock keeps moving so the FPS window can empty out.
        return _lastPresentMs + Math.Min(sinceLastPresent, 5000);
    }

    private static void Add(Dictionary<MetricId, double> values, MetricId id, float? value)
    {
        if (value.HasValue && float.IsFinite(value.Value)) values[id] = value.Value;
    }

    public float[] GetHistory(MetricId id, int windowSeconds)
    {
        int frameSamplesPerSecond = 20; // Both consumers use the 20 Hz publication clock
        if (MetricHistory.IsFrameMetric(id))
        {
            int count = Math.Clamp(windowSeconds * frameSamplesPerSecond, 32, 2048);
            return id == MetricId.Fps ? _fpsFast.Snapshot(count) : _frameTimeFast.Snapshot(count);
        }

        lock (_gate)
        {
            if (!_slowHistory.TryGetValue(id, out var history)) return Array.Empty<float>();
            int count = Math.Clamp(windowSeconds * 20, 16, 1200);
            return history.Snapshot(count);
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _tickThread?.Join(1000);
        }
        catch
        {
            // ignore
        }
        _tickThread = null;
        _cts?.Dispose();
        _cts = null;
        Capture.Stop();
        Hardware.Stop();
        System.Dispose();
        Timeline.Dispose();
    }

    public void Dispose() => Stop();
}
