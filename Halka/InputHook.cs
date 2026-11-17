using System.Runtime.InteropServices;

namespace Halka;

sealed class InputHook : IDisposable
{
    public event Action<int, int>? MouseDown;
    public event Action<int, int>? MouseUp;
    public event Action<int, int>? MouseMove;
    public event Action<int>? Wheel;
    public event Action? Escape;
    public event Action? Tab;

    readonly NativeMethods.HookProc _mouse;
    readonly NativeMethods.HookProc _key;
    IntPtr _mouseHook;
    IntPtr _keyHook;
    bool _tabDown;
    bool _disposed;

    public bool Installed { get; private set; }

    public InputHook()
    {
        _mouse = MouseProc;
        _key = KeyProc;
    }

    public void Install()
    {
        var module = NativeMethods.GetModuleHandle(null);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouse, module, 0);
        _keyHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _key, module, 0);
        Installed = _mouseHook != IntPtr.Zero && _keyHook != IntPtr.Zero;
    }

    IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.MouseHook>(lParam);
            var msg = (int)wParam;
            if (msg == NativeMethods.WmLButtonDown)
                MouseDown?.Invoke(data.Point.X, data.Point.Y);
            else if (msg == NativeMethods.WmLButtonUp)
                MouseUp?.Invoke(data.Point.X, data.Point.Y);
            else if (msg == NativeMethods.WmMouseMove)
                MouseMove?.Invoke(data.Point.X, data.Point.Y);
            else if (msg == NativeMethods.WmMouseWheel)
            {
                var delta = (short)((data.MouseData >> 16) & 0xffff);
                if (delta != 0)
                    Wheel?.Invoke(delta);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = (int)wParam;
            if (msg is NativeMethods.WmKeyDown or NativeMethods.WmKeyUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KeyHook>(lParam);
                var up = msg == NativeMethods.WmKeyUp || (data.Flags & NativeMethods.LlkhfUp) != 0;
                if (data.VkCode == NativeMethods.VkEscape && !up)
                    Escape?.Invoke();
                else if (data.VkCode == NativeMethods.VkTab)
                {
                    if (up)
                        _tabDown = false;
                    else if (!_tabDown)
                    {
                        _tabDown = true;
                        Tab?.Invoke();
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_mouseHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
        if (_keyHook != IntPtr.Zero)
            NativeMethods.UnhookWindowsHookEx(_keyHook);
    }
}
