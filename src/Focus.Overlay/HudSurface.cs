using System.Windows;
using System.Windows.Media;
using Focus.Core.Profiles;
using Focus.Hud;
using Focus.Hud.Rendering;

namespace Focus.Overlay;

/// <summary>Draws a HUD profile into the overlay window using the shared renderer.</summary>
public sealed class HudSurface : FrameworkElement
{
    public HudProfile? Profile { get; set; }
    public IHudDataSource Data { get; set; } = EmptyHudDataSource.Instance;

    protected override void OnRender(DrawingContext dc)
    {
        if (Profile is null) return;

        double scaleX = ActualWidth / Math.Max(1, Profile.CanvasWidth);
        double scaleY = ActualHeight / Math.Max(1, Profile.CanvasHeight);
        double scale = Math.Min(scaleX, scaleY);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0) return;

        dc.PushTransform(new ScaleTransform(scale, scale));
        HudRenderer.Render(dc, Profile, Data, VisualTreeHelper.GetDpi(this).PixelsPerDip, dimUnavailable: false);
        dc.Pop();
    }
}
