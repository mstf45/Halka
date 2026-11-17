using System.Runtime.InteropServices;

namespace Halka;

static class NativeMethods
{
    public const int GwlExStyle = -20;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WhMouseLl = 14;
    public const int WhKeyboardLl = 13;
    public const int WmMouseMove = 0x0200;
    public const int WmLButtonDown = 0x0201;
    public const int WmLButtonUp = 0x0202;
    public const int WmMouseWheel = 0x020A;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int LlkhfUp = 0x80;
    public const int VkEscape = 0x1B;
    public const int VkTab = 0x09;
    public const int VkControl = 0x11;
    public const uint MonitorDefaultToNearest = 2;

    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MouseHook
    {
        public NativePoint Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    public static extern bool AllocConsole();

    public static uint DpiAt(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
            return dpiX;
        return 96;
    }
}
