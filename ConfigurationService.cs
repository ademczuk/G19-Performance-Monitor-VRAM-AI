using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace G19PerformanceMonitorVRAM
{
    [DataContract]
    public class LlmTarget
    {
        [DataMember] public int Port { get; set; }
        [DataMember] public string Path { get; set; } = "/";
        [DataMember] public string Name { get; set; }
        [DataMember] public long VramEstimateBytes { get; set; } 
        [DataMember] public string Method { get; set; } = "GET"; 
        [DataMember] public string PostPayload { get; set; } 
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public int PollingIntervalMs { get; set; } = 1000;
        [DataMember] public int RenderingIntervalMs { get; set; } = 50; 
        [DataMember] public bool EnableGpsTemperature { get; set; } = true;
        [DataMember] public string CpuColor { get; set; } = "#50FF50"; 
        [DataMember] public string RamColor { get; set; } = "#508CFF"; 
        [DataMember] public string VRamColor { get; set; } = "#FF50FF"; 
        [DataMember] public int DefaultPage { get; set; } = 0;
        [DataMember] public List<LlmTarget> LlmEndpoints { get; set; } = new List<LlmTarget>();

        public AppSettings()
        {
            LlmEndpoints.Add(new LlmTarget { Port = 8000, Name = "Jina-Reranker", VramEstimateBytes = 3L * 1024 * 1024 * 1024 });
            LlmEndpoints.Add(new LlmTarget { Port = 8001, Name = "Qwen3-Embed", VramEstimateBytes = 10L * 1024 * 1024 * 1024 });
            LlmEndpoints.Add(new LlmTarget { Port = 8002, Name = "FunctionGemma", VramEstimateBytes = 512L * 1024 * 1024, Method = "POST", Path = "/route", PostPayload = "{\"query\":\"health\"}" });
        }
    }

    public static class ConfigurationService
    {
        private static readonly string ConfigPath;
        static ConfigurationService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "G19PerformanceMonitor");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            ConfigPath = Path.Combine(folder, "settings.json");
        }

        public static AppSettings Load()
        {
            if (!File.Exists(ConfigPath))
            {
                var defaults = new AppSettings();
                Save(defaults);
                return defaults;
            }

            try {
                AppSettings settings;
                using (var fs = File.OpenRead(ConfigPath)) {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    settings = (AppSettings)serializer.ReadObject(fs);
                }

                if (settings.LlmEndpoints == null || settings.LlmEndpoints.Count == 0)
                {
                    Logger.Info("Config found but LlmEndpoints missing/empty. Adding defaults.");
                    settings.LlmEndpoints = new AppSettings().LlmEndpoints;
                    Save(settings); 
                }
                return settings;
            } catch { return new AppSettings(); }
        }

        public static void Save(AppSettings settings)
        {
            try {
                using (var fs = File.Create(ConfigPath)) {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    serializer.WriteObject(fs, settings);
                }
            } catch { }
        }
    }
}
