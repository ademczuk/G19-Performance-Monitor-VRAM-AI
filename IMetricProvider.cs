using System;

namespace G19PerformanceMonitorVRAM
{
    /// <summary>
    /// Interface for hardware metric providers.
    /// Allows for decoupling different monitoring sources (PerfCounters, NVAPI, etc.)
    /// </summary>
    public struct DiskInfo
    {
        public string Name;
        public long FreeBytes;
        public long TotalBytes;
        public float PercentFree => TotalBytes > 0 ? (float)FreeBytes / TotalBytes * 100f : 0;
        public float FreeGB => FreeBytes / 1024f / 1024f / 1024f;
    }

    public struct ProcessVramInfo
    {
        public uint Pid;
        public string Name;
        public ulong UsedBytes;
        public float UsedGB => UsedBytes / 1024f / 1024f / 1024f;
        public float PercentVram;
        public bool IsDead;
    }

    public interface IMetricProvider : IDisposable
    {
        /// <summary>
        /// Name of the provider.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Latest CPU Usage percentage.
        /// </summary>
        float CpuUsage { get; }

        /// <summary>
        /// Latest RAM Usage percentage.
        /// </summary>
        float RamUsage { get; }

        /// <summary>
        /// Latest VRAM Usage percentage.
        /// </summary>
        float VRamUsage { get; }

        /// <summary>
        /// Latest GPU Utilization percentage.
        /// </summary>
        float GpuUsage { get; }

        float TotalVramGB { get; }
        float TotalRamGB { get; }

        float[] CpuHistory { get; }
        float[] RamHistory { get; }
        float[] VRamHistory { get; }
        float[] GpuHistory { get; }

        System.Collections.Generic.IEnumerable<DiskInfo> DiskMetrics { get; }
        System.Collections.Generic.IEnumerable<ProcessVramInfo> TopVramConsumers { get; }

        /// <summary>
        /// True if the provider is currently initialized and monitoring.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Triggers a refresh of all metrics. 
        /// </summary>
        void Update();
    }
}
