using System.Windows;
using System.Windows.Input;
using Focus.App.Services;
using Focus.Telemetry.System;

namespace Focus.App.Dialogs;

public partial class ProcessPickerWindow : Window
{
    private readonly AppServices _services;
    private List<ProcessInfo> _processes = new();

    public ProcessInfo? SelectedProcess { get; private set; }

    public ProcessPickerWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Refresh();
            SearchBox.Focus();
        };
    }

    private void Refresh()
    {
        _processes = ProcessLocator.GetCandidates().ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string query = SearchBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(query)
            ? _processes
            : _processes.Where(p =>
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.WindowTitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        ProcessList.ItemsSource = filtered;
        EmptyHint.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (filtered.Count > 0) ProcessList.SelectedIndex = 0;
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void OnRefresh(object sender, RoutedEventArgs e) => Refresh();

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfo info)
        {
            SelectedProcess = info;
            DialogResult = true;
            Close();
        }
    }

    private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProcessList.SelectedItem is ProcessInfo info)
        {
            SelectedProcess = info;
            DialogResult = true;
            Close();
        }
    }
}
