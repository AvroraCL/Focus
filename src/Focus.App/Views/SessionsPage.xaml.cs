using System.Windows;
using System.Windows.Controls;
using Focus.App.Localization;
using Focus.App.Services;
using Focus.Core.Sessions;

namespace Focus.App.Views;

public partial class SessionsPage : UserControl
{
    private readonly AppServices _services;
    private PerformanceSession? _loaded;
    private string? _selectedId;

    public event Action? NavigateToDashboard;

    public SessionsPage(AppServices services)
    {
        _services = services;
        InitializeComponent();
        Loaded += (_, _) => Rebuild();
        _services.Sessions.Changed += OnSessionsChanged;
        _services.RecordingChanged += OnSessionsChanged;
        Loc.Inst.LanguageChanged += OnLanguageChanged;
    }

    private void OnSessionsChanged()
    {
        if (IsLoaded) Rebuild();
    }

    private void OnLanguageChanged()
    {
        if (IsLoaded) Rebuild();
    }

    private sealed record SessionItem(string Id, string Name, string Subtitle, string Summary);

    private void Rebuild()
    {
        var items = _services.Sessions.Summaries.Select(s => new SessionItem(
            s.Id,
            s.Name,
            $"{s.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {s.ProcessName ?? "—"}",
            $"{s.Stats.AverageFps:F0} FPS · {s.Stats.OnePercentLow:F0} 1% · {FormatDuration(s.Stats.DurationSeconds)}"))
            .ToList();

        SessionList.ItemsSource = items;
        CountText.Text = items.Count.ToString();
        if (items.Count > 0 && SessionList.SelectedItem is null) SessionList.SelectedIndex = 0;

        if (_selectedId is not null)
        {
            var match = items.FirstOrDefault(i => i.Id == _selectedId);
            if (match is not null)
            {
                SessionList.SelectedItem = match;
                return;
            }
        }

        if (items.Count == 0)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyState.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnSessionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SessionList.SelectedItem is not SessionItem item) return;
        _selectedId = item.Id;
        var session = _services.Sessions.Load(item.Id);
        if (session is null) return;
        _loaded = session;
        ShowDetail(session);
    }

    private void ShowDetail(PerformanceSession session)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;

        DetailTitle.Text = session.Name;
        DetailSubtitle.Text = $"{session.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} → {session.EndedAt.LocalDateTime:HH:mm:ss} · {session.ProcessName ?? "—"}";

        var stats = session.Stats;
        AvgFpsText.Text = stats.AverageFps.ToString("F1");
        Low1Text.Text = stats.OnePercentLow.ToString("F1");
        Low01Text.Text = stats.PointOnePercentLow.ToString("F1");
        StutterText.Text = $"{stats.StutterPercent:F1}%";
        DurationText.Text = FormatDuration(stats.DurationSeconds);
        FramesText.Text = stats.FrameCount.ToString("N0");
        MedianText.Text = stats.MedianFrameTimeMs.ToString("F2") + " ms";
        MaxText.Text = stats.MaxFrameTimeMs.ToString("F2") + " ms";

        Chart.SetData(session.FrameTimes, stats.MedianFrameTimeMs, stats.MedianFrameTimeMs * SessionCalculator.StutterFactor);
        FillSpikes(session);
        FillCompareOptions(session);
    }

    private void FillSpikes(PerformanceSession session)
    {
        SpikeList.Children.Clear();
        var frames = session.FrameTimes;
        double threshold = session.Stats.MedianFrameTimeMs * SessionCalculator.StutterFactor;
        if (frames.Length == 0 || threshold <= 0)
        {
            SpikeList.Children.Add(new TextBlock
            {
                Text = Loc.Inst["sessions.noSpikes"],
                Style = (Style)FindResource("Text.Muted"),
            });
            return;
        }

        var spikes = new List<(int Index, double Ms)>();
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] > threshold) spikes.Add((i, frames[i]));
        }

        if (spikes.Count == 0)
        {
            SpikeList.Children.Add(new TextBlock
            {
                Text = Loc.Inst["sessions.noSpikes"],
                Style = (Style)FindResource("Text.Muted"),
            });
            return;
        }

        var times = new double[frames.Length];
        double elapsed = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            times[i] = elapsed;
            elapsed += frames[i];
        }

        foreach (var (index, ms) in spikes.OrderByDescending(s => s.Ms).Take(5))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var label = new TextBlock
            {
                Text = Loc.Inst.Format("sessions.frameAt", index + 1, TimeSpan.FromMilliseconds(times[index]).ToString(@"hh\:mm\:ss\.fff")),
                Style = (Style)FindResource("Text.Muted"),
            };
            var msText = new TextBlock
            {
                Text = ms.ToString("F1") + " ms",
                Style = (Style)FindResource("Text.Numeric"),
                Foreground = (System.Windows.Media.Brush)FindResource("Brush.Danger"),
            };
            var ratio = new TextBlock
            {
                Text = "×" + (ms / session.Stats.MedianFrameTimeMs).ToString("F1"),
                Style = (Style)FindResource("Text.Numeric"),
                Foreground = (System.Windows.Media.Brush)FindResource("Brush.TextSecondary"),
            };
            Grid.SetColumn(msText, 1);
            Grid.SetColumn(ratio, 2);
            row.Children.Add(label);
            row.Children.Add(msText);
            row.Children.Add(ratio);
            SpikeList.Children.Add(row);
        }
    }

    private void FillCompareOptions(PerformanceSession current)
    {
        var options = new List<(string Id, string Label)> { (string.Empty, Loc.Inst["sessions.noBaseline"]) };
        foreach (var summary in _services.Sessions.Summaries)
        {
            if (summary.Id == current.Id) continue;
            options.Add((summary.Id, $"{summary.Name} · {summary.Stats.AverageFps:F0} FPS"));
        }
        _compareOptions = options;
        CompareCombo.ItemsSource = options.Select(o => o.Label).ToList();
        CompareCombo.SelectedIndex = 0;
        CompareFpsText.Text = "—";
        CompareLowText.Text = "—";
        CompareStutterText.Text = "—";
    }

    private List<(string Id, string Label)> _compareOptions = new();

    private void OnCompareChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded is null || CompareCombo.SelectedIndex < 0 || CompareCombo.SelectedIndex >= _compareOptions.Count) return;
        string id = _compareOptions[CompareCombo.SelectedIndex].Id;
        if (string.IsNullOrEmpty(id))
        {
            CompareFpsText.Text = "—";
            CompareLowText.Text = "—";
            CompareStutterText.Text = "—";
            return;
        }

        var baseline = _services.Sessions.Summaries.FirstOrDefault(s => s.Id == id);
        if (baseline is null) return;
        CompareFpsText.Text = Delta(_loaded.Stats.AverageFps, baseline.Stats.AverageFps);
        CompareLowText.Text = Delta(_loaded.Stats.OnePercentLow, baseline.Stats.OnePercentLow);
        CompareStutterText.Text = Delta(_loaded.Stats.StutterPercent, baseline.Stats.StutterPercent, invert: true);
        CompareFpsText.Foreground = BrushFor(_loaded.Stats.AverageFps, baseline.Stats.AverageFps);
        CompareLowText.Foreground = BrushFor(_loaded.Stats.OnePercentLow, baseline.Stats.OnePercentLow);
        CompareStutterText.Foreground = BrushFor(baseline.Stats.StutterPercent, _loaded.Stats.StutterPercent);
    }

    private static string Delta(double current, double baseline, bool invert = false)
    {
        if (baseline <= 0) return "—";
        double diff = (current - baseline) / baseline * 100;
        return (diff >= 0 ? "+" : string.Empty) + diff.ToString("F1") + "%";
    }

    private System.Windows.Media.Brush BrushFor(double current, double baseline)
    {
        if (baseline <= 0) return (System.Windows.Media.Brush)FindResource("Brush.TextMuted");
        if (Math.Abs(current - baseline) < baseline * 0.005) return (System.Windows.Media.Brush)FindResource("Brush.TextSecondary");
        return current >= baseline
            ? (System.Windows.Media.Brush)FindResource("Brush.Success")
            : (System.Windows.Media.Brush)FindResource("Brush.Danger");
    }

    private static string FormatDuration(double seconds)
    {
        if (seconds <= 0) return "—";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private void OnGoToDashboard(object sender, RoutedEventArgs e) => NavigateToDashboard?.Invoke();

    private void OnExportCsv(object sender, RoutedEventArgs e)
    {
        if (_loaded is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = SafeFileName(_loaded.Name) + ".csv",
            Title = Loc.Inst["sessions.exportCsv"],
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _services.Sessions.ExportCsv(_loaded, dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.Inst["msg.error"]);
        }
    }

    private static string SafeFileName(string name)
    {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "session" : name;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_loaded is null) return;
        var result = MessageBox.Show(
            Loc.Inst["sessions.deleteConfirm"],
            Loc.Inst["common.delete"],
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        _services.Sessions.Delete(_loaded.Id);
        _loaded = null;
        _selectedId = null;
        DetailPanel.Visibility = Visibility.Collapsed;
        Rebuild();
    }
}
