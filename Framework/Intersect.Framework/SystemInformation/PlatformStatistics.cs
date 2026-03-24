using Hardware.Info;
using Microsoft.Extensions.Logging;

namespace Intersect.Framework.SystemInformation;

public class PlatformStatistics
{
    private static readonly HardwareInfo? HardwareInfo;

    public static IGPUStatisticsProvider? GPUStatisticsProvider { get; set; }

    public static ILogger? Logger { get; set; }

    public static long AvailablePhysicalMemory => HardwareInfo != null ? (long)HardwareInfo.MemoryStatus.AvailablePhysical : 0;

    public static long TotalPhysicalMemory => HardwareInfo != null ? (long)HardwareInfo.MemoryStatus.TotalPhysical : 0;

    public static long AvailableGPUMemory => GPUStatisticsProvider?.AvailableMemory ?? AvailableSystemMemory;

    public static long TotalGPUMemory => GPUStatisticsProvider?.TotalMemory ?? TotalSystemMemory;

    public static long AvailableSystemMemory => HardwareInfo != null ? (long)HardwareInfo.MemoryStatus.AvailableVirtual : 0;

    public static long TotalSystemMemory => HardwareInfo != null ? (long)HardwareInfo.MemoryStatus.TotalVirtual : 0;

    public static void Refresh()
    {
        if (HardwareInfo == null) return;

        try
        {
            HardwareInfo.RefreshMemoryStatus();
        }
        catch
        {
            // Do nothing
        }
    }

    static PlatformStatistics()
    {
        if (OperatingSystem.IsBrowser()) return;

        HardwareInfo = new HardwareInfo();

        Refresh();
    }
}