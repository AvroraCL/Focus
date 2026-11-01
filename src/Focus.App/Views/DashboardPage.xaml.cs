using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Focus.App.Dialogs;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.Core.Metrics;
using Focus.Hud.Formatting;

namespace Focus.App.Views;

public partial class DashboardPage : UserControl
{
    private readonly AppServices _services;
    private readonly DispatcherTimer _timer;

    public event Action<string>? NavigateRequested;

    public DashboardPage(AppServices services)
    {
        _services = services;
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshInterval() };
        _timer.Tick += (_, _) =>
        {
            if (!IsVisible) return;
            Update();
        };
        _timer.Start();

        _services.TargetChanged += Update;
        _services.RecordingChanged += Update;
        _services.OverlayVisibilityChanged += Update;
        _services.ActiveProfileChanged += Update;
        Loc.Inst.LanguageChanged += Update;

        Loaded += (_, _) =>
        {
            _timer.Interval = RefreshInterval();
            Update();
            if (!_timer.IsEnabled) _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
    }

    private TimeSpan RefreshInterval()
    {
        int hz = Math.Clamp(_services.Settings.Current.UiRefreshHz, 5, 20);
        return TimeSpan.FromMilliseconds(1000.0 / hz);
    }

    private void Update()
    {
        var hub = _services.Telemetry;
        var snapshot = hub.Current;
        var hardware = hub.Hardware.Current;

        bool hasTarget = snapshot.TargetPid > 0;
        TargetNameText.Text = hasTarget ? snapshot.TargetName : Loc.Inst["dashboard.noTarget"];
        NoTargetButton.Visibility = hasTarget ? Visibility.Collapsed : Visibility.Visible;

        double? fps = snapshot.Get(MetricId.Fps);
        bool live = fps is > 0;
        LiveDot.Fill = live ? (Brush)FindResource("Brush.Success") : (Brush)FindResource("Brush.TextMuted");

        FpsText.Text = fps is null ? "—" : fps.Value.ToString("F0");
        FpsText.FontSize = fps is null ? 46 : 62;
        FpsText.Foreground = fps is null ? (Brush)FindResource("Brush.TextMuted") : (Brush)FindResource("Brush.Text");
        FrameTimeText.Text = FormatMetric(snapshot, MetricId.FrameTime, 2);
        FrameStatusText.Text = hasTarget && !live ? Loc.Inst["dashboard.noFrames"] : string.Empty;

        AvgFpsText.Text = FormatMetric(snapshot, MetricId.FpsAverage, 1);
        Low1Text.Text = FormatMetric(snapshot, MetricId.FpsOnePercentLow, 1);
        Low01Text.Text = FormatMetric(snapshot, MetricId.FpsPointOnePercentLow, 1);
        DurationText.Text = FormatDuration(snapshot.Get(MetricId.SessionTime));

        FrameSpark.Values = hub.GetHistory(MetricId.FrameTime, 30);
        GpuSpark.Values = hub.GetHistory(MetricId.GpuUsage, 30);
        RamSpark.Values = hub.GetHistory(MetricId.RamPercent, 30);

        GpuNameText.Text = hardware.GpuName ?? Loc.Inst["common.na"];
        GpuUsageText.Text = FormatNumber(hardware.GpuUsage);
        GpuUsageBar.Value = hardware.GpuUsage ?? 0;
        GpuTempText.Text = FormatMetric(snapshot, MetricId.GpuTemperature);
        GpuClockText.Text = FormatMetric(snapshot, MetricId.GpuCoreClock);
        GpuPowerText.Text = FormatMetric(snapshot, MetricId.GpuPower, 1);
        VramText.Text = FormatVram(snapshot);
        VramDetailText.Text = FormatVram(snapshot);
        VramPercentText.Text = FormatMetric(snapshot, MetricId.GpuMemoryPercent);

        CpuNameText.Text = hardware.CpuName ?? Loc.Inst["common.na"];
        CpuUsageText.Text = FormatNumber(hardware.CpuUsage);
        CpuUsageBar.Value = hardware.CpuUsage ?? 0;
        CpuTempText.Text = FormatMetric(snapshot, MetricId.CpuTemperature);
        CpuClockText.Text = FormatMetric(snapshot, MetricId.CpuClock);
        CpuPowerText.Text = FormatMetric(snapshot, MetricId.CpuPower, 1);
        var cores = snapshot.CoreUsage;
        CoreCountText.Text = cores.Count > 0 ? cores.Count.ToString() : "—";
        CoreStrip.Values = cores.ToArray();

        RamPercentText.Text = FormatNumber(snapshot.Get(MetricId.RamPercent));
        RamBar.Value = snapshot.Get(MetricId.RamPercent) ?? 0;
        RamText.Text = FormatRam(snapshot);
        ProcessMemText.Text = FormatMetric(snapshot, MetricId.ProcessWorkingSet);

        OverlayProfileText.Text = DisplayProfileName(_services.ActiveProfile.Name, _services.ActiveProfile.Id);
        bool overlayVisible = _services.OverlayVisible;
        OverlayStateText.Text = Loc.Inst[overlayVisible ? "dashboard.overlayVisible" : "dashboard.overlayHidden"];
        OverlayDot.Fill = overlayVisible ? (Brush)FindResource("Brush.Success") : (Brush)FindResource("Brush.TextMuted");
        OverlayToggleButton.Content = Loc.Inst[overlayVisible ? "dashboard.hideOverlay" : "dashboard.showOverlay"];

        bool recording = _services.IsRecording;
        RecordButton.IsEnabled = hasTarget || recording;
        RecordButton.Content = Loc.Inst[recording ? "dashboard.stopRecording" : "dashboard.startRecording"];
        RecordDot.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
        if (recording)
        {
            var started = _services.Telemetry.Recorder.StartedAt;
            var elapsed = DateTimeOffset.Now - started;
            RecordTimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
            SessionInfoText.Text = Loc.Inst.Format("dashboard.recordingInfo", _services.Telemetry.Recorder.FrameCount);
        }
        else
        {
            RecordTimerText.Text = "00:00";
            SessionInfoText.Text = Loc.Inst.Format("dashboard.recordHint", HotkeyService.Describe(_services.Settings.Current.Hotkeys.ToggleRecording));
        }

        AdminBanner.Visibility = _services.IsElevated ? Visibility.Collapsed : Visibility.Visible;
    }

