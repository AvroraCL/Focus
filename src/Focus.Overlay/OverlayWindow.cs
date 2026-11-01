using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Focus.Core.Ipc;
using Focus.Core.Profiles;

namespace Focus.Overlay;

/// <summary>
/// Borderless, transparent, click-through HUD window. It never accepts focus, never appears in
/// Alt+Tab and follows either the game window or a fixed monitor.
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly SharedStateChannel _channel;
    private readonly HudSurface _surface;
    private readonly OverlayDataSource _dataSource = new();
    private readonly DispatcherTimer _timer;
    private EventWaitHandle? _exitSignal;
    private OverlayState? _state;
    private HudProfile? _profile;
    private int _profileRevision = -1;
    private DateTime _lastPayloadAt = DateTime.UtcNow;
    private nint _hwnd;
    private nint _gameWindow;
    private int _tick;
    private bool _visible;
    private string _lastPlacement = string.Empty;
    private bool _lastFocused = true;
    private long _lastRenderedSequence = -1;
    private bool _profileDirty = true;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Width = 1;
        Height = 1;
        Left = -10000;
        Top = -10000;

        _surface = new HudSurface { IsHitTestVisible = false };
        Content = _surface;

        _channel = SharedStateChannel.OpenReader();
        TryOpenExitSignal();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeOverlay.MakeClickThrough(_hwnd);
        };

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void TryOpenExitSignal()
    {
        if (_exitSignal is not null) return;
        try
        {
            _exitSignal = _channel.OpenOrCreateExitEvent();
        }
        catch
        {
            _exitSignal = null;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _tick++;
        try
        {
            OnTickCore();
        }
        catch (Exception ex)
        {
            OverlayLog.Write("tick failed: " + ex.Message);
        }
    }

    private void OnTickCore()
    {
        if (_tick == 1) OverlayLog.Write("first tick, reader has writer=" + _channel.HasWriter + ", version=" + _channel.PublishedVersion);

        TryOpenExitSignal();
        if (_exitSignal is { } exit && exit.WaitOne(0))
        {
            Close();
            return;
        }

        if (_channel.TryReadNewest(out var payload))
        {
            try
            {
                var state = OverlayStateCodec.Deserialize(payload);
                if (state is not null)
                {
                    if (_state is null) OverlayLog.Write($"first payload: {payload.Length} bytes, profileRev={state.ProfileRevision}, target={state.TargetPid}, visible={state.Visible}, values={state.Values.Count}");
                    _state = state;
                    _lastPayloadAt = DateTime.UtcNow;
                    if (state.ProfileRevision != _profileRevision)
                    {
                        _profileRevision = state.ProfileRevision;
                        try
                        {
                            _profile = OverlayStateCodec.ParseProfile(state);
                            _profileDirty = true;
                            OverlayLog.Write("profile parsed: " + (_profile?.Name ?? "<null>"));
                        }
                        catch (Exception ex)
                        {
                            OverlayLog.Write("profile parse failed: " + ex.Message);
                        }
                    }
                }
            }
            catch
            {
                // A malformed payload is skipped; the next one will replace it.
            }
        }

        // The app writes continuously; a long silence means it is gone.
        if (DateTime.UtcNow - _lastPayloadAt > TimeSpan.FromSeconds(20))
        {
            OverlayLog.Write("no payload for 20s, exiting");
            Close();
            return;
        }

        if (_profile is null) return;

        bool shouldShow = _state?.Visible != false;
        // Without a monitored game the HUD is a design preview, so the focus rule does not apply.
        if (shouldShow && _state?.OnlyWhenFocused == true && (_state?.TargetPid ?? 0) > 0)
        {
            if (_tick % 15 == 0) _lastFocused = NativeOverlay.GetForegroundPid() == _state!.TargetPid;
            shouldShow = _lastFocused;
        }

        if (_tick % 15 == 0 || _visible != shouldShow) UpdatePlacement(shouldShow);

        _dataSource.Update(_state ?? new OverlayState());
        _surface.Profile = _profile;
        _surface.Data = _dataSource;

        // Values are published at 20 Hz while the timer runs at 30 Hz; skip duplicate frames.
        long sequence = _state?.Sequence ?? -1;
        if (shouldShow && _visible && (sequence != _lastRenderedSequence || _profileDirty))
        {
            _lastRenderedSequence = sequence;
            _profileDirty = false;
            _surface.InvalidateVisual();
        }
    }

    private void UpdatePlacement(bool visible)
    {
        if (_hwnd == 0 || _profile is null) return;

        int pid = _state?.TargetPid ?? 0;
        if (_tick % 30 == 0)
        {
            _gameWindow = pid > 0 ? NativeOverlay.FindMainWindow(pid) : 0;
        }

        var display = _profile.Display;
        double scale = display.Scale is > 0 and < 10 ? display.Scale : 1.0;

        NativeOverlay.RECT bounds;
        uint dpi;
        nint monitor;

        if (display.FollowGameWindow && _gameWindow != 0)
        {
            bounds = NativeOverlay.GetRect(_gameWindow);
            dpi = NativeOverlay.GetDpi(_gameWindow);
            if (bounds.Width < 64 || bounds.Height < 64)
            {
                bounds = NativeOverlay.GetMonitorBounds(display.MonitorDeviceName, _gameWindow, out monitor, out dpi);
            }
        }
        else
        {
            bounds = NativeOverlay.GetMonitorBounds(display.MonitorDeviceName, _gameWindow, out monitor, out dpi);
        }

        double dpiScale = Math.Max(1.0, dpi / 96.0);
        double width = Math.Max(8, _profile.CanvasWidth * scale * dpiScale);
        double height = Math.Max(8, _profile.CanvasHeight * scale * dpiScale);
        double ox = display.OffsetX * scale * dpiScale;
        double oy = display.OffsetY * scale * dpiScale;

        double x, y;
        switch (display.Anchor)
        {
            case HudAnchor.TopCenter:
                x = bounds.Left + (bounds.Width - width) / 2 + ox; y = bounds.Top + oy; break;
            case HudAnchor.TopRight:
                x = bounds.Right - width - ox; y = bounds.Top + oy; break;
            case HudAnchor.MiddleLeft:
                x = bounds.Left + ox; y = bounds.Top + (bounds.Height - height) / 2 + oy; break;
            case HudAnchor.Center:
                x = bounds.Left + (bounds.Width - width) / 2 + ox; y = bounds.Top + (bounds.Height - height) / 2 + oy; break;
            case HudAnchor.MiddleRight:
                x = bounds.Right - width - ox; y = bounds.Top + (bounds.Height - height) / 2 + oy; break;
            case HudAnchor.BottomLeft:
                x = bounds.Left + ox; y = bounds.Bottom - height - oy; break;
            case HudAnchor.BottomCenter:
                x = bounds.Left + (bounds.Width - width) / 2 + ox; y = bounds.Bottom - height - oy; break;
            case HudAnchor.BottomRight:
                x = bounds.Right - width - ox; y = bounds.Bottom - height - oy; break;
            default:
                x = bounds.Left + ox; y = bounds.Top + oy; break;
        }

        // Never let an oversized HUD drift completely off the display.
        x = Math.Clamp(x, bounds.Left - width + 48, bounds.Right - 48);
        y = Math.Clamp(y, bounds.Top - height + 48, bounds.Bottom - 48);

        _visible = visible;
        string key = $"{x:F0}:{y:F0}:{width:F0}:{height:F0}:{visible}";
        if (key == _lastPlacement) return;
        _lastPlacement = key;
        OverlayLog.Write($"place x={x:F0} y={y:F0} w={width:F0} h={height:F0} visible={visible} dpi={dpi}");
        NativeOverlay.Place(_hwnd, (int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(width), (int)Math.Round(height), visible);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _exitSignal?.Dispose();
        _channel.Dispose();
        base.OnClosed(e);
    }
}
