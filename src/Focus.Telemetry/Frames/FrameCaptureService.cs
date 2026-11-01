using System.Diagnostics;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Session;

namespace Focus.Telemetry.Frames;

/// <summary>
/// Realtime consumer of the Microsoft-Windows-DxgKrnl ETW provider. Every Present call made by any
/// process (D3D9/11/12, Vulkan and OpenGL WSI all end up here) produces one event.
///
/// Requires the process to be elevated: ETW realtime sessions are a privileged operation.
/// </summary>
public sealed class FrameCaptureService : IDisposable
{
    private static readonly Guid DxgKrnlProvider = new("802EC45A-1E99-4B83-9920-87C98277BA9D");
    private const int PresentEventId = 0xB8; // Present_Info
    // Matches the DxgKrnl Present_Info keyword bits (Base|Profiler|ForceVsync|Cdd|Resource).
    // Using 0x8000000 (the "Present" keyword name in modern manifests) captures nothing on Windows 10.
    private const ulong PresentKeyword = 0x1; // Base keyword also carries Present_Info

    private readonly object _gate = new();
    private TraceEventSession? _session;
    private Task? _processing;
    private volatile bool _running;
    private volatile int _eventsSeen;
    private volatile int _presentsSeen;

    public event Action<int, double>? Present;

    /// <summary>Overrides the provider keyword filter (for testing on unusual builds). 0 = default.</summary>
    public static ulong KeywordOverride { get; set; }

    public bool IsRunning => _running;
    public string? LastError { get; private set; }
    public int EventsSeen => _eventsSeen;
    public int PresentsSeen => _presentsSeen;
    public bool Elevated { get; private set; }

    public bool Start()
    {
        lock (_gate)
        {
            if (_running) return true;

            LastError = null;
            Elevated = TraceEventSession.IsElevated() == true;
            if (!Elevated)
            {
                LastError = "elevation-required";
                return false;
            }

            try
            {
                CleanupStaleSessions();
                _session = new TraceEventSession(SessionName, TraceEventSessionOptions.Create)
                {
                    StopOnDispose = true,
                    BufferSizeMB = 4,
                };
                EnableProvider();
                _session.Source.AllEvents += OnEvent;
                _running = true;
                _processing = Task.Factory.StartNew(
                    () => _session?.Source.Process(),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                global::System.IO.File.AppendAllText(
                    global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "focus-etw.log"),
                    $"{DateTime.Now:HH:mm:ss.fff}  session started, event-id filter for Present_Info{Environment.NewLine}");
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                StopInternal();
                return false;
            }
        }
    }

    private string SessionName => "Focus-FrameCapture-" + Environment.ProcessId;

    private void EnableProvider()
    {
        if (_session is null) return;

        // Keywords do not match Present_Info on Windows 10; the kernel-level event id filter is what
        // keeps the session quiet, so every other DxgKrnl event is dropped before it reaches us.
        ulong keywords = KeywordOverride;
        // Kernel-level event id filtering keeps the session quiet: only Present_Info is delivered.
        var options = new TraceEventProviderOptions
        {
            EventIDsToEnable = new List<int> { PresentEventId },
        };
        _session.EnableProvider(DxgKrnlProvider, TraceEventLevel.Verbose, keywords, options);
    }

    private void OnEvent(TraceEvent data)
    {
        if (data.ProviderGuid != DxgKrnlProvider) return;
        if ((int)data.ID != PresentEventId) { _eventsSeen++; return; }
        _presentsSeen++;
        try
        {
            Present?.Invoke(data.ProcessID, data.TimeStampRelativeMSec);
        }
        catch
        {
            // A throwing subscriber must never kill the ETW dispatch thread.
        }
    }

    private static void CleanupStaleSessions()
    {
        try
        {
            foreach (var name in TraceEventSession.GetActiveSessionNames())
            {
                if (!name.StartsWith("Focus-FrameCapture-", StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = name["Focus-FrameCapture-".Length..];
                if (!int.TryParse(suffix, out int pid)) continue;
                if (pid == Environment.ProcessId) continue;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited) continue;
                }
                catch
                {
                    // Process is gone: the session is stale.
                }
                try
                {
                    var stale = TraceEventSession.GetActiveSession(name);
                    stale?.Dispose();
                }
                catch
                {
                    // Ignore: another instance may own it.
                }
            }
        }
        catch
        {
            // Enumerating sessions can fail on locked-down systems; not fatal.
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _running = false;
        try
        {
            _session?.Source.StopProcessing();
        }
        catch
        {
            // ignore
        }
        try
        {
            _session?.Dispose();
        }
        catch
        {
            // ignore
        }
        _session = null;
        _processing = null;
    }

    public void Dispose() => Stop();
}
