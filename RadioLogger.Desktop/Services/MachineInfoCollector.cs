using RadioLogger.Models;
using RadioLogger.Shared.Models;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace RadioLogger.Services
{
    public class MachineInfoCollector
    {
        private static readonly ILogger _log = AppLog.For<MachineInfoCollector>();
        private readonly AppSettings _settings;
        private readonly DateTime _appStartTime = DateTime.UtcNow;
        private readonly PerformanceCounter _cpuCounter;

        // Cached values for disk (refreshed every 10s via MainViewModel)
        private double _diskFreeGb;
        private double _diskTotalGb;
        private DateTime _lastDiskCheck = DateTime.MinValue;

        // Cached public IP (fetched once)
        private string _publicIp = "";
        private bool _publicIpFetched;

        public MachineInfoCollector(AppSettings settings)
        {
            _settings = settings;
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // First call always returns 0, discard it
        }

        public MachineInfo GetMachineInfo()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            return new MachineInfo
            {
                MachineId = Environment.MachineName,
                AppVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0",
                AutoLoginEnabled = AutoStartService.IsAutoLoginEnabled(),
                AutoStartEnabled = AutoStartService.IsAutoStartEnabled(),
                LocalIp = GetLocalIp(),
                PublicIp = _publicIp
            };
        }

        /// <summary>
        /// Fetch public IP asynchronously (called once at startup).
        /// </summary>
        public async Task FetchPublicIpAsync()
        {
            if (_publicIpFetched) return;
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                _publicIp = (await client.GetStringAsync("https://api.ipify.org")).Trim();
                _publicIpFetched = true;
            }
            catch
            {
                _publicIp = "N/A";
            }
        }

        public MachineMetrics GetMetrics()
        {
            RefreshDiskIfNeeded();

            return new MachineMetrics
            {
                DiskFreeGb = _diskFreeGb,
                DiskTotalGb = _diskTotalGb,
                CpuPercent = Math.Round(GetCpuUsage(), 1),
                RamUsedGb = Math.Round(GetRamUsedGb(), 2),
                RamTotalGb = Math.Round(GetRamTotalGb(), 2),
                WindowsUptimeHours = Math.Round(TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours, 2),
                AppUptimeHours = Math.Round((DateTime.UtcNow - _appStartTime).TotalHours, 2)
            };
        }

        private float GetCpuUsage()
        {
            try { return _cpuCounter.NextValue(); }
            catch { return 0; }
        }

        private void RefreshDiskIfNeeded()
        {
            if ((DateTime.Now - _lastDiskCheck).TotalSeconds < 10) return;
            _lastDiskCheck = DateTime.Now;

            try
            {
                var drive = new DriveInfo(_settings.RecordingBasePath);
                _diskFreeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
                _diskTotalGb = drive.TotalSize / 1024.0 / 1024.0 / 1024.0;
            }
            catch { }
        }

        private static string GetLocalIp()
        {
            try
            {
                var addresses = Dns.GetHostAddresses(Dns.GetHostName());
                var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                return ipv4?.ToString() ?? "N/A";
            }
            catch { return "N/A"; }
        }

        // P/Invoke for fast RAM info
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

        private static double GetRamTotalGb()
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref mem) ? mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0 : 0;
        }

        private static double GetRamUsedGb()
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref mem)) return 0;
            return (mem.ullTotalPhys - mem.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
        }
    }
}
