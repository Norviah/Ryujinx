using System;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Ryujinx.Common.Logging;

namespace Ryujinx.Common.SystemInfo
{
    [SupportedOSPlatform("windows")]
    class WindowsSystemInfo : SystemInfo
    {
        internal WindowsSystemInfo()
        {
            CpuName = $"{GetCpuidCpuName() ?? GetCpuNameWMI()} ; {LogicalCoreCount} logical"; // WMI is very slow
            (RamTotal, RamAvailable) = GetMemoryStats();
        }

        private static (ulong Total, ulong Available) GetMemoryStats()
        {
            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return (memStatus.TotalPhys, memStatus.AvailPhys); // Bytes
            }
            else
            {
                Logger.Error?.Print(LogClass.Application, $"GlobalMemoryStatusEx failed. Error {Marshal.GetLastWin32Error():X}");
            }

            // Alternative WMI path
            ManagementObjectCollection memObjs = GetWMIObjects("root\\CIMV2", "SELECT * FROM Win32_OperatingSystem");

            if (memObjs != null)
            {
                foreach (var memObj in memObjs)
                {
                    // Entries are in KB
                    _ = ulong.TryParse(memObj["TotalVisibleMemorySize"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong totalKB);
                    _ = ulong.TryParse(memObj["FreePhysicalMemory"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong availableKB);

                    return (totalKB * 1024, availableKB * 1024);
                }
            }

            return (0, 0);
        }

        private static string GetCpuNameWMI()
        {
            ManagementObjectCollection cpuObjs = GetWMIObjects("root\\CIMV2", "SELECT * FROM Win32_Processor");

            if (cpuObjs != null)
            {
                foreach (var cpuObj in cpuObjs)
                {
                    return cpuObj["Name"].ToString().Trim();
                }
            }

            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER").Trim();
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        private static ManagementObjectCollection GetWMIObjects(string scope, string query)
        {
            try
            {
                return new ManagementObjectSearcher(scope, query).Get();
            }
            catch (PlatformNotSupportedException e)
            {
                Logger.Error?.Print(LogClass.Application, $"WMI isn't available : {e.Message}");
            }
            catch (COMException e)
            {
                Logger.Error?.Print(LogClass.Application, $"WMI isn't available : {e.Message}");
            }

            return null;
        }
    }
}