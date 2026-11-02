using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.Core.App;

namespace Focus.App.Views;

public partial class SettingsPage : UserControl
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _statusTimer;
    private bool _loading;
    private string? _awaitingHotkey;

    public event Action? HotkeysChanged;

    public SettingsPage(AppServices services)
    {
        _services = services;
        InitializeComponent();
        Focusable = true;
        PreviewKeyDown += OnHotkeyCapture;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateStatus();

        Loaded += (_, _) =>
        {
            LoadValues();
            UpdateStatus();
            _statusTimer.Start();
        };
        Unloaded += (_, _) => _statusTimer.Stop();
        Loc.Inst.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        if (IsLoaded) LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;

        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(Loc.Inst["settings.languageSystem"]);
        LanguageCombo.Items.Add("简体中文");
        LanguageCombo.Items.Add("English");
        LanguageCombo.SelectedIndex = _services.Settings.Current.Language switch
        {
            "zh-CN" => 1,
            "en-US" => 2,
            _ => 0,
        };

        HardwareIntervalCombo.Items.Clear();
        foreach (var value in new[] { 200, 500, 1000, 2000 })
        {
            HardwareIntervalCombo.Items.Add($"{value} ms");
        }
        HardwareIntervalCombo.SelectedIndex = Array.IndexOf(new[] { 200, 500, 1000, 2000 }, _services.Settings.Current.HardwarePollMs) is var index && index >= 0 ? index : 1;

        UiRefreshCombo.Items.Clear();
        foreach (var value in new[] { 5, 8, 10, 15, 20, 30 })
        {
            UiRefreshCombo.Items.Add($"{value} Hz");
        }
        UiRefreshCombo.SelectedIndex = Array.IndexOf(new[] { 5, 8, 10, 15, 20, 30 }, _services.Settings.Current.UiRefreshHz) is var refreshIndex && refreshIndex >= 0 ? refreshIndex : 2;

        CloseToTrayCheck.IsChecked = _services.Settings.Current.CloseToTray;
        AutoUpdateCheck.IsChecked = _services.Settings.Current.CheckForUpdates;
        StartMinimizedCheck.IsChecked = _services.Settings.Current.StartMinimized;
        OnlyWhenFocusedCheck.IsChecked = _services.Settings.Current.OnlyWhenFocused;
        AutoRecordCheck.IsChecked = _services.Settings.Current.RecordAutomatically;

        ToggleHudKeys.Text = HotkeyService.Describe(_services.Settings.Current.Hotkeys.ToggleHud);
        ToggleRecordingKeys.Text = HotkeyService.Describe(_services.Settings.Current.Hotkeys.ToggleRecording);
        CycleProfileKeys.Text = HotkeyService.Describe(_services.Settings.Current.Hotkeys.CycleProfile);

        VersionText.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0";
        DataFolderText.Text = DataPaths.Root;

        _loading = false;
    }

    private void UpdateStatus()
    {
        UpdateUpdateStatus();
        bool elevated = _services.IsElevated;
        AdminDot.Fill = elevated ? (Brush)FindResource("Brush.Success") : (Brush)FindResource("Brush.Warning");
        AdminStatus.Text = Loc.Inst[elevated ? "settings.elevated" : "settings.notElevated"];
        ElevateButton.Visibility = elevated ? Visibility.Collapsed : Visibility.Visible;

        bool etw = _services.Telemetry.Capture.IsRunning;
        EtwDot.Fill = etw ? (Brush)FindResource("Brush.Success") : (Brush)FindResource("Brush.TextMuted");
        EtwStatus.Text = Loc.Inst[etw ? "settings.running" : "settings.stopped"];

        bool hardware = _services.Telemetry.Hardware.IsRunning;
        HardwareDot.Fill = hardware ? (Brush)FindResource("Brush.Success") : (Brush)FindResource("Brush.TextMuted");
        HardwareStatus.Text = Loc.Inst[hardware ? "settings.running" : "settings.unavailable"];
    }

    private void SaveSettings()
    {
        if (_loading) return;
        _services.ApplySettings();
        SavedIndicator.BeginAnimation(OpacityProperty, null);
        bool saved = _services.Settings.LastSaveError is null;
        SavedIndicator.Foreground = (Brush)FindResource(saved ? "Brush.Success" : "Brush.Danger");
        SavedIndicator.ToolTip = _services.Settings.LastSaveError;
        SavedIndicator.Text = Loc.Inst[saved ? "settings.saved" : "settings.saveFailed"];
        SavedIndicator.Opacity = 1;
        if (!saved) return;
        SavedIndicator.Opacity = 1;
        var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(1400),
        };
        SavedIndicator.BeginAnimation(OpacityProperty, fade);
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.Language = LanguageCombo.SelectedIndex switch
        {
            1 => "zh-CN",
            2 => "en-US",
            _ => "system",
        };
        SaveSettings();
    }

    private void OnHardwareIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.HardwarePollMs = new[] { 200, 500, 1000, 2000 }[Math.Max(0, HardwareIntervalCombo.SelectedIndex)];
        SaveSettings();
    }

    private void OnUiRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.UiRefreshHz = new[] { 5, 8, 10, 15, 20, 30 }[Math.Max(0, UiRefreshCombo.SelectedIndex)];
        SaveSettings();
    }

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.CloseToTray = CloseToTrayCheck.IsChecked == true;
        SaveSettings();
    }

    private void OnStartMinimizedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.StartMinimized = StartMinimizedCheck.IsChecked == true;
        _services.Settings.Current.OnboardingCompleted = true;
        SaveSettings();
    }

    private void OnOnlyWhenFocusedChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.OnlyWhenFocused = OnlyWhenFocusedCheck.IsChecked == true;
        SaveSettings();
    }

    private void OnAutoRecordChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.RecordAutomatically = AutoRecordCheck.IsChecked == true;
        SaveSettings();
    }

    private void OnCaptureHotkey(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string which }) return;
        _awaitingHotkey = which;
        HotkeyHint.Visibility = Visibility.Visible;
        HotkeyHint.Foreground = (Brush)FindResource("Brush.Accent");
        HotkeyHint.Text = Loc.Inst["settings.pressKeys"];
        Focus();
        Keyboard.Focus(this);
    }

    private void OnHotkeyCapture(object sender, KeyEventArgs e)
    {
        if (_awaitingHotkey is null) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _awaitingHotkey = null;
            HotkeyHint.Visibility = Visibility.Collapsed;
            return;
        }

        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift
            or Key.RightShift or Key.LWin or Key.RWin or Key.System) return;

        var binding = new HotkeyBinding
        {
            Control = (Keyboard.Modifiers & ModifierKeys.Control) != 0,
            Alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0,
            Shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0,
            Win = (Keyboard.Modifiers & ModifierKeys.Windows) != 0,
            VirtualKey = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key),
            Enabled = true,
        };
        if (binding.VirtualKey == 0) return;
        binding.Normalize();

        bool hasModifier = binding.Control || binding.Alt || binding.Shift || binding.Win;
        bool isFunctionKey = binding.VirtualKey is >= 0x70 and <= 0x87;
        if (!hasModifier && !isFunctionKey)
        {
            HotkeyHint.Foreground = (Brush)FindResource("Brush.Warning");
            HotkeyHint.Text = Loc.Inst["settings.needModifier"];
            return;
        }

        switch (_awaitingHotkey)
        {
            case "ToggleHud": _services.Settings.Current.Hotkeys.ToggleHud = binding; break;
            case "ToggleRecording": _services.Settings.Current.Hotkeys.ToggleRecording = binding; break;
            case "CycleProfile": _services.Settings.Current.Hotkeys.CycleProfile = binding; break;
        }

        _awaitingHotkey = null;
        HotkeyHint.Visibility = Visibility.Collapsed;
        SaveSettings();
        LoadValues();
        HotkeysChanged?.Invoke();
    }

    private void OnElevate(object sender, RoutedEventArgs e)
    {
        if (!ElevationHelper.RestartElevated())
        {
            MessageBox.Show(Loc.Inst["msg.error"], Loc.Inst["app.title"]);
        }
    }

    private void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            var telemetry = _services.Telemetry;
            var lines = new List<string>
            {
                $"Focus {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}",
                $"OS: {Environment.OSVersion.VersionString} ({System.Runtime.InteropServices.RuntimeInformation.OSArchitecture})",
                $"Elevated: {_services.IsElevated}",
                $"ETW running: {telemetry.Capture.IsRunning}",
                $"Hardware running: {telemetry.Hardware.IsRunning}",
                $"Target: {telemetry.TargetName} ({telemetry.TargetPid})",
                $"Capture: {telemetry.DiagnosticsString()}",
                $"Overlay running: {_services.Overlay.IsOverlayRunning}, visible: {_services.OverlayVisible}",
                $"Profiles: {_services.Profiles.Custom.Count}, sessions: {_services.Sessions.Summaries.Count}",
                $"Data: {DataPaths.Root}",
            };
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, lines));
            DiagnosticsState.Text = Loc.Inst["settings.diagnosticsCopied"];
            DiagnosticsState.Foreground = (Brush)FindResource("Brush.Success");
        }
        catch (Exception ex)
        {
            DiagnosticsState.Text = ex.Message;
            DiagnosticsState.Foreground = (Brush)FindResource("Brush.Danger");
        }
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            DataPaths.EnsureCreated();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DataPaths.Root}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.Inst["msg.error"]);
        }
    }

    private void UpdateUpdateStatus()
    {
        var updates = _services.Updates;
        UpdateVersion.Text = "v" + updates.CurrentVersion.ToString(3);
        UpdateState.Text = Loc.Inst["update." + updates.Phase.ToString().ToLowerInvariant()];
        UpdateState.Foreground = (Brush)FindResource(updates.Phase == UpdatePhase.Failed ? "Brush.Danger" : "Brush.Text");
        UpdateDetail.Text = updates.Error ?? (updates.Release is not null
            ? $"v{updates.Release.Version} · {updates.Release.Size / 1048576.0:F1} MB · " + Loc.Inst["update.source"]
            : Loc.Inst["update.source"]);
        if (updates.LastChecked is not null) UpdateDetail.Text += " · " + updates.LastChecked.Value.ToLocalTime().ToString("MM-dd HH:mm");
        UpdateProgress.Visibility = updates.Phase == UpdatePhase.Downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = updates.Progress;
        if (updates.Phase == UpdatePhase.Downloading) UpdateState.Text += $" {updates.Progress:F0}%";
        CheckUpdateButton.IsEnabled = !updates.Busy && updates.Phase != UpdatePhase.Ready;
        DownloadUpdateButton.Visibility = !updates.Busy && updates.Release is not null && updates.Phase != UpdatePhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.Visibility = updates.Phase == UpdatePhase.Ready ? Visibility.Visible : Visibility.Collapsed;
        CancelUpdateButton.Visibility = updates.Phase is UpdatePhase.Checking or UpdatePhase.Downloading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAutoUpdateChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.Settings.Current.CheckForUpdates = AutoUpdateCheck.IsChecked == true;
        SaveSettings();
    }
    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        var task = _services.Updates.CheckAsync();
        UpdateUpdateStatus();
        await task;
        UpdateUpdateStatus();
    }
    private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        var task = _services.Updates.DownloadAsync();
        UpdateUpdateStatus();
        await task;
        UpdateUpdateStatus();
    }
    private async void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow window) await window.InstallUpdateAsync();
        UpdateUpdateStatus();
    }
    private void OnCancelUpdate(object sender, RoutedEventArgs e) => _services.Updates.Cancel();
    private void OnReleaseNotes(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(global::Focus.Core.Updates.ReleaseClient.Repository + "/releases") { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.Inst["msg.error"]); }
    }
}
