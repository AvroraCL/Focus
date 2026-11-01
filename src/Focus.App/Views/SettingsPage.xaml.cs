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
        SavedIndicator.Text = Loc.Inst["settings.saved"];
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
}
