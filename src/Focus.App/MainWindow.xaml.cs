using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.App.Views;
using Focus.Core.Metrics;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Focus.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _toastTimer;
    private HotkeyService? _hotkeys;
    private WinForms.NotifyIcon? _tray;
    private DashboardPage? _dashboard;
    private EditorPage? _editor;
    private ProfilesPage? _profiles;
    private SessionsPage? _sessions;
    private SettingsPage? _settings;
    private bool _forceClose;
    private string _lastTrayText = string.Empty;
    private readonly string _initialPage;

    public MainWindow(AppServices services, string initialPage = "dashboard")
    {
        _services = services;
        _initialPage = initialPage;
        InitializeComponent();
        RestoreWindowPlacement();

        VersionText.Text = "Focus " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0");

        _statusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _statusTimer.Tick += (_, _) => UpdateChrome();
        _statusTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.6) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };

        SourceInitialized += (_, _) => SetupHotkeys();
        Loaded += (_, _) =>
        {
            SetupTray();
            GoTo(_initialPage);
            UpdateChrome();
        };
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closing += OnClosing;

        _services.TargetChanged += UpdateChrome;
        _services.RecordingChanged += UpdateChrome;
        _services.OverlayVisibilityChanged += UpdateChrome;
        _services.ActiveProfileChanged += UpdateChrome;
        Loc.Inst.LanguageChanged += OnLanguageChanged;
    }

    // ------------------------------------------------------------- navigation

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key } && IsLoaded) Navigate(key);
    }

    private void Navigate(string key)
    {
        switch (key)
        {
            case "editor":
                _editor ??= CreateEditor();
                PageHost.Content = _editor;
                PageTitle.Text = Loc.Inst["nav.editor"];
                PageSubtitle.Text = Loc.Inst["nav.editor.desc"];
                break;
            case "profiles":
                _profiles ??= CreateProfiles();
                PageHost.Content = _profiles;
                PageTitle.Text = Loc.Inst["nav.profiles"];
                PageSubtitle.Text = Loc.Inst["nav.profiles.desc"];
                break;
            case "sessions":
                if (_sessions is null)
                {
                    _sessions = new SessionsPage(_services);
                    _sessions.NavigateToDashboard += () => GoTo("dashboard");
                }
                PageHost.Content = _sessions;
                PageTitle.Text = Loc.Inst["nav.sessions"];
                PageSubtitle.Text = Loc.Inst["nav.sessions.desc"];
                break;
            case "settings":
                _settings ??= CreateSettings();
                PageHost.Content = _settings;
                PageTitle.Text = Loc.Inst["nav.settings"];
                PageSubtitle.Text = Loc.Inst["nav.settings.desc"];
                break;
            default:
                _dashboard ??= CreateDashboard();
                PageHost.Content = _dashboard;
                PageTitle.Text = Loc.Inst["nav.dashboard"];
                PageSubtitle.Text = Loc.Inst["nav.dashboard.desc"];
                break;
        }
    }

    private DashboardPage CreateDashboard()
    {
        var page = new DashboardPage(_services);
        page.NavigateRequested += GoTo;
        return page;
    }

    private EditorPage CreateEditor()
    {
        var page = new EditorPage(_services);
        return page;
    }

    private ProfilesPage CreateProfiles()
    {
        var page = new ProfilesPage(_services);
        page.NavigateRequested += GoTo;
        return page;
    }

    private SettingsPage CreateSettings()
    {
        var page = new SettingsPage(_services);
        page.HotkeysChanged += () => RegisterHotkeys();
        return page;
    }

    private void GoTo(string key)
    {
        var button = key switch
        {
            "editor" => NavEditor,
            "profiles" => NavProfiles,
            "sessions" => NavSessions,
            "settings" => NavSettings,
            _ => NavDashboard,
        };
        button.IsChecked = true;
        Navigate(key);
    }

    // ------------------------------------------------------------- chrome

    private void UpdateChrome()
    {
        var telemetry = _services.Telemetry;
        bool elevated = _services.IsElevated;
        bool capture = telemetry.Capture.IsRunning;
        bool live = telemetry.Current.Get(MetricId.Fps) is > 0;

        CaptureDot.Fill = (Brush)FindResource(capture ? (live ? "Brush.Success" : "Brush.Warning") : "Brush.TextMuted");
        CaptureStatusText.Text = capture
            ? Loc.Inst["settings.running"] + (live ? " · " + telemetry.TargetName : string.Empty)
            : Loc.Inst[elevated ? "settings.stopped" : "dashboard.adminRequired"];
        CaptureStatusText.Foreground = (Brush)FindResource(elevated || capture ? "Brush.TextMuted" : "Brush.Warning");

        OverlayDot.Fill = (Brush)FindResource(_services.OverlayVisible ? "Brush.Success" : "Brush.TextMuted");
        OverlayStatusText.Text = Loc.Inst["dashboard.overlay"] + " · " +
                                 Loc.Inst[_services.OverlayVisible ? "dashboard.overlayVisible" : "dashboard.overlayHidden"];

        HudChip.Content = _services.OverlayVisible
            ? Loc.Inst["dashboard.hideOverlay"]
            : Loc.Inst["dashboard.showOverlay"];

        bool recording = _services.IsRecording;
        RecordingChip.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        if (recording)
        {
            var elapsed = DateTimeOffset.Now - telemetry.Recorder.StartedAt;
            RecordingChipText.Text = $"REC {elapsed:hh\\:mm\\:ss}";
        }

        if (_tray is not null)
        {
            string fps = telemetry.Current.Get(MetricId.Fps) is { } value ? $"{value:F0} FPS · " : string.Empty;
            string trayText = $"Focus — {fps}{telemetry.TargetName}".Trim(' ', '—', ' ');
            if (trayText != _lastTrayText)
            {
                _lastTrayText = trayText;
                _tray.Text = trayText;
            }
        }
    }

    private void UpdateMaximizeGlyph()
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        RootMarginFix(maximized);
    }

    private void RootMarginFix(bool maximized)
    {
        // WindowChrome maximized windows need a small inset to avoid clipping at the screen edge.
        if (Content is FrameworkElement root)
        {
            root.Margin = maximized ? new Thickness(7) : new Thickness(0);
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void RestoreWindowPlacement()
    {
        var settings = _services.Settings.Current;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        if (settings.WindowLeft is double left && settings.WindowTop is double top)
        {
            double minLeft = SystemParameters.VirtualScreenLeft - 32;
            double minTop = SystemParameters.VirtualScreenTop - 32;
            double maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 200;
            double maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 120;
            if (left >= minLeft && left <= maxLeft && top >= minTop && top <= maxTop)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }

        if (settings.WindowMaximized)
        {
            Loaded += (_, _) => WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowPlacement()
    {
        var settings = _services.Settings.Current;
        settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
        }
        _services.Settings.Save();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowPlacement();
        if (_forceClose) return;
        if (_services.Settings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _forceClose = true;
        _statusTimer.Stop();
        _toastTimer.Stop();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        base.OnClosed(e);
    }

    private void OnToggleHud(object sender, RoutedEventArgs e)
    {
        _services.OverlayVisible = !_services.OverlayVisible;
        ShowToast(Loc.Inst[_services.OverlayVisible ? "dashboard.overlayVisible" : "dashboard.overlayHidden"]);
        UpdateChrome();
    }

    // ------------------------------------------------------------- hotkeys

    private void SetupHotkeys()
    {
        _hotkeys = new HotkeyService();
        _hotkeys.Attach(this);
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        _hotkeys.UnregisterAll();
        var settings = _services.Settings.Current.Hotkeys;
        bool ok = true;
        ok &= _hotkeys.Register(settings.ToggleHud, () => Dispatcher.BeginInvoke(ToggleHudHotkey));
        ok &= _hotkeys.Register(settings.ToggleRecording, () => Dispatcher.BeginInvoke(ToggleRecordingHotkey));
        ok &= _hotkeys.Register(settings.CycleProfile, () => Dispatcher.BeginInvoke(CycleProfileHotkey));
        if (!ok) ShowToast(Loc.Inst["msg.hotkeyTaken"]);
    }

    private void ToggleHudHotkey()
    {
        _services.OverlayVisible = !_services.OverlayVisible;
        ShowToast(Loc.Inst[_services.OverlayVisible ? "dashboard.overlayVisible" : "dashboard.overlayHidden"]);
        UpdateChrome();
    }

    private void ToggleRecordingHotkey()
    {
        _services.ToggleRecording();
        ShowToast(Loc.Inst[_services.IsRecording ? "dashboard.recording" : "dashboard.recordStopped"]);
        UpdateChrome();
    }

    private void CycleProfileHotkey()
    {
        _services.CycleProfile();
        ShowToast(_services.ActiveProfile.Name);
        UpdateChrome();
    }

    // ------------------------------------------------------------- tray

    private void SetupTray()
    {
        try
        {
            _tray = new WinForms.NotifyIcon
            {
                Icon = CreateTrayIcon(),
                Visible = true,
                Text = "Focus",
            };
            RebuildTrayMenu();
            _tray.DoubleClick += (_, _) => RestoreFromTray();
        }
        catch (Exception ex)
        {
            DataPaths.AppendLog("tray init failed: " + ex.Message);
        }
    }

    private void RebuildTrayMenu()
    {
        if (_tray is null) return;
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Loc.Inst["tray.show"], null, (_, _) => RestoreFromTray());
        menu.Items.Add(Loc.Inst["tray.toggleHud"], null, (_, _) => ToggleHudHotkey());
        menu.Items.Add(Loc.Inst["tray.toggleRecording"], null, (_, _) => ToggleRecordingHotkey());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(Loc.Inst["tray.exit"], null, (_, _) => ExitFromTray());
        _tray.ContextMenuStrip = menu;
    }

    public void ActivateFromExternal()
    {
        DataPaths.AppendLog("second launch: window activated");
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _forceClose = true;
        _tray?.Dispose();
        _tray = null;
        Application.Current.Shutdown();
    }

    private static Drawing.Icon CreateTrayIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            using var brush = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x4C, 0x9A, 0xFF));
            graphics.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Drawing.Font("Segoe UI", 17, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var size = graphics.MeasureString("F", font);
            graphics.DrawString("F", font, Drawing.Brushes.White, (32 - size.Width) / 2, (32 - size.Height) / 2);
        }

        nint handle = bitmap.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(handle);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);

    // ------------------------------------------------------------- toast / language

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void OnLanguageChanged()
    {
        RebuildTrayMenu();
        UpdateChrome();
        if (PageHost.Content is not null && IsLoaded)
        {
            // Refresh the current page title/subtitle.
            string key = NavEditor.IsChecked == true ? "editor"
                : NavProfiles.IsChecked == true ? "profiles"
                : NavSessions.IsChecked == true ? "sessions"
                : NavSettings.IsChecked == true ? "settings"
                : "dashboard";
            Navigate(key);
        }
    }

}
