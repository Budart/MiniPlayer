using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MiniPlayer
{
    // Todas as chamadas diretas ao Windows (Win32) num só lugar
    static class Native
    {
        public struct RECT { public int L, T, R, B; }
        public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cb; public RECT rc, work; public uint f; }

        public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        public const uint WM_MOUSELEAVE = 0x02A3;

        public delegate bool EnumProc(IntPtr h, IntPtr p);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr h, int o, int c, uint t, uint ms);
        public delegate IntPtr LowLevelProc(int code, IntPtr wp, IntPtr lp);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc f, IntPtr p);
        [DllImport("user32.dll")] public static extern IntPtr FindWindow(string c, string w);
        [DllImport("user32.dll")] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string c, string w);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint f);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc p, uint pid, uint tid, uint fl);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, LowLevelProc p, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr wp, IntPtr lp);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int st);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string n);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool DeleteFile(string path);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);

        // nome da classe de uma janela, com um único buffer reaproveitado (chamado várias vezes por segundo, só na thread da interface)
        [ThreadStatic] static StringBuilder classBuf;
        public static string ClassName(IntPtr h)
        {
            var sb = classBuf ??= new StringBuilder(64);
            sb.Clear(); GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool MonitorOf(IntPtr h, out MONITORINFO mi)
        {
            mi = new MONITORINFO { cb = Marshal.SizeOf<MONITORINFO>() };
            return GetMonitorInfo(MonitorFromWindow(h, 2 /*mais próximo*/), ref mi);
        }
    }
}
