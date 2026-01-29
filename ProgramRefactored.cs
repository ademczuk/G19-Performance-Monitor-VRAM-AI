using System;
using System.Windows.Forms;

namespace G19PerformanceMonitorVRAM
{
    static class ProgramRefactored
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppSettings settings = ConfigurationService.Load();
            PerformanceMonitorRefactored monitor = new PerformanceMonitorRefactored(settings.PollingIntervalMs);
            PerformanceMonitorAppletRefactored applet = new PerformanceMonitorAppletRefactored();
            if (applet.Initialize(monitor, settings)) Application.Run();
            applet.Shutdown();
            monitor.Dispose();
        }
    }
}