    private string DisplayProfileName(string name, string id)
    {
        if (id.StartsWith("preset-", StringComparison.Ordinal))
        {
            string key = "preset." + id;
            var localized = Loc.Inst[key];
            if (localized != key) return localized;
        }
        return name;
    }

    private static string FormatMetric(Core.Metrics.MetricsSnapshot snapshot, MetricId id, int decimals = -1)
        => MetricFormatter.FormatValue(id, snapshot.Get(id), decimals);

    private static string FormatNumber(double? value) => value is null ? "—" : value.Value.ToString("F0");

    private static string FormatVram(Core.Metrics.MetricsSnapshot snapshot)
    {
        double? used = snapshot.Get(MetricId.GpuMemoryUsed);
        double? total = snapshot.Get(MetricId.GpuMemoryTotal);
        if (used is null) return "—";
        string usedText = (used.Value / 1024.0).ToString("F1");
        return total is null ? usedText + " GB" : $"{usedText} / {total.Value / 1024.0:F0} GB";
    }

    private static string FormatRam(Core.Metrics.MetricsSnapshot snapshot)
    {
        double? used = snapshot.Get(MetricId.RamUsed);
        double? total = snapshot.Get(MetricId.RamTotal);
        if (used is null) return "—";
        string usedText = (used.Value / 1024.0).ToString("F1");
        return total is null ? usedText + " GB" : $"{usedText} / {total.Value / 1024.0:F0} GB";
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null or <= 0) return "—";
        var span = TimeSpan.FromSeconds(seconds.Value);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private void OnElevateClick(object sender, RoutedEventArgs e)
    {
        if (!ElevationHelper.RestartElevated())
        {
            MessageBox.Show(Loc.Inst["msg.error"], Loc.Inst["app.title"]);
        }
    }

    private void OnSelectProcessClick(object sender, RoutedEventArgs e) => SelectProcess();

    private void SelectProcess()
    {
        var dialog = new ProcessPickerWindow(_services) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.SelectedProcess is { } process)
        {
            _services.AttachTo(process);
            Update();
        }
    }

    private void OnToggleOverlay(object sender, RoutedEventArgs e)
    {
        _services.OverlayVisible = !_services.OverlayVisible;
        Update();
    }

    private void OnEditHud(object sender, RoutedEventArgs e) => NavigateRequested?.Invoke("editor");

    private void OnToggleRecording(object sender, RoutedEventArgs e)
    {
        _services.ToggleRecording();
        Update();
    }
}
