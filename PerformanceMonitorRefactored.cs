using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;

namespace G19PerformanceMonitorVRAM
{
    public class PerformanceMonitorRefactored : IMetricProvider
    {
        private const string NVMLDLL = "nvml.dll";

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlInit_v2")]
            public static extern int nvmlInit();

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlShutdown")]
            public static extern int nvmlShutdown();

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
            public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetMemoryInfo_v2")]
            public static extern int nvmlDeviceGetMemoryInfo_v2(IntPtr device, ref nvmlMemory_v2_t memory);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetUtilizationRates")]
            public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetCount_v2")]
            public static extern int nvmlDeviceGetCount(out uint deviceCount);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetComputeRunningProcesses")]
            public static extern int nvmlDeviceGetComputeRunningProcesses(IntPtr device, ref uint infoCount, [System.Runtime.InteropServices.Out] nvmlProcessInfo_t[] infos);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetGraphicsRunningProcesses")]
            public static extern int nvmlDeviceGetGraphicsRunningProcesses(IntPtr device, ref uint infoCount, [System.Runtime.InteropServices.Out] nvmlProcessInfo_t[] infos);

            [System.Runtime.InteropServices.DllImport(NVMLDLL, EntryPoint = "nvmlDeviceGetTemperature")]
            public static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensorType, out uint temperature);

            public static uint GetNvmlVersion(int size, int version)
            {
                return (uint)(size | (version << 24));
            }

