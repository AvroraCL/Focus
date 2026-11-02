using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Focus.Core.Updates;

namespace Focus.App.Services;

public enum UpdatePhase { Idle, Checking, Current, Available, Downloading, Ready, Preparing, Failed, Cancelled }

/// <summary>UI-thread-owned update state. Downloads never overwrite the running installation.</summary>
public sealed class UpdateService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(15) };
    private CancellationTokenSource? _operation;
    private string? _jobDirectory;
    private string? _sha256;
    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    public UpdatePhase Phase { get; private set; }
    public UpdateRelease? Release { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }
    public DateTimeOffset? LastChecked { get; private set; }
    public bool Busy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Preparing;
    public event Action? Changed;

    private void Set(UpdatePhase phase) { Phase = phase; Changed?.Invoke(); }

    public async Task CheckAsync()
    {
        if (Busy || Phase == UpdatePhase.Ready) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        _operation = operation;
        Error = null;
        Release = null;
        Set(UpdatePhase.Checking);
        try
        {
            Release = await new ReleaseClient(_http).CheckAsync(CurrentVersion, operation.Token);
            LastChecked = DateTimeOffset.Now;
            Set(Release is null ? UpdatePhase.Current : UpdatePhase.Available);
        }
        catch (OperationCanceledException) { Set(UpdatePhase.Cancelled); }
        catch (Exception ex) { Error = ex.Message; Set(UpdatePhase.Failed); }
        finally { _operation = null; }
    }

    public async Task DownloadAsync()
    {
        if (Busy || Release is null) return;
        using var operation = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        _operation = operation;
        _jobDirectory = Path.Combine(DataPaths.Root, "updates", Guid.NewGuid().ToString("N"));
        Error = null;
        Progress = 0;
        Set(UpdatePhase.Downloading);
        try
        {
            Directory.CreateDirectory(_jobDirectory);
            _sha256 = await new ReleaseClient(_http).DownloadAsync(Release, Path.Combine(_jobDirectory, "package.zip"),
                new Progress<double>(p => { if (Phase == UpdatePhase.Downloading) { Progress = p; Changed?.Invoke(); } }), operation.Token);
            Progress = 100;
            Set(UpdatePhase.Ready);
        }
        catch (OperationCanceledException) { Set(UpdatePhase.Cancelled); }
        catch (Exception ex) { Error = ex.Message; Set(UpdatePhase.Failed); }
        finally { _operation = null; }
    }

    public void Cancel() => _operation?.Cancel();

    public async Task<bool> PrepareInstallAsync()
    {
        if (Busy || _jobDirectory is null || _sha256 is null || Phase != UpdatePhase.Ready) return false;
        Set(UpdatePhase.Preparing);
        Process? helper = null;
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "updater");
            string helperDirectory = Path.Combine(_jobDirectory, "helper");
            Directory.CreateDirectory(helperDirectory);
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(helperDirectory, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
            // Fail before closing the UI if the installation is read-only / requires elevation.
            string probe = Path.Combine(AppContext.BaseDirectory, ".focus-update-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            using var parent = Process.GetCurrentProcess();
            string jobPath = Path.Combine(_jobDirectory, "job.json");
            File.WriteAllText(jobPath, JsonSerializer.Serialize(new
            {
                Target = AppContext.BaseDirectory, Sha256 = _sha256, ParentId = parent.Id,
                ParentStarted = parent.StartTime.ToUniversalTime().Ticks,
            }));
            var start = new ProcessStartInfo(Path.Combine(helperDirectory, "FocusUpdater.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helperDirectory };
            start.ArgumentList.Add(jobPath);
            helper = Process.Start(start) ?? throw new IOException("Could not start the update helper.");
            var deadline = DateTime.UtcNow.AddSeconds(90);
            while (!File.Exists(Path.Combine(_jobDirectory, "ready")))
            {
                if (helper.HasExited || File.Exists(Path.Combine(_jobDirectory, "error.txt"))) throw new IOException("Update preparation failed. See " + _jobDirectory);
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Update preparation timed out.");
                await Task.Delay(150);
            }
            File.WriteAllText(Path.Combine(_jobDirectory, "install"), "confirmed");
            return true;
        }
        catch (Exception ex)
        {
            try { if (helper is { HasExited: false }) { helper.Kill(); helper.WaitForExit(5000); } } catch { }
            Error = ex.Message;
            Set(UpdatePhase.Failed);
            return false;
        }
        finally { helper?.Dispose(); }
    }

    public void Dispose() { Cancel(); _http.Dispose(); }
}
