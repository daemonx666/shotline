namespace Shotline;

internal static class Native
{
    internal const int HotKeyMessage = 0x0312, ClipboardMessage = 0x031D,
        DisplayMessage = 0x007E, DpiMessage = 0x02E0, SettingsMessage = 0x001A;
    private const int ExStyle = -20;
    private const long ToolWindow = 0x80, NoActivate = 0x08000000, Transparent = 0x20;
    internal static readonly IntPtr Topmost = new(-1);

    [StructLayout(LayoutKind.Sequential)] internal struct RECT
    { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top;
      public bool Contains(POINT p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct MONITORINFO
    { public int Size; public RECT Bounds, Work; public uint Flags; }
    internal sealed record Monitor(IntPtr Handle, RECT Bounds, RECT Work, double Scale);

    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr param);
    internal delegate void WinEventProc(IntPtr hook, uint eventId, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);

    internal static Monitor AtPointer()
    {
        GetCursorPos(out var point);
        return ReadMonitor(MonitorFromPoint(point, 2));
    }
    internal static Monitor ReadMonitor(IntPtr handle)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(handle, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = GetDpiForMonitor(handle, 0, out var dpi, out _);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        return new(handle, info.Bounds, info.Work, dpi / 96.0);
    }
    internal static IntPtr Configure(Window window, bool passThrough)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var style = GetWindowLongPtr(handle, ExStyle).ToInt64() | ToolWindow | NoActivate;
        if (passThrough) style |= Transparent;
        SetWindowLongPtr(handle, ExStyle, new IntPtr(style));
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
            return IntPtr.Zero;
        });
        return handle;
    }
    internal static void Place(Window window, int x, int y, double width, double height, double scale)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (!SetWindowPos(handle, Topmost, x, y, (int)Math.Ceiling(width * scale),
                (int)Math.Ceiling(height * scale), 0x10 | 0x04))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static bool IsFullscreen(Monitor monitor)
    {
        var full = false;
        var center = new POINT { X = monitor.Bounds.Left + monitor.Bounds.Width / 2,
            Y = monitor.Bounds.Top + monitor.Bounds.Height / 2 };
        // Topmost eligible window at the monitor's centre approximates the visible app.
        // Ordinary captioned maximized windows and the desktop are explicitly excluded.
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || IsIconic(window)) return true;
            GetWindowThreadProcessId(window, out var process);
            if (process == Environment.ProcessId) return true;
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
            if (DwmGetWindowAttribute(window, 14, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            if (!GetWindowRect(window, out var rect) || !rect.Contains(center)) return true;
            var caption = (GetWindowLongPtr(window, -16).ToInt64() & 0x00C00000) != 0;
            full = !(caption && IsZoomed(window)) && rect.Left <= monitor.Bounds.Left + 2 &&
                rect.Top <= monitor.Bounds.Top + 2 && rect.Right >= monitor.Bounds.Right - 2 &&
                rect.Bottom >= monitor.Bounds.Bottom - 2;
            return false;
        }, IntPtr.Zero);
        return full;
    }
    internal static bool IsOurForeground()
    { GetWindowThreadProcessId(GetForegroundWindow(), out var process); return process == Environment.ProcessId; }
}
