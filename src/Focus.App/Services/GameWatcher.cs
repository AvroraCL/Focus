using Focus.Telemetry;
using Focus.Telemetry.System;

namespace Focus.App.Services;

/// <summary>
/// Watches the foreground window and decides when a real game is running: the process must be
/// presenting frames (or be explicitly bound to a profile) to count.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    private readonly TelemetryHub _telemetry;
    private readonly ProfileStore _profiles;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public GameWatcher(TelemetryHub telemetry, ProfileStore profiles)
    {
        _telemetry = telemetry;
        _profiles = profiles;
    }

    public ProcessInfo? CurrentGame { get; private set; }
    public event Action<ProcessInfo>? GameDetected;
    public event Action<ProcessInfo>? GameChanged;

    public void Start()
    {
        if (_thread is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _thread = new Thread(() => Loop(token))
        {
            IsBackground = true,
            Name = "Focus-GameWatcher",
        };
        _thread.Start();
    }

    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch
            {
                // Never let detection crash the app.
            }
            token.WaitHandle.WaitOne(1000);
        }
    }

    private void Tick()
    {
        var foreground = ProcessLocator.GetForeground();
        if (foreground is null) return;
        if (foreground.Pid == Environment.ProcessId) return;
        if (foreground.Name.Equals("FocusOverlay", StringComparison.OrdinalIgnoreCase)) return;

        bool bound = _profiles.FindByGame(foreground.Name + ".exe") is not null;
        int presents = _telemetry.GetPresentRate(foreground.Pid);

        if (!bound && presents < 4) return;

        bool isNew = CurrentGame?.Pid != foreground.Pid;
        CurrentGame = foreground;
        if (isNew)
        {
            GameChanged?.Invoke(foreground);
            if (bound) GameDetected?.Invoke(foreground);
        }
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
    }

    public void Dispose() => Stop();
}
