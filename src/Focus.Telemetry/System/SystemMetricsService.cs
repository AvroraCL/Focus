using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Focus.Telemetry.System;

public sealed class SystemSnapshot
{
    public long TotalMemoryMb { get; init; }
    public long UsedMemoryMb { get; init; }
    public float MemoryPercent { get; init; }
    public long ProcessWorkingSetMb { get; init; }
    public long ProcessPrivateMb { get; init; }
}

public sealed class SystemMetricsService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private Process? _process;
    private int _processId = -1;

    public SystemSnapshot Sample(int processId)
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        long totalMb = 0, usedMb = 0;
        float percent = 0;
        if (GlobalMemoryStatusEx(ref status))
        {
            totalMb = (long)(status.ullTotalPhys / (1024 * 1024));
            long availMb = (long)(status.ullAvailPhys / (1024 * 1024));
            usedMb = totalMb - availMb;
            percent = totalMb > 0 ? usedMb * 100f / totalMb : 0;
        }

        long workingSet = 0, privateBytes = 0;
        if (processId > 0)
        {
            try
            {
                if (_process is null || _processId != processId || _process.HasExited)
                {
                    _process?.Dispose();
                    _process = Process.GetProcessById(processId);
                    _processId = processId;
                }
                _process.Refresh();
                workingSet = _process.WorkingSet64 / (1024 * 1024);
                privateBytes = _process.PrivateMemorySize64 / (1024 * 1024);
            }
            catch
            {
                _process?.Dispose();
                _process = null;
                _processId = -1;
            }
        }

        return new SystemSnapshot
        {
            TotalMemoryMb = totalMb,
            UsedMemoryMb = usedMb,
            MemoryPercent = percent,
            ProcessWorkingSetMb = workingSet,
            ProcessPrivateMb = privateBytes,
        };
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}
