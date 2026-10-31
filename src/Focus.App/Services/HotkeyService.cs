using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Focus.Core.App;

namespace Focus.App.Services;

/// <summary>Global hotkeys registered on the main window.</summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1;
    private const uint MOD_CONTROL = 0x2;
    private const uint MOD_SHIFT = 0x4;
    private const uint MOD_WIN = 0x8;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly Dictionary<int, Action> _handlers = new();
    private HwndSource? _source;
    private int _nextId = 0x4A00;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return 0;
    }

    /// <summary>Registers a binding. Returns false when the combination is already taken.</summary>
    public bool Register(HotkeyBinding binding, Action callback)
    {
        if (!binding.Enabled || binding.VirtualKey == 0 || _source is null) return false;

        uint modifiers = MOD_NOREPEAT;
        if (binding.Control) modifiers |= MOD_CONTROL;
        if (binding.Alt) modifiers |= MOD_ALT;
        if (binding.Shift) modifiers |= MOD_SHIFT;
        if (binding.Win) modifiers |= MOD_WIN;

        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, (uint)binding.VirtualKey))
        {
            DataPaths.AppendLog($"hotkey register failed: vk={binding.VirtualKey:X2} mods={modifiers:X}");
            return false;
        }
        _handlers[id] = callback;
        return true;
    }

    public void UnregisterAll()
    {
        if (_source is null) return;
        foreach (var id in _handlers.Keys.ToList())
        {
            try
            {
                UnregisterHotKey(_source.Handle, id);
            }
            catch
            {
                // ignore
            }
        }
        _handlers.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    public static string Describe(HotkeyBinding binding)
    {
        if (binding.VirtualKey == 0) return "—";
        var parts = new List<string>();
        if (binding.Control) parts.Add("Ctrl");
        if (binding.Alt) parts.Add("Alt");
        if (binding.Shift) parts.Add("Shift");
        if (binding.Win) parts.Add("Win");
        parts.Add(((System.Windows.Input.Key)System.Windows.Input.KeyInterop.KeyFromVirtualKey(binding.VirtualKey)).ToString());
        return string.Join(" + ", parts);
    }
}

public static class ElevationHelper
{
    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool RestartElevated()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            var info = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
            };
            var process = System.Diagnostics.Process.Start(info);
            if (process is null) return false;
            Application.Current.Shutdown();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
