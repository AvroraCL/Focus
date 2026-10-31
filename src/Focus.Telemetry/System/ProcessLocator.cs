using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Focus.Telemetry.System;

public sealed record ProcessInfo(int Pid, string Name, string? ExecutablePath, string WindowTitle, nint WindowHandle)
{
    public string DisplayName => string.IsNullOrWhiteSpace(WindowTitle) ? Name : $"{Name} — {WindowTitle}";
}

/// <summary>Locates the game the user is currently running, without touching the game process itself.</summary>
public static class ProcessLocator
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost",
        "ApplicationFrameHost", "SystemSettings", "Taskmgr", "LockApp", "dwm", "csrss", "winlogon",
        "Focus", "FocusOverlay", "chrome", "msedge", "firefox", "Code", "devenv", "WindowsTerminal",
    };

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int index);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    public static ProcessInfo? GetForeground() => Describe(GetForegroundWindow());

    public static ProcessInfo? Describe(nint hWnd)
    {
        if (hWnd == 0 || !IsWindowVisible(hWnd)) return null;
        GetWindowThreadProcessId(hWnd, out int pid);
        if (pid <= 0) return null;
        return DescribePid(pid, hWnd);
    }

    public static ProcessInfo? DescribePid(int pid, nint hWnd = 0)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            string title = string.Empty;
            if (hWnd != 0)
            {
                var builder = new StringBuilder(512);
                GetWindowText(hWnd, builder, builder.Capacity);
                title = builder.ToString();
            }
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                // Access denied for protected processes: leave the path unknown.
            }
            return new ProcessInfo(pid, process.ProcessName, path, title, hWnd);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Visible top-level windows the user could reasonably be playing a game in.</summary>
    public static IReadOnlyList<ProcessInfo> GetCandidates()
    {
        var results = new Dictionary<int, ProcessInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            int style = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((style & WS_EX_TOOLWINDOW) != 0) return true;
            if ((style & WS_EX_NOACTIVATE) != 0) return true;

            GetWindowThreadProcessId(hWnd, out int pid);
            if (pid <= 0 || pid == Environment.ProcessId) return true;

            var classBuilder = new StringBuilder(256);
            GetClassName(hWnd, classBuilder, classBuilder.Capacity);
            string cls = classBuilder.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow") return true;

            var titleBuilder = new StringBuilder(512);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();

            var info = DescribePid(pid, hWnd);
            if (info is null) return true;
            if (Ignored.Contains(info.Name)) return true;
            if (string.IsNullOrWhiteSpace(title)) return true;

            if (!results.ContainsKey(pid)) results[pid] = info;
            return true;
        }, 0);

        return results.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsRunning(int pid)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
