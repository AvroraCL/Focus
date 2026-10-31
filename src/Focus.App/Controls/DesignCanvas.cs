using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Focus.Core.Metrics;
using Focus.Core.Profiles;
using Focus.Hud;
using Focus.Hud.Rendering;

namespace Focus.App.Controls;

/// <summary>
/// Interactive HUD design surface: renders the profile with the exact same renderer used by the
/// in-game overlay, and provides selection, dragging, resizing, snapping and zooming.
/// </summary>
public sealed class DesignCanvas : FrameworkElement
{
    private enum DragMode
    {
        None,
        Move,
        Resize,
        Marquee,
        Pan,
    }

    private const double HandleSize = 7;
    private const double HandleHitRadius = 8;

    private readonly List<string> _selected = new();
    private readonly Dictionary<string, (double X, double Y, double W, double H)> _startBounds = new();
    private readonly List<(bool Vertical, double Position)> _guides = new();
    private DragMode _mode;
    private Point _startScreen;
    private Point _startCanvas;
    private double _panStartX, _panStartY;
    private int _resizeHandle = -1;
    private Rect _marquee;
    private Rect _selectionBounds;
    private bool _didMutate;
    private bool _spaceDown;
    private double _zoom = 1;
    private double _panX, _panY;

    public DesignCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Background = (Brush)Application.Current.FindResource("Brush.Window");
        SnapsToDevicePixels = true;
    }

    public Brush Background { get; set; }

    public bool IsInteracting => _mode != DragMode.None;

    public HudProfile? Profile { get; set; }
    public IHudDataSource Data { get; set; } = EmptyHudDataSource.Instance;
    public bool GridEnabled { get; set; } = true;
    public bool SnapEnabled { get; set; } = true;
    public double GridSize { get; set; } = 8;

    public event Action? BeginMutation;
    public event Action? Changed;
    public event Action? SelectionChanged;

    public IReadOnlyList<string> SelectedIds => _selected;

    public double Zoom
    {
        get => _zoom;
        set
        {
            double clamped = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(clamped - _zoom) < 0.0001) return;
            _zoom = clamped;
            InvalidateVisual();
        }
    }

    public void ResetView()
    {
        _panX = 0;
        _panY = 0;
        InvalidateVisual();
    }

    public void ZoomToFit()
    {
        if (Profile is null || ActualWidth < 20 || ActualHeight < 20) return;
        double fit = Math.Min((ActualWidth - 80) / Profile.CanvasWidth, (ActualHeight - 80) / Profile.CanvasHeight);
        Zoom = Math.Clamp(fit, 0.25, 2.0);
        _panX = 0;
        _panY = 0;
        InvalidateVisual();
    }

    public void SetSelection(IEnumerable<string> ids, bool notify = true)
    {
        _selected.Clear();
        _selected.AddRange(ids);
        UpdateSelectionBounds();
        InvalidateVisual();
        if (notify) SelectionChanged?.Invoke();
    }

    public void RefreshSelection()
    {
        UpdateSelectionBounds();
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (Profile is null) return;
        _selected.Clear();
        _selected.AddRange(Profile.Elements.Select(e => e.Id));
        UpdateSelectionBounds();
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    private Point Origin
    {
        get
        {
            if (Profile is null) return new Point(_panX, _panY);
            double w = Profile.CanvasWidth * _zoom;
            double h = Profile.CanvasHeight * _zoom;
            return new Point((ActualWidth - w) / 2 + _panX, (ActualHeight - h) / 2 + _panY);
        }
    }

    public Point ScreenToCanvas(Point screen)
    {
        var origin = Origin;
        return new Point((screen.X - origin.X) / _zoom, (screen.Y - origin.Y) / _zoom);
    }

    public Point CanvasToScreen(Point canvas)
    {
        var origin = Origin;
        return new Point(origin.X + canvas.X * _zoom, origin.Y + canvas.Y * _zoom);
    }

    private void UpdateSelectionBounds()
    {
        if (Profile is null || _selected.Count == 0)
        {
            _selectionBounds = Rect.Empty;
            return;
        }
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var id in _selected)
        {
            var element = Profile.FindElement(id);
            if (element is null) continue;
            minX = Math.Min(minX, element.X);
            minY = Math.Min(minY, element.Y);
            maxX = Math.Max(maxX, element.X + element.Width);
            maxY = Math.Max(maxY, element.Y + element.Height);
        }
        _selectionBounds = _selected.Count == 0 ? Rect.Empty : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // ------------------------------------------------------------------ render

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Profile is null) return;

        var origin = Origin;
        double cw = Profile.CanvasWidth * _zoom;
        double ch = Profile.CanvasHeight * _zoom;
        var canvasRect = new Rect(origin.X, origin.Y, cw, ch);

        // soft drop shadow
        for (int i = 3; i >= 1; i--)
        {
            var shadow = new SolidColorBrush(Color.FromArgb((byte)(8 * i), 0, 0, 0));
            shadow.Freeze();
            dc.DrawRoundedRectangle(shadow, null, new Rect(canvasRect.X - i * 2, canvasRect.Y + i, cw + i * 4, ch + i * 2), 3, 3);
        }

        DrawCheckerboard(dc, canvasRect);

        if (GridEnabled)
        {
            DrawGrid(dc, canvasRect);
        }

        dc.PushClip(new RectangleGeometry(canvasRect));
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(_zoom, _zoom));
        HudRenderer.Render(dc, Profile, Data, VisualTreeHelper.GetDpi(this).PixelsPerDip, dimUnavailable: true);
        dc.Pop();
        dc.Pop();
        dc.Pop();

        var border = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF));
        border.Freeze();
        dc.DrawRectangle(null, new Pen(border, 1), canvasRect);

        DrawSelection(dc);
        DrawGuides(dc, origin);
        if (_mode == DragMode.Marquee)
        {
            var fill = new SolidColorBrush(Color.FromArgb(0x22, 0x4C, 0x9A, 0xFF));
            var edge = new SolidColorBrush(Color.FromArgb(0xCC, 0x4C, 0x9A, 0xFF));
            fill.Freeze();
            edge.Freeze();
            dc.DrawRectangle(fill, new Pen(edge, 1) { DashStyle = DashStyles.Dash }, _marquee);
        }
    }

    private void DrawCheckerboard(DrawingContext dc, Rect rect)
    {
        dc.PushClip(new RectangleGeometry(rect));
        var a = new SolidColorBrush(Color.FromRgb(0x0F, 0x12, 0x16));
        var b = new SolidColorBrush(Color.FromRgb(0x12, 0x15, 0x1A));
        a.Freeze();
        b.Freeze();
        const double cell = 8;
        int cols = (int)Math.Ceiling(rect.Width / cell);
        int rows = (int)Math.Ceiling(rect.Height / cell);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                dc.DrawRectangle(((x + y) & 1) == 0 ? a : b, null, new Rect(rect.X + x * cell, rect.Y + y * cell, cell, cell));
            }
        }
        dc.Pop();
    }

    private void DrawGrid(DrawingContext dc, Rect rect)
    {
        double step = GridSize * _zoom;
        if (step < 5) step *= 4;
        if (step < 5) return;

        var minor = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        minor.Freeze();
        var major = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        major.Freeze();

        dc.PushClip(new RectangleGeometry(rect));
        int index = 0;
        for (double x = rect.X; x <= rect.Right; x += step, index++)
        {
            var pen = new Pen(index % 4 == 0 ? major : minor, 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(Math.Round(x) + 0.5, rect.Top), new Point(Math.Round(x) + 0.5, rect.Bottom));
        }
        index = 0;
        for (double y = rect.Y; y <= rect.Bottom; y += step, index++)
        {
            var pen = new Pen(index % 4 == 0 ? major : minor, 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(rect.Left, Math.Round(y) + 0.5), new Point(rect.Right, Math.Round(y) + 0.5));
        }
        dc.Pop();
    }

    private void DrawSelection(DrawingContext dc)
    {
        if (Profile is null || _selected.Count == 0) return;

        var accent = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF));
        accent.Freeze();
        var pen = new Pen(accent, 1.2);
        pen.Freeze();
        var soft = new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0x4C, 0x9A, 0xFF)), 1);
        soft.Freeze();

        foreach (var id in _selected)
        {
            var element = Profile.FindElement(id);
            if (element is null) continue;
            var topLeft = CanvasToScreen(new Point(element.X, element.Y));
            var rect = new Rect(topLeft.X, topLeft.Y, element.Width * _zoom, element.Height * _zoom);
            dc.DrawRectangle(null, _selected.Count > 1 ? soft : pen, rect);
        }

        var boundsTopLeft = CanvasToScreen(new Point(_selectionBounds.X, _selectionBounds.Y));
        var boundsRect = new Rect(boundsTopLeft.X, boundsTopLeft.Y, _selectionBounds.Width * _zoom, _selectionBounds.Height * _zoom);

        if (_selected.Count > 1)
        {
            var dashed = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
            dashed.Freeze();
            dc.DrawRectangle(null, dashed, boundsRect);
        }

        if (_selected.Count == 1)
        {
            foreach (var point in HandlePoints(boundsRect))
            {
                var handleRect = new Rect(point.X - HandleSize / 2, point.Y - HandleSize / 2, HandleSize, HandleSize);
                dc.DrawRectangle(Brushes.White, pen, handleRect);
            }
        }
    }

    private void DrawGuides(DrawingContext dc, Point origin)
    {
        if (_guides.Count == 0) return;
        var brush = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0x59, 0x8A));
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        double cw = Profile!.CanvasWidth * _zoom;
        double ch = Profile.CanvasHeight * _zoom;
        foreach (var (vertical, position) in _guides)
        {
            if (vertical)
            {
                double x = Math.Round(origin.X + position * _zoom) + 0.5;
                dc.DrawLine(pen, new Point(x, origin.Y - 8), new Point(x, origin.Y + ch + 8));
            }
            else
            {
                double y = Math.Round(origin.Y + position * _zoom) + 0.5;
                dc.DrawLine(pen, new Point(origin.X - 8, y), new Point(origin.X + cw + 8, y));
            }
        }
    }

    private static IEnumerable<Point> HandlePoints(Rect rect)
    {
        double cx = rect.Left + rect.Width / 2;
        double cy = rect.Top + rect.Height / 2;
        yield return new Point(rect.Left, rect.Top);
        yield return new Point(cx, rect.Top);
        yield return new Point(rect.Right, rect.Top);
        yield return new Point(rect.Right, cy);
        yield return new Point(rect.Right, rect.Bottom);
        yield return new Point(cx, rect.Bottom);
        yield return new Point(rect.Left, rect.Bottom);
        yield return new Point(rect.Left, cy);
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Profile is null) return;
        Focus();
        CaptureMouse();
        _startScreen = e.GetPosition(this);
        _startCanvas = ScreenToCanvas(_startScreen);
        _didMutate = false;

        if (_spaceDown)
        {
            BeginPan();
            return;
        }

        int handle = HitHandle(_startScreen);
        if (handle >= 0)
        {
            BeginMutation?.Invoke();
            _mode = DragMode.Resize;
            _resizeHandle = handle;
            SnapshotBounds();
            return;
        }

        var hit = HitElement(_startCanvas);
        bool additive = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 || (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (hit is null)
        {
            if (!additive)
            {
                _selected.Clear();
                SelectionChanged?.Invoke();
            }
            _mode = DragMode.Marquee;
            _marquee = new Rect(_startScreen, _startScreen);
            UpdateSelectionBounds();
            InvalidateVisual();
            return;
        }

        if (additive)
        {
            if (_selected.Contains(hit.Id)) _selected.Remove(hit.Id);
            else _selected.Add(hit.Id);
        }
        else if (!_selected.Contains(hit.Id))
        {
            _selected.Clear();
            _selected.Add(hit.Id);
            ExpandGroups();
        }

        UpdateSelectionBounds();
        SelectionChanged?.Invoke();

        if (_selected.Count > 0)
        {
            BeginMutation?.Invoke();
            _mode = DragMode.Move;
            SnapshotBounds();
        }
        InvalidateVisual();
    }

    private void ExpandGroups()
    {
        if (Profile is null) return;
        var groups = new HashSet<string>();
        foreach (var id in _selected.ToList())
        {
            var element = Profile.FindElement(id);
            if (element?.GroupId is { } gid) groups.Add(gid);
        }
        if (groups.Count == 0) return;
        foreach (var element in Profile.Elements)
        {
            if (element.GroupId is { } gid && groups.Contains(gid) && !_selected.Contains(element.Id))
            {
                _selected.Add(element.Id);
            }
        }
    }

    private void SnapshotBounds()
    {
        _startBounds.Clear();
        if (Profile is null) return;
        foreach (var id in _selected)
        {
            var element = Profile.FindElement(id);
            if (element is not null) _startBounds[id] = (element.X, element.Y, element.Width, element.Height);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var screen = e.GetPosition(this);

        switch (_mode)
        {
            case DragMode.None:
                UpdateCursor(screen);
                return;

            case DragMode.Pan:
            {
                var delta = screen - _startScreen;
                _panX = _panStartX + delta.X;
                _panY = _panStartY + delta.Y;
                InvalidateVisual();
                return;
            }

            case DragMode.Move:
                MoveSelection(screen);
                return;

            case DragMode.Resize:
                ResizeSelection(screen);
                return;

            case DragMode.Marquee:
            {
                _marquee = new Rect(_startScreen, screen);
                var canvasTopLeft = ScreenToCanvas(new Point(_marquee.Left, _marquee.Top));
                var canvasBottomRight = ScreenToCanvas(new Point(_marquee.Right, _marquee.Bottom));
                var canvasRect = new Rect(canvasTopLeft, canvasBottomRight);
                var ids = Profile?.Elements.Where(el => canvasRect.IntersectsWith(new Rect(el.X, el.Y, el.Width, el.Height))).Select(el => el.Id).ToList() ?? new List<string>();
                _selected.Clear();
                _selected.AddRange(ids);
                UpdateSelectionBounds();
                SelectionChanged?.Invoke();
                InvalidateVisual();
                return;
            }
        }
    }

    private void UpdateCursor(Point screen)
    {
        if (Profile is null) return;
        int handle = HitHandle(screen);
        if (handle >= 0)
        {
            Cursor = handle switch
            {
                0 or 4 => Cursors.SizeNWSE,
                2 or 6 => Cursors.SizeNESW,
                1 or 5 => Cursors.SizeNS,
                3 or 7 => Cursors.SizeWE,
                _ => Cursors.Arrow,
            };
            return;
        }
        var element = HitElement(ScreenToCanvas(screen));
        Cursor = element is null ? Cursors.Arrow : Cursors.SizeAll;
    }

    private void MoveSelection(Point screen)
    {
        if (Profile is null || _startBounds.Count == 0) return;
        double dx = (screen.X - _startScreen.X) / _zoom;
        double dy = (screen.Y - _startScreen.Y) / _zoom;

        if (GridEnabled && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            var first = _startBounds.Values.First();
            dx = Math.Round((first.X + dx) / GridSize) * GridSize - first.X;
            dy = Math.Round((first.Y + dy) / GridSize) * GridSize - first.Y;
        }

        _guides.Clear();
        if (SnapEnabled && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            (dx, dy) = ApplySnapping(dx, dy);
        }

        foreach (var (id, bounds) in _startBounds)
        {
            var element = Profile.FindElement(id);
            if (element is null || element.Locked) continue;
            element.X = Math.Round(bounds.X + dx, 2);
            element.Y = Math.Round(bounds.Y + dy, 2);
        }

        _didMutate = true;
        UpdateSelectionBounds();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private (double Dx, double Dy) ApplySnapping(double dx, double dy)
    {
        if (Profile is null || _selected.Count == 0) return (dx, dy);
        double threshold = 6 / _zoom;

        var bounds = new Rect(
            _selectionBounds.X + dx, _selectionBounds.Y + dy,
            _selectionBounds.Width, _selectionBounds.Height);

        double bestX = threshold + 1, bestY = threshold + 1;
        double snapDx = dx, snapDy = dy;

        var xTargets = new List<double> { 0, Profile.CanvasWidth / 2, Profile.CanvasWidth };
        var yTargets = new List<double> { 0, Profile.CanvasHeight / 2, Profile.CanvasHeight };

        foreach (var element in Profile.Elements)
        {
            if (_selected.Contains(element.Id)) continue;
            xTargets.Add(element.X);
            xTargets.Add(element.X + element.Width / 2);
            xTargets.Add(element.X + element.Width);
            yTargets.Add(element.Y);
            yTargets.Add(element.Y + element.Height / 2);
            yTargets.Add(element.Y + element.Height);
        }

        var xEdges = new[] { bounds.Left, bounds.Left + bounds.Width / 2, bounds.Right };
        var yEdges = new[] { bounds.Top, bounds.Top + bounds.Height / 2, bounds.Bottom };

        foreach (var target in xTargets)
        {
            foreach (var edge in xEdges)
            {
                double diff = target - edge;
                if (Math.Abs(diff) < Math.Abs(bestX))
                {
                    bestX = diff;
                    snapDx = dx + diff;
                }
            }
        }

        foreach (var target in yTargets)
        {
            foreach (var edge in yEdges)
            {
                double diff = target - edge;
                if (Math.Abs(diff) < Math.Abs(bestY))
                {
                    bestY = diff;
                    snapDy = dy + diff;
                }
            }
        }

        if (Math.Abs(bestX) <= threshold)
        {
            dx = snapDx;
            _guides.Add((true, bounds.Left + bestX));
        }
        if (Math.Abs(bestY) <= threshold)
        {
            dy = snapDy;
            _guides.Add((false, bounds.Top + bestY));
        }

        return (dx, dy);
    }

    private void ResizeSelection(Point screen)
    {
        if (Profile is null || _selected.Count != 1 || _startBounds.Count == 0) return;
        string id = _selected[0];
        var element = Profile.FindElement(id);
        if (element is null || element.Locked) return;
        var start = _startBounds[id];

        double mouseX = ScreenToCanvas(screen).X;
        double mouseY = ScreenToCanvas(screen).Y;

        double left = start.X, top = start.Y, right = start.X + start.W, bottom = start.Y + start.H;

        if (_resizeHandle is 0 or 6 or 7) left = Math.Min(mouseX, right - 4);
        if (_resizeHandle is 2 or 3 or 4) right = Math.Max(mouseX, left + 4);
        if (_resizeHandle is 0 or 1 or 2) top = Math.Min(mouseY, bottom - 4);
        if (_resizeHandle is 4 or 5 or 6) bottom = Math.Max(mouseY, top + 4);

        if (GridEnabled && (Keyboard.Modifiers & ModifierKeys.Alt) == 0)
        {
            left = Math.Round(left / GridSize) * GridSize;
            top = Math.Round(top / GridSize) * GridSize;
            right = Math.Round(right / GridSize) * GridSize;
            bottom = Math.Round(bottom / GridSize) * GridSize;
        }

        element.X = Math.Round(left, 2);
        element.Y = Math.Round(top, 2);
        element.Width = Math.Round(Math.Max(4, right - left), 2);
        element.Height = Math.Round(Math.Max(4, bottom - top), 2);

        _didMutate = true;
        UpdateSelectionBounds();
        InvalidateVisual();
        Changed?.Invoke();
    }

    private void BeginPan()
    {
        _mode = DragMode.Pan;
        _panStartX = _panX;
        _panStartY = _panY;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        bool mutated = _mode is DragMode.Move or DragMode.Resize && _didMutate;
        _mode = DragMode.None;
        if (mutated)
        {
            UpdateSelectionBounds();
            Changed?.Invoke();
        }
        _resizeHandle = -1;
        _guides.Clear();
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            Focus();
            CaptureMouse();
            _startScreen = e.GetPosition(this);
            BeginPan();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _mode == DragMode.Pan)
        {
            ReleaseMouseCapture();
            _mode = DragMode.None;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var before = ScreenToCanvas(e.GetPosition(this));
            double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            Zoom = _zoom * factor;
            var after = ScreenToCanvas(e.GetPosition(this));
            _panX += (after.X - before.X) * _zoom;
            _panY += (after.Y - before.Y) * _zoom;
            InvalidateVisual();
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _panX += e.Delta * 0.6;
            InvalidateVisual();
        }
        else
        {
            _panY += e.Delta * 0.6;
            InvalidateVisual();
        }
        e.Handled = true;
    }

    public void SetSpaceDown(bool down) => _spaceDown = down;

    private HudElement? HitElement(Point canvasPoint)
    {
        if (Profile is null) return null;
        return Profile.Elements
            .Where(e => e.Visible)
            .OrderByDescending(e => e.ZIndex)
            .FirstOrDefault(e => new Rect(e.X, e.Y, e.Width, e.Height).Contains(canvasPoint));
    }

    private int HitHandle(Point screen)
    {
        if (Profile is null || _selected.Count != 1) return -1;
        var topLeft = CanvasToScreen(new Point(_selectionBounds.X, _selectionBounds.Y));
        var rect = new Rect(topLeft.X, topLeft.Y, _selectionBounds.Width * _zoom, _selectionBounds.Height * _zoom);
        var handles = HandlePoints(rect).ToList();
        for (int i = 0; i < handles.Count; i++)
        {
            if (Math.Abs(handles[i].X - screen.X) <= HandleHitRadius && Math.Abs(handles[i].Y - screen.Y) <= HandleHitRadius)
                return i;
        }
        return -1;
    }
}
