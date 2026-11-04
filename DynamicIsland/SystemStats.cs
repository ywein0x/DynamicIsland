using System.Runtime.InteropServices;

namespace DynamicIsland;

public sealed record StatsSample(int CpuPercent, int RamPercent, int? GpuTemperature, int? GpuPercent);

/// <summary>CPU and memory load from Win32, GPU temperature and load from NVIDIA's NVML when available.</summary>
public sealed class SystemStats
{
    long _lastIdle, _lastKernel, _lastUser;
    bool _nvmlTried;
    IntPtr _gpu;

    public StatsSample Sample()
    {
        var temperature = GpuTemperature();
        return new(Cpu(), Ram(), temperature, GpuLoad());
    }

    int? GpuLoad()
    {
        if (_gpu == IntPtr.Zero)
            return null;
        try
        {
            return nvmlDeviceGetUtilizationRates(_gpu, out var rates) == 0 ? (int)rates.Gpu : null;
        }
        catch
        {
            return null;
        }
    }

    int Cpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return 0;
        var idleDelta = idle - _lastIdle;
        var total = kernel - _lastKernel + (user - _lastUser); // kernel time includes idle time
        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        return total > 0 ? (int)Math.Round(100.0 * (total - idleDelta) / total) : 0;
    }

    static int Ram()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref status) ? (int)status.dwMemoryLoad : 0;
    }

    int? GpuTemperature()
    {
        try
        {
            if (!_nvmlTried)
            {
                _nvmlTried = true;
                if (nvmlInit_v2() != 0 || nvmlDeviceGetHandleByIndex_v2(0, out _gpu) != 0)
                    _gpu = IntPtr.Zero;
            }
            if (_gpu == IntPtr.Zero)
                return null;
            return nvmlDeviceGetTemperature(_gpu, 0, out var celsius) == 0 ? (int)celsius : null;
        }
        catch
        {
            // No NVIDIA driver (DllNotFoundException) or an old one.
            _gpu = IntPtr.Zero;
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("nvml.dll")]
    static extern int nvmlInit_v2();

    [DllImport("nvml.dll")]
    static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll")]
    static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temperature);

    [StructLayout(LayoutKind.Sequential)]
    struct NvmlUtilization
    {
        public uint Gpu, Memory;
    }

    [DllImport("nvml.dll")]
    static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization rates);
}
