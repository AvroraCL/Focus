using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Focus.Hud;
using Focus.Hud.Formatting;
using Focus.Hud.Presets;
using Focus.Hud.Rendering;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/hud-presets");
        Directory.CreateDirectory(output);
        var presets = new[] { PresetLibrary.Swiss(), PresetLibrary.Competitive(), PresetLibrary.Hardware(),
            PresetLibrary.Thermal(), PresetLibrary.Benchmark(), PresetLibrary.Ribbon() };
        string[] names = { "01  瑞士极简", "02  竞技横条", "03  硬件侧栏", "04  温控面板", "05  跑分分析", "06  底部信息带" };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brush("#F5F4EF"), null, new Rect(0, 0, 1460, 1150));
            Text(dc, "Focus. / HUD", 40, 28, 38, "#181818");
            Text(dc, "6 NEW PRESETS  /  示例数据，仅用于展示布局", 40, 84, 15, "#686862");
            double[] rowTop = { 135, 358, 808 };
            double[] rowHeight = { 183, 410, 302 };
            for (int i = 0; i < presets.Length; i++)
            {
                var p = presets[i];
                // Match the normalization applied during import/editor/IPC serialization.
                p.Normalize();
                Validate(p, SampleHudDataSource.Instance);
                Validate(p, EmptyHudDataSource.Instance);
                var exported = p.Clone(); exported.Name = names[i][4..];
                File.WriteAllText(Path.Combine(output, p.Id + ".json"), ProfileCodec.Serialize(exported));
                Render(p, SampleHudDataSource.Instance, Path.Combine(output, p.Id + ".png"));
                Render(p, EmptyHudDataSource.Instance, Path.Combine(output, p.Id + "-empty.png"));
                int row = i / 2;
                double x = 40 + i % 2 * 710, y = rowTop[row];
                Text(dc, names[i], x, y, 18, "#181818");
                var bounds = new Rect(x, y + 34, 670, rowHeight[row] - 34);
                dc.DrawRectangle(Brush("#343B43"), null, bounds);
                double scale = Math.Min(1, Math.Min((bounds.Width - 24) / p.CanvasWidth, (bounds.Height - 24) / p.CanvasHeight));
                dc.PushTransform(new TranslateTransform(bounds.X + (bounds.Width - p.CanvasWidth * scale) / 2,
                    bounds.Y + (bounds.Height - p.CanvasHeight * scale) / 2));
                dc.PushTransform(new ScaleTransform(scale, scale));
                HudRenderer.Render(dc, p, SampleHudDataSource.Instance, 1, dimUnavailable: true);
                dc.Pop(); dc.Pop();
            }
        }
        Save(visual, 1460, 1150, Path.Combine(output, "presets-overview.png"));
        CheckStaticLabelContrast();
        Console.WriteLine("PASS: six presets normalized, bounds/text fit, sample/empty rendering, JSON exports and static-label contrast. " + output);
    }

    private static void Validate(HudProfile profile, IHudDataSource data)
    {
        foreach (var e in profile.Elements)
        {
            if (e.X < 0 || e.Y < 0 || e.X + e.Width > profile.CanvasWidth || e.Y + e.Height > profile.CanvasHeight)
                throw new Exception(profile.Id + ": element outside canvas: " + e.Name);
            if (e.Kind != HudElementKind.Text) continue;
            string text = MetricFormatter.ApplyTemplate(e.Template, e.Label,
                MetricFormatter.FormatValue(e.Metric, data.Get(e.Metric), e.Decimals, e.UnitOverride, e.UnitDivisor, e.ShowUnit));
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily(e.FontFamily), FontStyles.Normal, FontWeight.FromOpenTypeWeight(e.FontWeight), FontStretches.Normal),
                e.FontSize, Brushes.White, 1);
            if (ft.Width > e.Width - e.PaddingLeft - e.PaddingRight || ft.Height > e.Height - e.PaddingTop - e.PaddingBottom)
                throw new Exception($"{profile.Id}: text does not fit: {e.Name} ({ft.Width:F1} × {ft.Height:F1})");
        }
    }

    private static void Render(HudProfile profile, IHudDataSource data, string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) HudRenderer.Render(dc, profile, data, 1, dimUnavailable: true);
        Save(visual, (int)profile.CanvasWidth, (int)profile.CanvasHeight, path);
    }

    private static void CheckStaticLabelContrast()
    {
        var profile = new HudProfile { CanvasWidth = 100, CanvasHeight = 40 };
        profile.Elements.Add(new HudElement { Template = "FPS", Metric = MetricId.None, FontSize = 24, Width = 100, Height = 40 });
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) HudRenderer.Render(dc, profile, EmptyHudDataSource.Instance, 1, true);
        var bitmap = new RenderTargetBitmap(100, 40, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        byte[] pixels = new byte[100 * 40 * 4]; bitmap.CopyPixels(pixels, 400, 0);
        if (!Enumerable.Range(0, 4000).Any(i => pixels[i * 4 + 3] > 240)) throw new Exception("Static labels were dimmed as missing metrics");
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    private static void Text(DrawingContext dc, string text, double x, double y, double size, string color) =>
        dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Arial, Microsoft YaHei UI"), size, Brush(color), 1), new Point(x, y));
    private static void Save(Visual visual, int width, int height, string path)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
