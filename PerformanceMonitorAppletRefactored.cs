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
        private const int MAX_CONSECUTIVE_FAILURES = 10;
        private int lastButtonPressTime = 0;
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
                Logger.Info("Initializing LCD Applet v3.5 (CPU+GPU Temp Support)...");

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
            }
            catch (Exception ex)
            {
                Logger.Error("Initialization error in Applet.", ex);
                return false;
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                HandleInput();
                DrawLCD();
                UpdateLCD();
            }
            catch { }
        }

        private void HandleInput()
        {
            int now = Environment.TickCount;
            if (now - lastButtonPressTime < BUTTON_DEBOUNCE_MS) return;

            bool rightPressed = LogitechLcdSDK.LogiLcdIsButtonPressed(LogitechLcdSDK.LOGI_LCD_COLOR_BUTTON_RIGHT) || 
                                LogitechLcdSDK.LogiLcdIsButtonPressed(0x00000008);
            bool leftPressed = LogitechLcdSDK.LogiLcdIsButtonPressed(LogitechLcdSDK.LOGI_LCD_COLOR_BUTTON_LEFT) || 
                               LogitechLcdSDK.LogiLcdIsButtonPressed(0x00000004);

            if (rightPressed || leftPressed)
            {
                lastButtonPressTime = now;
                if (rightPressed) currentPage = (currentPage + 1) % TOTAL_PAGES;
                else currentPage = (currentPage - 1 + TOTAL_PAGES) % TOTAL_PAGES;
            }
        }

        private const int TOTAL_PAGES = 1;

        private void DrawLCD()
        {
            lcdGraphics.Clear(COLOR_BG);
            DrawUnifiedDashboard();
        }

        private void DrawUnifiedDashboard()
        {
            try 
            {
                int margin = 5;
                int hGap = 4;
                int graphW = (LCD_WIDTH - (margin * 2) - hGap) / 2;
                int graphH = 80; 
                int y = 5;

                // Calculate current values for labels
                float cpu = perfMonitor.CpuUsage;
                float cpuTempC = perfMonitor.CpuTempCelsius;
                float cpuTempF = (cpuTempC * 9 / 5) + 32;

                float ramPct = perfMonitor.RamUsage;
                float ramUsedGB = (ramPct / 100.0f) * perfMonitor.TotalRamGB;

                float gpu = perfMonitor.GpuUsage;
                float gpuTempC = perfMonitor.GpuTempCelsius;
                float gpuTempF = (gpuTempC * 9 / 5) + 32;

                float vramPct = perfMonitor.VRamUsage;
                float vramUsedGB = (vramPct / 100.0f) * perfMonitor.TotalVramGB;
                float vramTotalGB = perfMonitor.TotalVramGB;

                // LEFT: CPU (Green) & RAM (Cyan)
                string cpuLabel = $"CPU:{cpu:F0}%";
                string cpuTempLabel = cpuTempC > 0 ? $"{cpuTempC:F0}°C / {cpuTempF:F0}°F" : "";

                DrawDualGraph(margin, y, graphW, graphH, 
                    perfMonitor.CpuHistory, COLOR_CPU, cpuLabel, 
                    perfMonitor.RamHistory, COLOR_RAM, $"RAM:{ramUsedGB:F1}G ({ramPct:F0}%)",
                    cpuTempLabel);

                // RIGHT: GPU (Red) & VRAM (Magenta)
                string gpuLabel = $"GPU:{gpu:F0}%";
                string tempLabel = $"{gpuTempC:F0}°C / {gpuTempF:F0}°F";

                DrawDualGraph(margin + graphW + hGap, y, graphW, graphH, 
                    perfMonitor.GpuHistory, COLOR_GPU, gpuLabel, 
                    perfMonitor.VRamHistory, COLOR_VRAM, $"VRAM:{vramUsedGB:F1}/{vramTotalGB:F0}G ({vramPct:F0}%)",
                    tempLabel);

                y += graphH + 8;

                using (Font driveFont = new Font("Consolas", 9, FontStyle.Bold))
                {
                    int dCount = 0;
                    string line = "DRIVES: ";
                    lcdGraphics.DrawLine(new Pen(Color.FromArgb(50, 50, 50)), margin, y, LCD_WIDTH - margin, y);
                    y += 3;

                    foreach (var d in perfMonitor.DiskMetrics)
                    {
                        if (dCount > 0 && dCount % 3 == 0) 
                        {
                            lcdGraphics.DrawString(line, driveFont, Brushes.Orange, margin, y);
                            y += 12; line = "        "; 
                        }
                        string driveName = d.Name.Split(':')[0];
                        line += $"{driveName}:{d.FreeGB:F0}G({d.PercentFree:F0}%) ";
                        dCount++;
                    }
                    if (dCount > 0)
                    {
                        lcdGraphics.DrawString(line, driveFont, Brushes.Orange, margin, y);
                        y += 15;
                    }
                }

                lcdGraphics.DrawLine(new Pen(Color.FromArgb(50, 50, 50)), margin, y, LCD_WIDTH - margin, y);
                y += 2;

                using (Font titleFont = new Font("Arial", 9, FontStyle.Bold))
                {
                    lcdGraphics.DrawString("ACTIVE LLMs (VRAM)", titleFont, Brushes.White, margin, y);
                }
                y += 14; 

                var consumers = perfMonitor.TopVramConsumers;
                using (Font llmFont = new Font("Consolas", 10, FontStyle.Bold))
                {
                    var enumerator = consumers.GetEnumerator();
                    if (!enumerator.MoveNext())
                    {
                        lcdGraphics.DrawString("No Active LLMs", llmFont, Brushes.DarkGray, margin, y);
                    }
                    else
                    {
                        int count = 0;
                        foreach (var proc in consumers)
                        {
                            if (count >= 5) break; 
                            string safeName = proc.Name ?? "Unknown";
                            if (proc.IsDead)
                            {
                                lcdGraphics.DrawString($"{safeName} [DEAD]", llmFont, Brushes.Gray, margin, y);
                            }
                            else
                            {
                                float pct = (proc.UsedGB / perfMonitor.TotalVramGB) * 100.0f;
                                lcdGraphics.DrawString($"{safeName}: {proc.UsedGB:F1}G ({pct:F0}%)", llmFont, Brushes.Cyan, margin, y);
                            }
                            y += 13; count++;
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.Error("Dashboard Render Error", ex); }
        }

        private void DrawDualGraph(int x, int y, int w, int h, 
            float[] data1, Color color1, string label1, 
            float[] data2, Color color2, string label2,
            string subLabel1 = null)
        {
            lcdGraphics.FillRectangle(new SolidBrush(COLOR_GRAPH_BG), x, y, w, h);
            lcdGraphics.DrawRectangle(new Pen(Color.FromArgb(40, 40, 40)), x, y, w, h);
            DrawSingleLine(x, y, w, h, data1, color1);
            DrawSingleLine(x, y, w, h, data2, color2);

            using (Font font = new Font("Arial", 7, FontStyle.Bold))
            {
                lcdGraphics.DrawString(label1, font, new SolidBrush(color1), x + 2, y + 2);
                SizeF s1 = lcdGraphics.MeasureString(label1, font);
                lcdGraphics.DrawString(label2, font, new SolidBrush(color2), x + 2 + s1.Width + 2, y + 2);

                if (!string.IsNullOrEmpty(subLabel1))
                {
                    lcdGraphics.DrawString(subLabel1, font, new SolidBrush(color1), x + 2, y + 2 + s1.Height - 1);
                }
            }
        }

        private void DrawSingleLine(int x, int y, int w, int h, float[] data, Color color)
        {
            if (data == null || data.Length < 2) return;
            using (Pen p = new Pen(color, 1))
            {
                int points = data.Length;
                float step = (float)w / points;
                PointF[] pts = new PointF[points];
                for (int i = 0; i < points; i++)
                {
                      float val = data[i];
                      float py = y + h - (val / 100f * h);
                      if (py < y) py = y; if (py > y + h) py = y + h;
                      pts[i] = new PointF(x + (i * step), py);
                }
                lcdGraphics.DrawLines(p, pts);
            }
        }

        private void UpdateLCD()
        {
            if (lcdBitmap == null) return;
            BitmapData data = lcdBitmap.LockBits(new Rectangle(0, 0, LCD_WIDTH, LCD_HEIGHT), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try {
                if (pixelBuffer == null) pixelBuffer = new byte[LCD_WIDTH * LCD_HEIGHT * 4];
                Marshal.Copy(data.Scan0, pixelBuffer, 0, pixelBuffer.Length);
                LogitechLcdSDK.LogiLcdColorSetBackground(pixelBuffer);
                LogitechLcdSDK.LogiLcdUpdate();
            }
            finally { lcdBitmap.UnlockBits(data); }
        }
    }
}
