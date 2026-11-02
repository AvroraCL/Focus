using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Focus.App.Dialogs;
using Focus.App.Services;
using Focus.App.Views;

public static class UiCheck
{
    [STAThread]
    public static int Main(string[] args)
    {
        string outputDirectory = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-preview");
        Directory.CreateDirectory(outputDirectory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Focus;component/Themes/Dark.xaml", UriKind.Relative) });
        var owner = new Window { Width = 800, Height = 600, ShowInTaskbar = false };
        owner.Show();
        int executions = 0;
        var palette = new CommandPalette(owner, new[] {
            new PaletteCommand("HUD 编辑器", "Alt + 2", "editor", () => executions++),
            new PaletteCommand("设置", "Alt + 5", "settings", () => executions += 10)
        });
        Exception? failure = null;
        palette.Loaded += (_, _) => palette.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var search = (TextBox)palette.FindName("SearchBox");
                var results = (ListBox)palette.FindName("Results");
                search.Text = "SETTINGS";
                if (results.Items.Count != 1 || ((PaletteCommand)results.SelectedItem).SearchTerms != "settings") throw new Exception("Search selection failed");
                search.Text = "missing";
                if (results.Items.Count != 0 || ((TextBlock)palette.FindName("EmptyState")).Visibility != Visibility.Visible) throw new Exception("Empty state failed");
                search.Text = "";
                void Press(Key key) => palette.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(palette), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Press(Key.Down);
                if (results.SelectedIndex != 1) throw new Exception("Down selection failed");
                Press(Key.Up);
                if (results.SelectedIndex != 0) throw new Exception("Up selection failed");
                palette.UpdateLayout();
                var placeholder = (TextBlock)search.Template.FindName("Placeholder", search);
                if (string.IsNullOrWhiteSpace(placeholder.Text) || placeholder.Visibility != Visibility.Visible) throw new Exception("Search placeholder missing");
                var image = new RenderTargetBitmap((int)palette.ActualWidth, (int)palette.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(palette);
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
                using (var output = File.Create(Path.Combine(outputDirectory, "commands.png"))) png.Save(output);
                Press(Key.Enter);
            }
            catch (Exception e) { failure = e; palette.Close(); }
        }), DispatcherPriority.ApplicationIdle);
        bool? accepted = palette.ShowDialog();
        if (failure != null) { Console.Error.WriteLine(failure); owner.Close(); app.Shutdown(); return 1; }
        if (accepted != true || palette.SelectedCommand?.SearchTerms != "editor") throw new Exception("Enter did not accept selected command");
        palette.SelectedCommand.Execute();
        if (executions != 1) throw new Exception("Wrong command executed");
        var cancelled = new CommandPalette(owner, new[] { new PaletteCommand("Cancel test", "", "cancel", () => executions++) });
        cancelled.Loaded += (_, _) => cancelled.Dispatcher.BeginInvoke(new Action(() => cancelled.RaiseEvent(
            new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(cancelled), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent })));
        cancelled.ShowDialog();
        if (cancelled.SelectedCommand is not null || executions != 1) throw new Exception("Escape executed a command");
        // Construct the actual editor without loading hardware or showing it. Exercise its layout toggle.
        var services = new AppServices();
        var editor = new EditorPage(services);
        var toggle = (System.Windows.Controls.Primitives.ToggleButton)editor.FindName("FocusCanvasToggle");
        var layers = (ColumnDefinition)editor.FindName("LayerColumn");
        var properties = (ColumnDefinition)editor.FindName("PropertyColumn");
        layers.Width = new GridLength(222); properties.Width = new GridLength(312);
        toggle.IsChecked = true;
        if (layers.Width.Value != 0 || properties.Width.Value != 0) throw new Exception("Focus canvas did not hide panels");
        toggle.IsChecked = false;
        if (layers.Width.Value != 222 || properties.Width.Value != 312) throw new Exception("Focus canvas did not restore widths");
        editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        services.Telemetry.Dispose();
        owner.Close(); app.Shutdown();
        Console.WriteLine("PASS: palette search, empty state, arrows, Enter, Escape, action dispatch, focus canvas and panel width restoration.");
        return 0;
    }
}
