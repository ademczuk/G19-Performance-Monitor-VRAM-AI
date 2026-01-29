using System;
using System.Runtime.InteropServices;

namespace G19PerformanceMonitorVRAM
{
    public static class LogitechLcdSDK
    {
        public const int LOGI_LCD_TYPE_MONO = 0x00000001;
        public const int LOGI_LCD_TYPE_COLOR = 0x00000002;
        public const int LOGI_LCD_COLOR_BUTTON_LEFT = 0x00000100;
        public const int LOGI_LCD_COLOR_BUTTON_RIGHT = 0x00000200;
        private const string DLL_NAME = "LogitechLcd.dll";
        
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiLcdInit(string name, int type);
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiLcdIsConnected(int type);
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiLcdIsButtonPressed(int button);
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void LogiLcdUpdate();
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern void LogiLcdShutdown();
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern bool LogiLcdColorSetBackground(byte[] bitmap);

        public static bool Init(string name, int type) => LogiLcdInit(name, type);
        public static bool IsConnected(int type) => LogiLcdIsConnected(type);
        public static bool IsButtonPressed(int button) => LogiLcdIsButtonPressed(button);
        public static void Update() => LogiLcdUpdate();
        public static void Shutdown() => LogiLcdShutdown();
        public static bool ColorSetBackground(byte[] bmp) => LogiLcdColorSetBackground(bmp);
    }
}
