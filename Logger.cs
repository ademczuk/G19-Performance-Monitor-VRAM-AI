using System;
using System.IO;

namespace G19PerformanceMonitorVRAM
{
    public static class Logger
    {
        private static readonly string LogPath;
        static Logger()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "G19PerformanceMonitor");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            LogPath = Path.Combine(folder, "app.log");
        }

        public static void Info(string message) => Log("INFO", message);
        public static void Warning(string message) => Log("WARN", message);
        public static void Error(string message, Exception ex = null) => Log("ERROR", $"{message}{(ex != null ? $" | {ex.Message}" : "")}");

        private static void Log(string level, string message)
        {
            try {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            } catch { }
        }
    }
}
