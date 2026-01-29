using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Timers;

namespace G19PerformanceMonitorVRAM
{
    // Native Memory API
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    public class MEMORYSTATUSEX
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

        public MEMORYSTATUSEX()
        {
            this.dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    public static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx([System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] MEMORYSTATUSEX lpBuffer);

        [System.Runtime.InteropServices.DllImport("dxgi.dll")]
        public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

        private const string NVMLDLL = "nvml.dll";

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

        public static uint GetNvmlVersion(int size, int version)
        {
            return (uint)(size | (version << 24));
        }
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

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("645967A4-1392-4310-A798-8053CE3E93FD")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter3 
    {
        void _0(); void _1(); void _2(); void _3();
        void _4(); void _5(); void _6();
        void _7();
        void _8();
        [System.Runtime.InteropServices.PreserveSig]
        int QueryVideoMemoryInfo(uint NodeIndex, int MemorySegmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO pVideoMemoryInfo);
    }

    [System.Runtime.InteropServices.ComImport]
    [System.Runtime.InteropServices.Guid("7b7166ec-21c7-44ae-b21a-c9ae321ae369")]
    [System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIFactory
    {
        [System.Runtime.InteropServices.PreserveSig]
        int EnumAdapters(uint Adapter, out IntPtr ppAdapter);
    }

    public class HistoryBuffer
    {
        private float[] _data;
        private int _points;
        private readonly object _lock = new object();

        public HistoryBuffer(int points)
        {
            _points = points;
            _data = new float[points];
        }

        public void Update(float val)
        {
            lock (_lock)
            {
                Array.Copy(_data, 1, _data, 0, _points - 1);
                _data[_points - 1] = val;
            }
        }

        public float[] GetData()
        {
            lock (_lock)
            {
                float[] copy = new float[_points];
                Array.Copy(_data, copy, _points);
                return copy;
            }
        }
    }

    public class PerformanceMonitorRefactored : IMetricProvider
    {
        private PerformanceCounter cpuCounter;
        private List<PerformanceCounter> vramUsageCounters = new List<PerformanceCounter>();
        private List<PerformanceCounter> vramCapacityCounters = new List<PerformanceCounter>();
        private PerformanceCounter gpuLoadCounter;

        private IDXGIAdapter3 _dxgiAdapter;
        private IntPtr _nvmlDevice = IntPtr.Zero;
        private bool _nvmlInitialized = false;

        private readonly object _syncLock = new object();
        private float _cpuUsage;
        private float _ramUsage;
        private float _vramUsage;
        private float _gpuUsage;
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
        public float RamUsage { get { lock (_syncLock) return _ramUsage; } } 
        public float VRamUsage { get { lock (_syncLock) return _vramUsage; } }
        public float GpuUsage => _gpuUsage;
        public float TotalVramGB => _totalVramGB;
        public float TotalRamGB => _totalRamGB;

        public float[] CpuHistory => _cpuHistory.GetData();
        public float[] RamHistory => _ramHistory.GetData();
        public float[] VRamHistory => _vramHistory.GetData();
        public float[] GpuHistory => _gpuHistory.GetData();

        public IEnumerable<DiskInfo> DiskMetrics { get { lock (_syncLock) return new List<DiskInfo>(_diskMetrics); } }
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
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
            } catch (Exception ex) { Logger.Error("CPU init failed", ex); }

            try {
                if (NativeMethods.nvmlInit() == 0) {
                    _nvmlInitialized = true;
                    uint count;
                    if (NativeMethods.nvmlDeviceGetCount(out count) == 0 && count > 0) {
                        if (NativeMethods.nvmlDeviceGetHandleByIndex(0, out _nvmlDevice) == 0) {
                            Logger.Info("Successfully bound NVML v2 Device 0.");
                        }
                    }
                }
            } catch { }

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
                            }
                        }
                    }
                } catch { }
            }

            InitializeFallbackCounters();
        }

        private void InitializeFallbackCounters()
        {
            vramUsageCounters.Clear();
            vramCapacityCounters.Clear();
            try {
                if (PerformanceCounterCategory.Exists("GPU Adapter Memory")) {
                    var cat = new PerformanceCounterCategory("GPU Adapter Memory");
                    string best = null; long max = -1;
                    foreach (var name in cat.GetInstanceNames().Where(n => n.Contains("phys"))) {
                        try {
                            using (var counter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Limit", name)) {
                                counter.NextValue();
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
                    var inst = cat.GetInstanceNames().FirstOrDefault(n => n.Contains("engtype_3D"));
                    if (inst != null) {
                        gpuLoadCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                    }
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

            float newCpu = 0, newRam = 0, newVram = 0, newGpu = 0;
            var newDisks = new List<DiskInfo>();

            try { if (cpuCounter != null) newCpu = Math.Min(100, Math.Max(0, cpuCounter.NextValue())); } catch { }

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
                }
            } catch { }

            if (!vramFound && _dxgiAdapter != null) {
                try {
                    DXGI_QUERY_VIDEO_MEMORY_INFO memInfo;
                    if (_dxgiAdapter.QueryVideoMemoryInfo(0, 0, out memInfo) == 0 && memInfo.Budget > 0) {
                        newVram = (float)((double)memInfo.CurrentUsage / memInfo.Budget * 100.0);
                        vramFound = true;
                    }
                } catch { }
            }

            if (!vramFound) newVram = GetVramFromCounters();
            if (!gpuFound && gpuLoadCounter != null) {
                try { newGpu = gpuLoadCounter.NextValue(); } catch { }
            }

            newVram = Math.Min(100, Math.Max(0, newVram));
            newGpu = Math.Min(100, Math.Max(0, newGpu));

            try {
                foreach (var drive in System.IO.DriveInfo.GetDrives()) {
                    if (drive.DriveType == System.IO.DriveType.Fixed && drive.IsReady) {
                        newDisks.Add(new DiskInfo {
                            Name = drive.Name.Replace("\\", ""),
                            TotalBytes = drive.TotalSize,
                            FreeBytes = drive.AvailableFreeSpace
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
                _ramUsage = newRam;
                _vramUsage = newVram;
                _gpuUsage = newGpu;
                _diskMetrics = newDisks;
                
                _cpuHistory.Update(newCpu);
                _ramHistory.Update(newRam);
                _vramHistory.Update(newVram);
                _gpuHistory.Update(newGpu);

                if (newLlmConsumers != null)
                    _topVramConsumers = newLlmConsumers;
            }
            
            _pollCount++;
            _pollingTimer.Start(); 
        }

        private int _pollCount = 0;
        private List<ProcessVramInfo> _topVramConsumers = new List<ProcessVramInfo>();
        public IEnumerable<ProcessVramInfo> TopVramConsumers { get { lock (_syncLock) return new List<ProcessVramInfo>(_topVramConsumers); } }

        private static readonly string[] LLM_BINARIES = { "python", "python3", "llama", "ollama", "lmstudio", "vllm", "text-generation" };

        private List<ProcessVramInfo> GetLlmProcesses()
        {
            var results = new List<ProcessVramInfo>();
            if (_nvmlDevice != IntPtr.Zero)
            {
                try
                {
                    uint count = 0;
                    if (NativeMethods.nvmlDeviceGetComputeRunningProcesses(_nvmlDevice, ref count, null) == 0 && count > 0)
                    {
                        var infos = new nvmlProcessInfo_t[count * 2];
                        count = (uint)infos.Length;
                        if (NativeMethods.nvmlDeviceGetComputeRunningProcesses(_nvmlDevice, ref count, infos) == 0)
                            ProcessNvmlInfos(infos, count, results);
                    }
                    
                    uint gCount = 0;
                    if (NativeMethods.nvmlDeviceGetGraphicsRunningProcesses(_nvmlDevice, ref gCount, null) == 0 && gCount > 0)
                    {
                        var infos = new nvmlProcessInfo_t[gCount * 2];
                        gCount = (uint)infos.Length;
                        if (NativeMethods.nvmlDeviceGetGraphicsRunningProcesses(_nvmlDevice, ref gCount, infos) == 0)
                             ProcessNvmlInfos(infos, gCount, results);
                    }
                } catch { }
            }

            results = results.GroupBy(p => p.Pid).Select(g => g.First()).ToList();

            if (results.Count == 0)
            {
                var httpResults = GetLlmProcessesViaHttp();
                results.AddRange(httpResults);
            }

            results.Sort((a, b) => b.UsedBytes.CompareTo(a.UsedBytes));
            return results.Take(6).ToList();
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
                    req.Timeout = (t.Method == "POST") ? 5000 : 800; 
                    req.Method = t.Method ?? "GET";

                    if (req.Method == "POST" && !string.IsNullOrEmpty(t.PostPayload))
                    {
                        req.ContentType = "application/json";
                        using (var writer = new System.IO.StreamWriter(req.GetRequestStream()))
                            writer.Write(t.PostPayload);
                    }

                    using (var resp = req.GetResponse()) { success = true; }
                } catch { }

                list.Add(new ProcessVramInfo { 
                    Name = t.Name, 
                    UsedBytes = success ? (ulong)t.VramEstimateBytes : 0,
                    IsDead = !success
                });
            }
            return list;
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
                         results.Add(new ProcessVramInfo { 
                             Pid = info.pid, 
                             Name = proc.ProcessName, 
                             UsedBytes = info.usedGpuMemory 
                         });
                    }
                } catch { }
            }
        }

        public void Dispose()
        {
            _pollingTimer?.Stop();
            _pollingTimer?.Dispose();
            cpuCounter?.Dispose();
            gpuLoadCounter?.Dispose();
            foreach (var c in vramUsageCounters) c?.Dispose();
            foreach (var c in vramCapacityCounters) c?.Dispose();
            if (_nvmlInitialized) NativeMethods.nvmlShutdown();
        }
    }
}
