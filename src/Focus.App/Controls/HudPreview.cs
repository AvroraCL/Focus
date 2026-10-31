using System.Windows;
using System.Windows.Media;
using Focus.Core.Profiles;
using Focus.Hud;
using Focus.Hud.Rendering;

namespace Focus.App.Controls;

/// <summary>Renders a HUD profile as a preview thumbnail on a checkerboard.</summary>
public sealed class HudPreview : FrameworkElement
{
    private static readonly Brush CheckerA = CreateBrush(Color.FromRgb(0x14, 0x18, 0x1E));
    private static readonly Brush CheckerB = CreateBrush(Color.FromRgb(0x18, 0x1D, 0x24));

    private static Brush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public HudProfile? Profile { get; set; }
    public IHudDataSource Data { get; set; } = EmptyHudDataSource.Instance;
    public bool ShowCheckerboard { get; set; } = true;
    public double Padding { get; set; } = 10;

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (ShowCheckerboard)
        {
            const double cell = 8;
            for (int y = 0; y * cell < h; y++)
            {
                for (int x = 0; x * cell < w; x++)
                {
                    var brush = ((x + y) & 1) == 0 ? CheckerA : CheckerB;
                    dc.DrawRectangle(brush, null, new Rect(x * cell, y * cell, cell, cell));
                }
            }
        }

        if (Profile is null) return;

        double availableW = Math.Max(1, w - Padding * 2);
        double availableH = Math.Max(1, h - Padding * 2);
        double scale = Math.Min(availableW / Profile.CanvasWidth, availableH / Profile.CanvasHeight);
        if (!(scale > 0) || double.IsInfinity(scale)) return;

        double drawW = Profile.CanvasWidth * scale;
        double drawH = Profile.CanvasHeight * scale;
        double originX = (w - drawW) / 2;
        double originY = (h - drawH) / 2;

        dc.PushTransform(new TranslateTransform(originX, originY));
        dc.PushTransform(new ScaleTransform(scale, scale));
        HudRenderer.Render(dc, Profile, Data, VisualTreeHelper.GetDpi(this).PixelsPerDip, dimUnavailable: true);
        dc.Pop();
        dc.Pop();
    }
}
