using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace G19PerformanceMonitorVRAM
{
    public class PerformanceMonitorAppletRefactored
    {
        private const int LCD_WIDTH = 320;
        private const int LCD_HEIGHT = 240;
        private IntPtr lcdConnection = IntPtr.Zero;
        private Timer updateTimer;
        private Bitmap lcdBitmap;
        private Graphics lcdGraphics;
        private IMetricProvider perfMonitor;
        private AppSettings settings;
        private byte[] pixelBuffer;
        private int currentPage = 0;
        private lastButtonPressTime = 0;
        private const int BUTTON_DEBOUNCE_MS = 300;

        private readonly Color COLOR_BG = Color.Black;
        private readonly Color COLOR_GRAPH_BG = Color.Black;
        private readonly Color COLOR_CPU = Color.FromArgb(0, 224, 0);
        private readonly Color COLOR_RAM = Color.FromArgb(0, 255, 255);
        private readonly Color COLOR_VRAM = Color.FromArgb(255, 0, 255);
        private readonly Color COLOR_GPU = Color.FromArgb(255, 0, 0);

        public bool Initialize(IMetricProvider provider, AppSettings appSettings)
        {
            try
            {
                perfMonitor = provider;
                settings = appSettings;
                int targetTypes = LogitechLcdSDK.LOGI_LCD_TYPE_COLOR | LogitechLcdSDK.LOGI_LCD_TYPE_MONO;
                lcdConnection = LogitechLcdSDK.LogiLcdInit("Performance Monitor 2026", targetTypes);
                if (lcdConnection == IntPtr.Zero) return false;
                lcdBitmap = new Bitmap(LCD_WIDTH, LCD_HEIGHT, PixelFormat.Format32bppArgb);
                lcdGraphics = Graphics.FromImage(lcdBitmap);
                lcdGraphics.SmoothingMode = SmoothingMode.None;
                lcdGraphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                updateTimer = new Timer();
                updateTimer.Interval = settings.RenderingIntervalMs;
                updateTimer.Tick += UpdateTimer_Tick;
                updateTimer.Start();
                return true;
            } catch { return false; }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try { HandleInput(); DrawLCD(); UpdateLCD(); } catch { }
        }

        private void HandleInput()
        {
            int now = Environment.TickCount;
            if (now - lastButtonPressTime < BUTTON_DEBOUNCE_MS) return;
            bool rightPressed = LogitechLcdSDK.LogiLcdIsButtonPressed(LogitechLcdSDK.LOGI_LCD_COLOR_BUTTON_RIGHT);
            bool leftPressed = LogitechLcdSDK.LogiLcdIsButtonPressed(LogitechLcdSDK.LOGI_LCD_COLOR_BUTTON_LEFT);
            if (rightPressed || leftPressed) {
                lastButtonPressTime = now;
                currentPage = (currentPage + 1) % 1;
            }
        }

        private void DrawLCD() { lcdGraphics.Clear(COLOR_BG); DrawUnifiedDashboard(); }

        private void DrawUnifiedDashboard()
        {
            try 
            {
                int margin = 5; int hGap = 4;
                int graphW = (LCD_WIDTH - (margin * 2) - hGap) / 2;
                int graphH = 80; int y = 5;
                float cpu = perfMonitor.CpuUsage;
                float ramPct = perfMonitor.RamUsage;
                float ramUsedGB = (ramPct / 100.0f) * perfMonitor.TotalRamGB;
                float gpu = perfMonitor.GpuUsage;
                float vramPct = perfMonitor.VRamUsage;
                float vramUsedGB = (vramPct / 100.0f) * perfMonitor.TotalVramGB;
                float vramTotalGB = perfMonitor.TotalVramGB;

                DrawDualGraph(margin, y, graphW, graphH, perfMonitor.CpuHistory, COLOR_CPU, $"CPU:{cpu:F0}%", perfMonitor.RamHistory, COLOR_RAM, $"RAM:{ramUsedGB:F1}G ({ramPct:F0}%)");
                DrawDualGraph(margin + graphW + hGap, y, graphW, graphH, perfMonitor.GpuHistory, COLOR_GPU, $"GPU:{gpu:F0}%", perfMonitor.VRamHistory, COLOR_VRAM, $"VRAM:{vramUsedGB:F1}/{vramTotalGB:F0}G ({vramPct:F0}%)");
                y += graphH + 8;

                using (Font driveFont = new Font("Consolas", 9, FontStyle.Bold))
                {
                    int dCount = 0; string line = "DRIVES: ";
                    lcdGraphics.DrawLine(new Pen(Color.FromArgb(50, 50, 50)), margin, y, LCD_WIDTH - margin, y);
                    y += 3;
                    foreach (var d in perfMonitor.DiskMetrics)
                    {
                        if (dCount > 0 && dCount % 3 == 0) {
                            lcdGraphics.DrawString(line, driveFont, Brushes.Orange, margin, y);
                            y += 12; line = "        "; 
                        }
                        line += $"{d.Name.Split(':')[0]}:{d.FreeGB:F0}G({d.PercentFree:F0}%) ";
                        dCount++;
                    }
                    if (dCount > 0) { lcdGraphics.DrawString(line, driveFont, Brushes.Orange, margin, y); y += 15; }
                }

                lcdGraphics.DrawLine(new Pen(Color.FromArgb(50, 50, 50)), margin, y, LCD_WIDTH - margin, y);
                y += 2;
                using (Font titleFont = new Font("Arial", 9, FontStyle.Bold))
                    lcdGraphics.DrawString("ACTIVE LLMs (VRAM)", titleFont, Brushes.White, margin, y);
                y += 14;

                var consumers = perfMonitor.TopVramConsumers;
                using (Font llmFont = new Font("Consolas", 10, FontStyle.Bold))
                {
                    int count = 0;
                    foreach (var proc in consumers)
                    {
                        if (count >= 5) break; 
                        if (proc.IsDead) lcdGraphics.DrawString($"{proc.Name} [DEAD]", llmFont, Brushes.Gray, margin, y);
                        else {
                            float pct = (proc.UsedGB / perfMonitor.TotalVramGB) * 100.0f;
                            lcdGraphics.DrawString($"{proc.Name}: {proc.UsedGB:F1}G ({pct:F0}%)", llmFont, Brushes.Cyan, margin, y);
                        }
                        y += 13; count++;
                    }
                }
            } catch { }
        }

        private void DrawDualGraph(int x, int y, int w, int h, float[] data1, Color color1, string label1, float[] data2, Color color2, string label2)
        {
            lcdGraphics.FillRectangle(new SolidBrush(COLOR_GRAPH_BG), x, y, w, h);
            DrawSingleLine(x, y, w, h, data1, color1);
            DrawSingleLine(x, y, w, h, data2, color2);
            using (Font font = new Font("Arial", 7, FontStyle.Bold)) {
                lcdGraphics.DrawString(label1, font, new SolidBrush(color1), x + 2, y + 2);
                lcdGraphics.DrawString(label2, font, new SolidBrush(color2), x + 2 + lcdGraphics.MeasureString(label1, font).Width + 2, y + 2);
            }
        }

        private void DrawSingleLine(int x, int y, int w, int h, float[] data, Color color)
        {
            if (data == null || data.Length < 2) return;
            using (Pen p = new Pen(color, 1)) {
                PointF[] pts = new PointF[data.Length];
                float step = (float)w / data.Length;
                for (int i = 0; i < data.Length; i++)
                    pts[i] = new PointF(x + (i * step), y + h - (data[i] / 100f * h));
                lcdGraphics.DrawLines(p, pts);
            }
        }

        private void UpdateLCD()
        {
            BitmapData bmpData = lcdBitmap.LockBits(new Rectangle(0, 0, LCD_WIDTH, LCD_HEIGHT), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                if (pixelBuffer == null) pixelBuffer = new byte[LCD_WIDTH * LCD_HEIGHT * 4];
                Marshal.Copy(bmpData.Scan0, pixelBuffer, 0, pixelBuffer.Length);
                LogitechLcdSDK.LogiLcdColorSetBackground(pixelBuffer);
                LogitechLcdSDK.LogiLcdUpdate();
            } finally { lcdBitmap.UnlockBits(bmpData); }
        }

        public void Shutdown() { updateTimer?.Stop(); LogitechLcdSDK.LogiLcdShutdown(); }
    }
}
