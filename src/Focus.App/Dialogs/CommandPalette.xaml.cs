using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Focus.App.Dialogs;

public sealed record PaletteCommand(string Label, string Shortcut, string SearchTerms, Action Execute);

public partial class CommandPalette : Window
{
    private readonly IReadOnlyList<PaletteCommand> _commands;
    public PaletteCommand? SelectedCommand { get; private set; }

    public CommandPalette(Window owner, IReadOnlyList<PaletteCommand> commands)
    {
        _commands = commands;
        Owner = owner;
        InitializeComponent();
        Filter();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (Results is not null) Filter();
    }

    private void Filter()
    {
        string query = SearchBox.Text.Trim();
        var matches = _commands.Where(c => (c.Label + " " + c.SearchTerms).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Results.ItemsSource = matches;
        Results.SelectedIndex = matches.Count > 0 ? 0 : -1;
        EmptyState.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ExecuteSelected()
    {
        if (Results.SelectedItem is not PaletteCommand command) return;
        SelectedCommand = command;
        DialogResult = true;
    }

    private void OnExecute(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(Results, source) is ListBoxItem)
            ExecuteSelected();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { ExecuteSelected(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up)
        {
            if (Results.Items.Count > 0)
            {
                Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1);
                Results.ScrollIntoView(Results.SelectedItem);
            }
            e.Handled = true;
        }
    }
}
