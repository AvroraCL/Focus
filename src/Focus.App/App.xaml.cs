using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Focus.App.Localization;
using Focus.App.Services;

namespace Focus.App;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _initialized;

    public AppServices Services { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Focus has no pen/touch input; disabling WPF's stylus stack avoids the pen polling thread
        // that otherwise burns CPU on systems with a tablet driver installed.
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.DisableStylusAndTouchSupport", true);
        base.OnStartup(e);

        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            if (e.Args[i] is "--instance" or "-i") Focus.Core.FocusInstance.Override = e.Args[i + 1];
            if (e.Args[i] is "--etw-keyword")
            {
                string raw = e.Args[i + 1].Replace("0x", string.Empty, StringComparison.OrdinalIgnoreCase);
                if (ulong.TryParse(raw, System.Globalization.NumberStyles.HexNumber, null, out ulong keyword))
                    Focus.Telemetry.Frames.FrameCaptureService.KeywordOverride = keyword;
            }
        }

        _instanceMutex = new Mutex(true, Focus.Core.FocusInstance.AppMutexName, out bool created);
        if (!created)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DataPaths.AppendLog("fatal: " + (args.ExceptionObject as Exception)?.Message);

        try
        {
            Services.Initialize();
            _initialized = true;
            DataPaths.AppendLog($"focus start: instance={Focus.Core.FocusInstance.Suffix}, elevated={Services.IsElevated}, pid={Environment.ProcessId}");
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("startup failed: " + ex.Message);
            MessageBox.Show(ex.Message, "Focus", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        string initialPage = "dashboard";
        string? attachTo = null;
        bool hudAlways = false;
        bool captureDiagnostics = false;
        bool attachPresenter = false;
        string? screenshotPath = null;
        string? screenCapturePath = null;
        bool hideWindow = false;
        int screenshotDelay = 2600;
        for (int i = 0; i < e.Args.Length - 1; i++)
        {
            switch (e.Args[i])
            {
                case "--page":
                case "-p":
                    initialPage = e.Args[i + 1];
                    break;
                case "--attach":
                    attachTo = e.Args[i + 1];
                    break;
                case "--hud-always":
                    hudAlways = true;
                    break;
                case "--capture-diagnostics":
                    captureDiagnostics = true;
                    break;
                case "--attach-presenter":
                    attachPresenter = true;
                    break;
                case "--screenshot":
                    screenshotPath = e.Args[i + 1];
                    break;
                case "--capture-screen":
                    screenCapturePath = e.Args[i + 1];
                    break;
                case "--hide-window":
                    hideWindow = true;
                    break;
                case "--screenshot-delay":
                    int.TryParse(e.Args[i + 1], out screenshotDelay);
                    break;
            }
        }

        if (captureDiagnostics)
        {
            var diagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            TimeSpan lastCpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            DateTime lastCpuAt = DateTime.UtcNow;
            diagnosticsTimer.Tick += (_, _) =>
            {
                var cpu = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
                var now = DateTime.UtcNow;
                double cpuPercent = (cpu - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastCpuAt).TotalMilliseconds) * 100;
                lastCpu = cpu;
                lastCpuAt = now;
                var process = System.Diagnostics.Process.GetCurrentProcess();
                DataPaths.AppendLog($"capture: {Services.Telemetry.DiagnosticsString()} cpu={cpuPercent:F1}% mem={process.WorkingSet64 / (1024 * 1024)}MB");
            };
            diagnosticsTimer.Start();
        }

        if (attachPresenter)
        {
            var attachTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            attachTimer.Tick += (_, _) =>
            {
                var top = Services.Telemetry.GetTopPresenters(4)
                    .FirstOrDefault(t => t.Pid != Environment.ProcessId);
                if (top.Pid == 0 || top.Rate < 5) return;
                attachTimer.Stop();
                var info = Focus.Telemetry.System.ProcessLocator.DescribePid(top.Pid);
                if (info is not null)
                {
                    Services.AttachTo(info);
                    DataPaths.AppendLog($"attached to top presenter pid={top.Pid} rate={top.Rate}");
                }
            };
            attachTimer.Start();
        }

        if (hudAlways)
        {
            Services.Settings.Current.OnlyWhenFocused = false;
            Services.Overlay.Visible = true;
        }

        if (!string.IsNullOrWhiteSpace(attachTo))
        {
            string processName = attachTo.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? attachTo[..^4]
                : attachTo;
            var process = System.Diagnostics.Process.GetProcessesByName(processName)
                .OrderByDescending(p => p.StartTime)
                .FirstOrDefault();
            if (process is not null)
            {
                var info = Focus.Telemetry.System.ProcessLocator.DescribePid(process.Id);
                if (info is not null) Services.AttachTo(info);
            }
        }

        var window = new MainWindow(Services, initialPage);
        MainWindow = window;
        window.Show();

        if (hideWindow) window.Hide();

        if (!string.IsNullOrWhiteSpace(screenshotPath) || !string.IsNullOrWhiteSpace(screenCapturePath))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(500, screenshotDelay)) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                try
                {
                    if (!string.IsNullOrWhiteSpace(screenCapturePath)) CaptureScreen(screenCapturePath);
                    if (!string.IsNullOrWhiteSpace(screenshotPath)) CaptureWindow(window, screenshotPath);
                }
                catch (Exception ex)
                {
                    DataPaths.AppendLog("screenshot failed: " + ex.Message);
                }
                Shutdown();
            };
            timer.Start();
        }
    }

    private static void CaptureScreen(string path)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        }
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void CaptureWindow(Window window, string path)
    {
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        int width = Math.Max(1, (int)Math.Round(window.ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Round(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        using var stream = System.IO.File.Create(path);
        encoder.Save(stream);
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DataPaths.AppendLog("unhandled: " + e.Exception);
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, Loc.Inst["msg.error"], MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_initialized) Services.Shutdown();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
