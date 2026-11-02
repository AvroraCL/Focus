using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Focus.Core.Native;

internal sealed class TimelineHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal TimelineHandle(int capacity) : base(true)
    {
        SetHandle(NativeCore.Create((uint)capacity));
        if (IsInvalid) throw new InvalidOperationException("Could not create Rust timeline.");
    }
    protected override bool ReleaseHandle() { NativeCore.Free(handle); return true; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeStats
{
    internal ulong Count;
    internal double Seconds, CurrentFps, AverageFps, LowOne, LowPointOne, Median, P95, P99, Maximum;
    internal ulong Stutters;
    internal double StutterPercent;
}

internal static class NativeCore
{
    private const string Library = "focus_native";
    static NativeCore()
    {
        if (AbiVersion() != 1) throw new InvalidOperationException("Incompatible Focus Rust core ABI.");
    }
    internal static void Check(int result)
    {
        if (result < 0) throw new InvalidOperationException($"Rust core operation failed ({result}).");
    }
    [DllImport(Library, EntryPoint = "focus_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint AbiVersion();
    [DllImport(Library, EntryPoint = "focus_timeline_new", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Create(uint capacity);
    [DllImport(Library, EntryPoint = "focus_timeline_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr handle);
    [DllImport(Library, EntryPoint = "focus_timeline", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Timeline(TimelineHandle handle, uint operation, double a, double b, [Out] double[]? output, uint length);
    [DllImport(Library, EntryPoint = "focus_timeline_stats", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int TimelineStats(TimelineHandle handle, double now, double window, out NativeStats stats);
    [DllImport(Library, EntryPoint = "focus_compute", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Compute(double[] input, uint length, out NativeStats stats);
    [DllImport(Library, EntryPoint = "focus_quantile", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Quantile(double[] input, uint length, double fraction, uint mode, out double value);
    [DllImport(Library, EntryPoint = "focus_atomic_write", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Write(byte[] path, uint pathLength, byte[] data, uint length);
}

/// <summary>Rust writes and flushes a unique sibling file, then replaces the destination.
/// Failure preserves the previous destination and is reported to the caller.</summary>
public static class AtomicFile
{
    public static void WriteAllBytes(string path, byte[] data)
    {
        var encoded = Encoding.UTF8.GetBytes(Path.GetFullPath(path));
        int result = NativeCore.Write(encoded, (uint)encoded.Length, data, (uint)data.Length);
        if (result != 0) throw new IOException($"Could not save {path}: " +
            (result > 0 ? new Win32Exception(result).Message : $"native error {result}"));
    }
    public static void WriteAllText(string path, string text) => WriteAllBytes(path, Encoding.UTF8.GetBytes(text));
}
