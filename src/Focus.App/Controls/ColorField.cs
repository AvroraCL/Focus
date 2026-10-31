using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Focus.App.Controls;

/// <summary>Compact color picker: swatch + hex editor + a small curated palette.</summary>
public sealed class ColorField : Grid
{
    private static readonly string[] Palette =
    {
        "#FFFFFFFF", "#FFE7EAEF", "#FF98A1AE", "#FF68717E", "#FF2F3843", "#FF000000",
        "#FF4C9AFF", "#FF5FD3A0", "#FFF0B45E", "#FFFF6B6B", "#FFB48CFF", "#FF7CD4E8",
        "#CC000000", "#99000000", "#66000000", "#33000000", "#CCFFFFFF", "#66FFFFFF",
    };

    private readonly Border _swatch;
    private readonly TextBox _hex;
    private readonly Popup _popup;
    private readonly Button _button;
    private bool _updating;

    public ColorField()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });

        var host = new Grid();
        _swatch = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(9, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        _hex = new TextBox
        {
            Text = "#FFFFFFFF",
            Padding = new Thickness(32, 6, 8, 6),
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 11.5,
        };
        _hex.LostFocus += (_, _) => ApplyHex();
        _hex.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) ApplyHex();
        };
        host.Children.Add(_hex);
        host.Children.Add(_swatch);
        SetColumn(host, 0);
        Children.Add(host);

        _button = new Button
        {
            Content = "",
            Style = (Style)Application.Current.FindResource("Btn.Icon"),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Width = 26,
            Height = 26,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        SetColumn(_button, 2);
        Children.Add(_button);

        _popup = new Popup
        {
            PlacementTarget = _button,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = BuildPalette(),
        };
        _button.Click += (_, _) => _popup.IsOpen = !_popup.IsOpen;
    }

    public event Action<string?>? ValueChanged;

    public string? Value
    {
        get => _hex.Text;
        set
        {
            if (value == _hex.Text) return;
            _updating = true;
            _hex.Text = value ?? string.Empty;
            UpdateSwatch();
            _updating = false;
        }
    }

    public bool AllowEmpty { get; set; }

    private UIElement BuildPalette()
    {
        var panel = new WrapPanel { Width = 186, Margin = new Thickness(2) };
        foreach (var color in Palette)
        {
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                Background = BrushFrom(color),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = color,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                SetValue(color);
                _popup.IsOpen = false;
            };
            panel.Children.Add(swatch);
        }

        var root = new StackPanel
        {
            Background = (Brush)Application.Current.FindResource("Brush.SurfaceRaised"),
        };
        root.Children.Add(panel);
        if (AllowEmpty)
        {
            var none = new Button
            {
                Content = Application.Current.TryFindResource("common.none"),
                Style = (Style)Application.Current.FindResource("Btn.Ghost"),
                Margin = new Thickness(6, 2, 6, 8),
            };
            none.Click += (_, _) =>
            {
                SetValue(null);
                _popup.IsOpen = false;
            };
            root.Children.Add(none);
        }

        return new Border
        {
            Background = (Brush)Application.Current.FindResource("Brush.SurfaceRaised"),
            BorderBrush = (Brush)Application.Current.FindResource("Brush.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3),
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.45,
                Color = Colors.Black,
            },
        };
    }

    private void SetValue(string? value)
    {
        _updating = true;
        _hex.Text = value ?? string.Empty;
        UpdateSwatch();
        _updating = false;
        ValueChanged?.Invoke(value);
    }

    private void ApplyHex()
    {
        if (_updating) return;
        var text = _hex.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            if (AllowEmpty)
            {
                UpdateSwatch();
                ValueChanged?.Invoke(null);
            }
            else
            {
                _hex.Text = "#FFFFFFFF";
            }
            return;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            string normalized = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _updating = true;
            _hex.Text = normalized;
            _updating = false;
            UpdateSwatch();
            ValueChanged?.Invoke(normalized);
        }
        catch
        {
            UpdateSwatch();
        }
    }

    private void UpdateSwatch()
    {
        try
        {
            _swatch.Background = string.IsNullOrWhiteSpace(_hex.Text) ? Brushes.Transparent : BrushFrom(_hex.Text);
        }
        catch
        {
            _swatch.Background = Brushes.Transparent;
        }
    }

    private static Brush BrushFrom(string value)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        catch
        {
            return Brushes.Transparent;
        }
    }
}