            [System.Runtime.InteropServices.DllImport("dxgi.dll")]
            public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct nvmlMemory_v2_t
        {
            public uint version;
            public uint _padding; 
            public ulong total;
            public ulong reserved;
            public ulong free;
            public ulong used;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct nvmlProcessInfo_t
        {
            public uint pid;
            public ulong usedGpuMemory;
            public uint gpuInstanceId;
            public uint computeInstanceId;
        }

        [ComImport, Guid("aec22e7e-337d-491c-a6ff-ad3222f123cb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIFactory
        {
            [PreserveSig] int EnumAdapters(uint adapter, out IntPtr ppAdapter);
        }

        [ComImport, Guid("645967A4-1392-4310-A798-8053CE3E93FD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IDXGIAdapter3
        {
            [PreserveSig] int QueryVideoMemoryInfo(uint nodeIndex, uint memorySegmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_QUERY_VIDEO_MEMORY_INFO
        {
            public ulong Budget;
            public ulong CurrentUsage;
            public ulong AvailableForReservation;
            public ulong CurrentReservation;
        }

        private PerformanceCounter cpuCounter;
        private List<PerformanceCounter> vramUsageCounters = new List<PerformanceCounter>();
        private List<PerformanceCounter> vramCapacityCounters = new List<PerformanceCounter>();
        private List<PerformanceCounter> gpuEngineCounters = new List<PerformanceCounter>();

        private IDXGIAdapter3 _dxgiAdapter;
        private IntPtr _nvmlDevice = IntPtr.Zero;
        private bool _nvmlInitialized = false;

        private readonly object _syncLock = new object();
        private float _cpuUsage;
        private float _cpuTempCelsius;
        private float _ramUsage;
        private float _vramUsage;
        private float _gpuUsage;
        private float _gpuTempCelsius;
        private float _totalVramGB = 24.0f; 
        private float _totalRamGB = 32.0f;  
        private List<DiskInfo> _diskMetrics = new List<DiskInfo>();

        private HistoryBuffer _cpuHistory = new HistoryBuffer(240);
        private HistoryBuffer _ramHistory = new HistoryBuffer(240);
        private HistoryBuffer _vramHistory = new HistoryBuffer(240);
        private HistoryBuffer _gpuHistory = new HistoryBuffer(240);

        private Timer _pollingTimer;
        private bool _isInitialized = false;

        public string Name => "G19 Performance Provider (NVML v2+DXGI+PerfCounter)";
        public float CpuUsage { get { lock (_syncLock) return _cpuUsage; } }
        public float CpuTempCelsius { get { lock (_syncLock) return _cpuTempCelsius; } }
        public float RamUsage { get { lock (_syncLock) return _ramUsage; } } 
        public float VRamUsage { get { lock (_syncLock) return _vramUsage; } }
        public float GpuUsage { get { lock (_syncLock) return _gpuUsage; } }
        public float GpuTempCelsius { get { lock (_syncLock) return _gpuTempCelsius; } }
        public float TotalVramGB => _totalVramGB;
        public float TotalRamGB => _totalRamGB;

        public float[] CpuHistory => _cpuHistory.GetData();
        public float[] RamHistory => _ramHistory.GetData();
        public float[] VRamHistory => _vramHistory.GetData();
        public float[] GpuHistory => _gpuHistory.GetData();

        public IEnumerable<DiskInfo> DiskMetrics { get { lock (_syncLock) return new List<DiskInfo>(_diskMetrics); } }
        public IEnumerable<ProcessVramInfo> TopVramConsumers { get { lock (_syncLock) return new List<ProcessVramInfo>(_topVramConsumers); } }

        public bool IsInitialized => _isInitialized;

        public PerformanceMonitorRefactored(int pollingIntervalMs = 1000)
        {
            try
            {
                Logger.Info($"Initializing {Name}...");
                InitializeCounters();
                _isInitialized = true;

                _pollingTimer = new Timer(pollingIntervalMs);
                _pollingTimer.Elapsed += (s, e) => UpdateMetrics();
                _pollingTimer.AutoReset = false; 
                _pollingTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize performance provider.", ex);
                _isInitialized = false;
            }
        }

        private void InitializeCounters()
        {
            try {
                if (PerformanceCounterCategory.Exists("Processor Information"))
                    cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
                else
                    cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                
                cpuCounter.NextValue();
            } catch (Exception ex) { Logger.Error("CPU init failed", ex); }

            try {
                Logger.Info("Attempting NVML v2 Initialization...");
                if (NativeMethods.nvmlInit() == 0) {
                    _nvmlInitialized = true;
                    uint count;
                    if (NativeMethods.nvmlDeviceGetCount(out count) == 0 && count > 0) {
                        if (NativeMethods.nvmlDeviceGetHandleByIndex(0, out _nvmlDevice) == 0) {
                            Logger.Info("Successfully bound NVML v2 Device 0 for VRAM/Utilization monitoring.");
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Warn($"NVML Initialization failed: {ex.Message}. Falling back to DXGI.");
            }

            if (_nvmlDevice == IntPtr.Zero) {
                try {
                IntPtr factoryPtr;
                Guid factoryGuid = new Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369"); 
                if (NativeMethods.CreateDXGIFactory1(ref factoryGuid, out factoryPtr) == 0) {
                    IDXGIFactory factory = (IDXGIFactory)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(factoryPtr);
                    IntPtr adapterPtr;
                    if (factory.EnumAdapters(0, out adapterPtr) == 0) {
                        IntPtr adapter3Ptr;
                        Guid adapter3Guid = new Guid("645967A4-1392-4310-A798-8053CE3E93FD");
                        System.Runtime.InteropServices.Marshal.QueryInterface(adapterPtr, ref adapter3Guid, out adapter3Ptr);
                        if (adapter3Ptr != IntPtr.Zero) {
                            _dxgiAdapter = (IDXGIAdapter3)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(adapter3Ptr);
                            Logger.Info("Successfully bound DXGI Adapter 3 for VRAM monitoring.");
                        }
                    }
                }
                } catch (Exception ex) {
                    Logger.Warn($"DXGI Initialization failed: {ex.Message}. Falling back to Performance Counters.");
                }
            }

            InitializeFallbackCounters();
        }

        private void InitializeFallbackCounters()
        {
            vramUsageCounters.Clear();
            vramCapacityCounters.Clear();
            gpuEngineCounters.Clear();

            try {
                if (PerformanceCounterCategory.Exists("GPU Adapter Memory")) {
                    var cat = new PerformanceCounterCategory("GPU Adapter Memory");
                    string best = null; long max = -1;
                    foreach (var name in cat.GetInstanceNames().Where(n => n.Contains("phys"))) {
                        try {
                            using (var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Limit", name)) {
                                counter.NextValue(); System.Threading.Thread.Sleep(50);
                                long cap = (long)counter.NextValue();
                                if (cap > max) { max = cap; best = name; }
                            }
                        } catch { }
                    }
                    if (best != null) {
                        vramUsageCounters.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", best));
                        vramCapacityCounters.Add(new PerformanceCounter("GPU Adapter Memory", "Dedicated Limit", best));
                    }
                }

                if (PerformanceCounterCategory.Exists("GPU Engine")) {
                    var cat = new PerformanceCounterCategory("GPU Engine");
                    var allInstances = cat.GetInstanceNames();
                    
                    var targetId = allInstances.FirstOrDefault(n => n.Contains("engtype_3D"))?
                                    .Split('_').FirstOrDefault(s => s.StartsWith("pid") == false && s.Length > 2);

                    var instances = allInstances.Where(n => (n.Contains("engtype_3D") || n.Contains("engtype_Compute") || n.Contains("engtype_Cuda")) 
                                                        && (targetId == null || n.Contains(targetId)));

                    foreach (var inst in instances) {
                        try {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                            counter.NextValue();
                            gpuEngineCounters.Add(counter);
                        } catch { }
                    }
                    Logger.Info($"Initialized {gpuEngineCounters.Count} GPU engine counters.");
                }
            } catch { }
        }

        private float GetVramFromCounters()
        {
            if (vramUsageCounters.Count == 0 || vramCapacityCounters.Count == 0) return 0;
            long usage = 0; long limit = 0;
            foreach (var c in vramUsageCounters) try { usage += (long)c.NextValue(); } catch { }
            foreach (var c in vramCapacityCounters) try { limit += (long)c.NextValue(); } catch { }
            return limit > 0 ? (float)(usage * 100.0 / limit) : 0;
        }

        public void Update() { UpdateMetrics(); }

        private void UpdateMetrics()
        {
            if (!_isInitialized) return;

            try 
            {
                float newCpu = 0, newCpuTemp = 0, newRam = 0, newVram = 0, newGpu = 0, newGpuTemp = 0;
                var newDisks = new List<DiskInfo>();

                // CPU
                try { 
                    if (cpuCounter != null) newCpu = Math.Min(100, Math.Max(0, cpuCounter.NextValue())); 
                    newCpuTemp = GetCpuTemperature(); 
                } catch { }

                // RAM
                try {
                    var memStatus = new MEMORYSTATUSEX();
                    if (NativeMethods.GlobalMemoryStatusEx(memStatus))
                    {
                        newRam = memStatus.dwMemoryLoad;
                        _totalRamGB = (float)memStatus.ullTotalPhys / (1024 * 1024 * 1024);
                    }
                } catch { }

                bool vramFound = false;
                bool gpuFound = false;
                
                try {
                    if (_nvmlDevice != IntPtr.Zero) {
                        nvmlMemory_v2_t memInfo = new nvmlMemory_v2_t();
                        memInfo.version = NativeMethods.GetNvmlVersion(System.Runtime.InteropServices.Marshal.SizeOf(typeof(nvmlMemory_v2_t)), 2);
                        
                        if (NativeMethods.nvmlDeviceGetMemoryInfo_v2(_nvmlDevice, ref memInfo) == 0) {
                            if (memInfo.total > 0) {
                                newVram = (float)((double)memInfo.used / memInfo.total * 100.0);
                                _totalVramGB = (float)memInfo.total / (1024 * 1024 * 1024);
                                vramFound = true;
                            }
                        }

                        nvmlUtilization_t util;
                        if (NativeMethods.nvmlDeviceGetUtilizationRates(_nvmlDevice, out util) == 0) {
                            newGpu = util.gpu;
                            gpuFound = true;
                        }

                        uint tempCelsius;
                        if (NativeMethods.nvmlDeviceGetTemperature(_nvmlDevice, 0, out tempCelsius) == 0) {
                            newGpuTemp = tempCelsius;
                        }
                    }
                } catch (Exception ex) { Logger.Warn($"NVML Update failed: {ex.Message}"); }

                if (!vramFound && _dxgiAdapter != null) {
                    try {
                        DXGI_QUERY_VIDEO_MEMORY_INFO dxgiMem;
                        if (_dxgiAdapter.QueryVideoMemoryInfo(0, 0, out dxgiMem) == 0 && dxgiMem.Budget > 0) {
                            newVram = (float)((double)dxgiMem.CurrentUsage / dxgiMem.Budget * 100.0);
                            _totalVramGB = (float)dxgiMem.Budget / (1024 * 1024 * 1024);
                            vramFound = true;
                        }
                    } catch { }
                }

                if (!vramFound) newVram = GetVramFromCounters();
                if (!gpuFound && gpuEngineCounters.Count > 0) {
                    try { 
                        float max = 0;
                        foreach (var c in gpuEngineCounters) {
                            float v = c.NextValue();
                            if (v > max) max = v;
                        }
                        newGpu = max;
                    } catch { }
                }

                newVram = Math.Min(100, Math.Max(0, newVram));
                newGpu = Math.Min(100, Math.Max(0, newGpu));

                try {
                    foreach (var drive in System.IO.DriveInfo.GetDrives()) {
                        if (drive.IsReady && drive.DriveType == System.IO.DriveType.Fixed) {
                            newDisks.Add(new DiskInfo { 
                                Name = drive.Name.Replace("\\", ""), 
                                FreeBytes = drive.AvailableFreeSpace, 
                                TotalBytes = drive.TotalSize 
                            });
                        }
                    }
                } catch { }

                List<ProcessVramInfo> newLlmConsumers = null;
                if (_pollCount % 5 == 0)
                {
                    try { newLlmConsumers = GetLlmProcesses(); } catch { }
                }

                lock (_syncLock) {
                    _cpuUsage = newCpu;
                    _cpuTempCelsius = newCpuTemp;
                    _ramUsage = newRam;
                    _vramUsage = newVram;
                    _gpuUsage = newGpu;
                    _gpuTempCelsius = newGpuTemp;
                    _diskMetrics = newDisks;
                    
                    _cpuHistory.Update(newCpu);
                    _ramHistory.Update(newRam);
                    _vramHistory.Update(newVram);
                    _gpuHistory.Update(newGpu);

                    if (newLlmConsumers != null)
                        _topVramConsumers = newLlmConsumers;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("UpdateMetrics loop failed", ex);
            }
            finally
            {
                _pollCount++;
                _pollingTimer.Start(); // Resume timer
            }
        }

        public void Dispose()
        {
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            cpuCounter?.Dispose();
            foreach (var c in gpuEngineCounters) c?.Dispose();
            foreach (var c in vramUsageCounters) c?.Dispose();
            foreach (var c in vramCapacityCounters) c?.Dispose();
            if (_nvmlInitialized) NativeMethods.nvmlShutdown();
        }

        private float GetCpuTemperature()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        double kelvin10 = Convert.ToDouble(obj["CurrentTemperature"]);
                        float c = (float)(kelvin10 / 10.0 - 273.15);
                        if (c > 0) return c;
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("WMI ThermalZone (root/WMI) failed: " + ex.Message); }

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\CIMV2", "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        double temp = Convert.ToDouble(obj["HighPrecisionTemperature"]);
                        if (temp > 2000) return (float)(temp / 10.0 - 273.15);
                        if (temp > 0) return (float)temp;
                    }
                }
            }
            catch (Exception ex) { Logger.Warn("WMI ThermalZone (root/CIMV2) failed: " + ex.Message); }

            return 0;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
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
            public MEMORYSTATUSEX() { this.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }

        private int _pollCount = 0;
        private List<ProcessVramInfo> _topVramConsumers = new List<ProcessVramInfo>();
        private static readonly string[] LLM_BINARIES = { "python", "python3", "llama", "ollama", "lmstudio", "vllm", "text-generation" };

        private List<ProcessVramInfo> GetLlmProcesses()
        {
            var results = new List<ProcessVramInfo>();
            var httpResults = GetLlmProcessesViaHttp();
            results.AddRange(httpResults);
            if (_nvmlDevice != IntPtr.Zero)
            {
                try
                {
                    uint infoCount = 64;
                    nvmlProcessInfo_t[] infos = new nvmlProcessInfo_t[infoCount];
                    if (NativeMethods.nvmlDeviceGetComputeRunningProcesses(_nvmlDevice, ref infoCount, infos) == 0)
                        ProcessNvmlInfos(infos, infoCount, results);
                    
                    infoCount = 64;
                    if (NativeMethods.nvmlDeviceGetGraphicsRunningProcesses(_nvmlDevice, ref infoCount, infos) == 0)
                        ProcessNvmlInfos(infos, infoCount, results);
                }
                catch { }
            }
            results = results.GroupBy(p => p.Name.ToLower()).Select(g => g.First()).ToList();
            results.Sort((a, b) => {
                if (a.IsDead != b.IsDead) return a.IsDead ? 1 : -1;
                return b.UsedBytes.CompareTo(a.UsedBytes);
            });
            return results.Take(6).ToList();
        }

        private void ProcessNvmlInfos(nvmlProcessInfo_t[] infos, uint count, List<ProcessVramInfo> results)
        {
            for (int i = 0; i < count; i++)
            {
                var info = infos[i];
                if (info.usedGpuMemory == 0) continue;
                try
                {
                    var proc = Process.GetProcessById((int)info.pid);
                    string name = proc.ProcessName.ToLower();
                    if (LLM_BINARIES.Any(b => name.Contains(b)))
                    {
                         results.Add(new ProcessVramInfo { Pid = info.pid, Name = proc.ProcessName, UsedBytes = info.usedGpuMemory });
                    }
                }
                catch { }
            }
        }

        private List<ProcessVramInfo> GetLlmProcessesViaHttp()
        {
            var list = new List<ProcessVramInfo>();
            var settings = ConfigurationService.Load();
            if (settings.LlmEndpoints == null) return list;
            foreach (var t in settings.LlmEndpoints)
            {
                bool success = false;
                try
                {
                    System.Net.ServicePointManager.Expect100Continue = false;
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create($"http://127.0.0.1:{t.Port}{t.Path}");
                    req.Timeout = (t.Method == "POST") ? 1000 : 800;
                    req.Method = t.Method ?? "GET";
                    if (req.Method == "POST" && !string.IsNullOrEmpty(t.PostPayload))
                    {
                        req.ContentType = "application/json";
                        using (var writer = new System.IO.StreamWriter(req.GetRequestStream())) { writer.Write(t.PostPayload); }
                    }
                    using (var resp = req.GetResponse()) { success = true; }
                }
                catch { }
                list.Add(new ProcessVramInfo { Name = t.Name, UsedBytes = success ? (ulong)t.VramEstimateBytes : 0, IsDead = !success });
            }
            return list;
        }
    }
}
