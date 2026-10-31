using System.Runtime.InteropServices;
using System.Text;

namespace Focus.Overlay;

internal static class NativeOverlay
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public static readonly nint HwndTopmost = new(-1);
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    public const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hWnd, int flags);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT pt, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public static void MakeClickThrough(nint hwnd)
    {
        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    public static void Place(nint hwnd, int x, int y, int width, int height, bool visible)
    {
        if (width < 1) width = 1;
        if (height < 1) height = 1;
        uint flags = SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW);
        SetWindowPos(hwnd, HwndTopmost, x, y, width, height, flags);
    }

    public static nint FindMainWindow(int pid)
    {
        if (pid <= 0) return 0;
        nint best = 0;
        int bestArea = 0;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out int windowPid);
            if (windowPid != pid) return true;
            if (!IsWindowVisible(hWnd) || IsIconic(hWnd)) return true;
            if (!GetWindowRect(hWnd, out var rect)) return true;
            int area = rect.Width * rect.Height;
            if (area > bestArea)
            {
                bestArea = area;
                best = hWnd;
            }
            return true;
        }, 0);
        return best;
    }

    public static RECT GetRect(nint hwnd)
    {
        GetWindowRect(hwnd, out var rect);
        return rect;
    }

    public static int GetForegroundPid()
    {
        nint hwnd = GetForegroundWindow();
        if (hwnd == 0) return 0;
        GetWindowThreadProcessId(hwnd, out int pid);
        return pid;
    }

    public static uint GetDpi(nint hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi > 0) return dpi;
        }
        catch
        {
            // Older builds: fall through.
        }
        return 96;
    }

    public static uint GetMonitorDpi(nint monitor)
    {
        try
        {
            if (GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0) return dpiX;
        }
        catch
        {
            // shcore may not exist on very old systems.
        }
        return 96;
    }

    public static RECT GetMonitorBounds(string? deviceName, nint referenceWindow, out nint monitor, out uint dpi)
    {
        monitor = 0;
        dpi = 96;

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            nint found = 0;
            RECT foundRect = default;
            EnumDisplayMonitors(0, 0, (nint hMonitor, nint _, ref RECT _, nint _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info) && string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    found = hMonitor;
                    foundRect = info.rcMonitor;
                    return false;
                }
                return true;
            }, 0);
            if (found != 0)
            {
                monitor = found;
                dpi = GetMonitorDpi(found);
                return foundRect;
            }
        }

        nint target = 0;
        if (referenceWindow != 0) target = MonitorFromWindow(referenceWindow, MONITOR_DEFAULTTONEAREST);
        if (target == 0)
        {
            var pt = new POINT { X = 0, Y = 0 };
            target = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        }

        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (target != 0 && GetMonitorInfo(target, ref mi))
        {
            monitor = target;
            dpi = GetMonitorDpi(target);
            return mi.rcMonitor;
        }

        return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    public static string MonitorDeviceName(nint hwnd)
    {
        nint monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return GetMonitorInfo(monitor, ref info) ? info.szDevice : string.Empty;
    }

    public static string DescribeWindow(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        var sb = new StringBuilder(256);
        var rect = GetRect(hwnd);
        return $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}";
    }
}
