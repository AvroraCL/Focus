using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Focus.App.Controls;

/// <summary>Builds a compact two-column property editor used by the HUD editor sidebar.</summary>
public sealed class PropertySheet : StackPanel
{
    public PropertySheet()
    {
        Margin = new Thickness(0);
    }

    public event Action? ValueChanged;

    private void Notify() => ValueChanged?.Invoke();

    public void AddSection(string title)
    {
        if (Children.Count > 0)
        {
            Children.Add(new Border
            {
                Height = 1,
                Background = (Brush)Application.Current.FindResource("Brush.Border"),
                Margin = new Thickness(0, 10, 0, 10),
            });
        }
        Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("Brush.TextMuted"),
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    public void AddHint(string text)
    {
        Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.FindResource("Brush.TextMuted"),
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    private Grid Row(string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.FindResource("Brush.TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetColumn(labelBlock, 0);
        grid.Children.Add(labelBlock);
        Children.Add(grid);
        return grid;
    }

    private void Place(Grid grid, UIElement control)
    {
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
    }

    public void AddText(string label, string value, Action<string> setter, string? hint = null)
    {
        var grid = Row(label);
        var box = new TextBox { Text = value ?? string.Empty, Height = 28, FontSize = 12 };
        if (hint is not null) box.ToolTip = hint;
        void Commit()
        {
            setter(box.Text);
            Notify();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
            }
        };
        Place(grid, box);
    }

    public void AddNumber(string label, double value, Action<double> setter, double min = -100000, double max = 100000, int decimals = 0, string? suffix = null)
    {
        var grid = Row(label);
        var box = new TextBox
        {
            Text = value.ToString("F" + decimals, CultureInfo.CurrentCulture) + (suffix ?? string.Empty),
            Height = 28,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Tag = (min, max, decimals, suffix),
        };
        void Commit()
        {
            var raw = box.Text.Replace(suffix ?? string.Empty, string.Empty, StringComparison.Ordinal).Trim();
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.CurrentCulture, out double parsed) ||
                double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                double clamped = Math.Clamp(parsed, min, max);
                box.Text = clamped.ToString("F" + decimals, CultureInfo.CurrentCulture) + (suffix ?? string.Empty);
                setter(clamped);
                Notify();
            }
            else
            {
                box.Text = value.ToString("F" + decimals, CultureInfo.CurrentCulture) + (suffix ?? string.Empty);
            }
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Commit();
                Keyboard.ClearFocus();
            }
        };
        Place(grid, box);
    }

    public void AddBool(string label, bool value, Action<bool> setter)
    {
        var grid = Row(label);
        var check = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
        check.Checked += (_, _) => { setter(true); Notify(); };
        check.Unchecked += (_, _) => { setter(false); Notify(); };
        Place(grid, check);
    }

    public void AddColor(string label, string? value, Action<string?> setter, bool allowEmpty = true)
    {
        var grid = Row(label);
        var field = new ColorField { Value = value, AllowEmpty = allowEmpty };
        field.ValueChanged += v =>
        {
            setter(v);
            Notify();
        };
        Place(grid, field);
    }

    public void AddSlide(string label, double value, double min, double max, Action<double> setter, Func<double, string>? format = null)
    {
        var grid = Row(label);
        var stack = new Grid();
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 52, 0),
        };
        var readout = new TextBlock
        {
            Text = format?.Invoke(value) ?? value.ToString("F2", CultureInfo.CurrentCulture),
            FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            Foreground = (Brush)Application.Current.FindResource("Brush.TextMuted"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, e) =>
        {
            readout.Text = format?.Invoke(e.NewValue) ?? e.NewValue.ToString("F2", CultureInfo.CurrentCulture);
            setter(e.NewValue);
            Notify();
        };
        stack.Children.Add(slider);
        stack.Children.Add(readout);
        Place(grid, stack);
    }

    public void AddChoice<T>(string label, IReadOnlyList<(T Value, string Text)> options, T value, Action<T> setter)
    {
        var grid = Row(label);
        var combo = new ComboBox { Height = 28, FontSize = 11.5 };
        int selected = 0;
        for (int i = 0; i < options.Count; i++)
        {
            combo.Items.Add(options[i].Text);
            if (EqualityComparer<T>.Default.Equals(options[i].Value, value)) selected = i;
        }
        combo.SelectedIndex = selected;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && combo.SelectedIndex < options.Count)
            {
                setter(options[combo.SelectedIndex].Value);
                Notify();
            }
        };
        Place(grid, combo);
    }

    public void AddSegmented<T>(string label, IReadOnlyList<(T Value, string Text)> options, T value, Action<T> setter)
    {
        var grid = Row(label);
        var panel = new UniformGrid { Rows = 1, Columns = options.Count, Height = 28 };
        var buttons = new List<ToggleButton>();
        foreach (var (optionValue, text) in options)
        {
            var button = new ToggleButton
            {
                Content = text,
                Style = (Style)Application.Current.FindResource("Btn.Chip"),
                Height = 28,
                IsChecked = EqualityComparer<T>.Default.Equals(optionValue, value),
                Margin = new Thickness(0, 0, 4, 0),
            };
            button.Click += (_, _) =>
            {
                foreach (var other in buttons) other.IsChecked = ReferenceEquals(other, button);
                setter(optionValue);
                Notify();
            };
            buttons.Add(button);
            panel.Children.Add(button);
        }
        Place(grid, panel);
    }

    public void AddCustom(UIElement element)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(grid, element);
        Children.Add(grid);
    }

    public void AddWide(UIElement element)
    {
        Children.Add(new Border { Margin = new Thickness(0, 2, 0, 8), Child = element });
    }
}
