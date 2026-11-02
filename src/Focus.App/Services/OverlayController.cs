using System.Diagnostics;
using System.IO;
using Focus.Core;
using Focus.Core.Ipc;
using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Focus.Telemetry;

namespace Focus.App.Services;

/// <summary>
/// Owns the overlay child process and the shared-memory channel that feeds it. Runs on its own thread
/// so publishing stays smooth while the UI is busy.
/// </summary>
public sealed class OverlayController : IDisposable
{
    private readonly TelemetryHub _telemetry;
    private readonly SettingsService _settings;
    private readonly SharedStateChannel _channel;
    private EventWaitHandle? _exitSignal;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private Process? _process;
    private HudProfile? _profile;
    private string? _profileJson;
    private int _profileRevision;
    private readonly object _gate = new();

    public OverlayController(TelemetryHub telemetry, SettingsService settings)
    {
        _telemetry = telemetry;
        _settings = settings;
        _channel = SharedStateChannel.CreateWriter();
        try
        {
            _exitSignal = new EventWaitHandle(false, EventResetMode.ManualReset, SharedStateChannel.ExitEventName);
        }
        catch
        {
            _exitSignal = null;
        }
    }

    public bool Visible { get; set; } = true;
    public bool OverlayExecutableMissing { get; private set; }
    public bool IsOverlayRunning => _process is { HasExited: false };

    /// <summary>
    /// The overlay is normally copied into an "overlay" subfolder, but a single-directory publish
    /// places it next to the app.
    /// </summary>
    public string OverlayExecutablePath
    {
        get
        {
            string isolated = Path.Combine(AppContext.BaseDirectory, "overlay", "FocusOverlay.exe");
            if (File.Exists(isolated)) return isolated;
            return Path.Combine(AppContext.BaseDirectory, "FocusOverlay.exe");
        }
    }

    public void SetProfile(HudProfile? profile)
    {
        lock (_gate)
        {
            _profile = profile?.Clone();
            _profileJson = _profile is null ? null : ProfileCodec.Serialize(_profile);
            _profileRevision++;
        }
    }

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() => Loop(token))
        {
            IsBackground = true,
            Name = "Focus-OverlayPublisher",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.Start();
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                EnsureOverlayProcess();
                PublishOnce();
            }
            catch (Exception ex)
            {
                DataPaths.AppendLog("overlay publish failed: " + ex.Message);
            }
            token.WaitHandle.WaitOne(50);
        }
    }

    private DateTime _overlayStartedAt;
    private DateTime _overlayRetryAfter;
    private int _overlayFailures;

    private void EnsureOverlayProcess()
    {
        if (_process is { HasExited: false }) return;

        // Back off when the overlay keeps dying immediately (for example a stale instance holds
        // its single-instance mutex) instead of respawning in a tight loop.
        if (_process is { HasExited: true })
        {
            bool quickExit = (DateTime.UtcNow - _overlayStartedAt).TotalSeconds < 3;
            _overlayFailures = quickExit ? _overlayFailures + 1 : 0;
            if (_overlayFailures >= 3)
            {
                _overlayRetryAfter = DateTime.UtcNow.AddSeconds(15);
                DataPaths.AppendLog($"overlay exited repeatedly ({_overlayFailures}), retrying in 15s");
                _overlayFailures = 0;
            }
        }

        string exe = OverlayExecutablePath;
        if (!File.Exists(exe))
        {
            if (!OverlayExecutableMissing) DataPaths.AppendLog("overlay executable not found at " + exe);
            OverlayExecutableMissing = true;
            return;
        }
        OverlayExecutableMissing = false;

        // The overlay must share this instance's named objects and data folder.
        if (DateTime.UtcNow < _overlayRetryAfter) return;

        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            CreateNoWindow = true,
        };
        info.Environment["FOCUS_INSTANCE"] = FocusInstance.Suffix;
        try
        {
            DataPaths.AppendLog("starting overlay: " + exe);
            _process = Process.Start(info);
            _overlayStartedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("overlay start failed: " + ex.Message);
        }
    }

    private void PublishOnce()
    {
        HudProfile? profile;
        string? json;
        int revision;
        lock (_gate)
        {
            profile = _profile;
            json = _profileJson;
            revision = _profileRevision;
        }

        var snapshot = _telemetry.Current;
        var values = new Dictionary<string, double>(snapshot.Values.Count);
        foreach (var (id, value) in snapshot.Values) values[id.ToString()] = value;

        var state = new OverlayState
        {
            Sequence = snapshot.Sequence,
            Timestamp = snapshot.Timestamp,
            TargetPid = snapshot.TargetPid,
            TargetName = snapshot.TargetName,
            Capturing = snapshot.Capturing,
            FrameCount = (long)(snapshot.Get(MetricId.FrameCount) ?? 0),
            SessionSeconds = snapshot.Get(MetricId.SessionTime) ?? 0,
            ProfileJson = json,
            ProfileRevision = revision,
            Visible = Visible,
            OnlyWhenFocused = _settings.Current.OnlyWhenFocused,
            Values = values,
            CoreUsage = snapshot.CoreUsage.ToArray(),
            History = BuildHistory(profile),
        };

        _channel.Write(OverlayStateCodec.Serialize(state));
    }

    private Dictionary<string, float[]> BuildHistory(HudProfile? profile)
    {
        var result = new Dictionary<string, float[]>();
        if (profile is null) return result;

        foreach (var element in profile.Elements.ToArray().OrderByDescending(e => e.WindowSeconds))
        {
            if (element.Kind != HudElementKind.Graph || element.Metric == MetricId.None) continue;
            string key = element.Metric.ToString();
            if (result.ContainsKey(key)) continue;
            result[key] = _telemetry.GetHistory(element.Metric, element.WindowSeconds);
        }
        return result;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _thread?.Join(500);
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
            _exitSignal?.Set();
        }
        catch
        {
            // ignore
        }

        if (_process is { HasExited: false })
        {
            try
            {
                if (!_process.WaitForExit(1200))
                {
                    _process.Kill(entireProcessTree: false);
                    _process.WaitForExit(3000);
                }
            }
            catch
            {
                // ignore
            }
        }
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        Stop();
        _exitSignal?.Dispose();
        _channel.Dispose();
    }
}
