namespace Shotline;

internal sealed class DesktopBridge : IDisposable
{
    private readonly HwndSource source;
    private readonly Diagnostics diagnostics;
    private readonly Native.WinEventProc eventProc;
    private readonly IntPtr foregroundHook;
    internal event Action? ToggleRequested;
    internal event Action? ClipboardChanged;
    internal event Action? EnvironmentChanged;
    private bool hotkeyRegistered;
    private bool clipboardRegistered;

    internal DesktopBridge(Diagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        source = new HwndSource(new HwndSourceParameters("Shotline.Messages")
        { Width = 0, Height = 0, WindowStyle = 0, ExtendedWindowStyle = 0x80 });
        source.AddHook(Message);
        clipboardRegistered = Native.AddClipboardFormatListener(source.Handle);
        if (!clipboardRegistered) diagnostics.Fail("clipboard.listen", "剪贴板监听注册失败，请重启晾图。",
            new Win32Exception(Marshal.GetLastWin32Error()));
        eventProc = (_, _, _, _, _, _, _) => Application.Current.Dispatcher.InvokeAsync(() => EnvironmentChanged?.Invoke());
        foregroundHook = Native.SetWinEventHook(3, 3, IntPtr.Zero, eventProc, 0, 0, 0);
        if (foregroundHook == IntPtr.Zero) diagnostics.Event("fullscreen.hook", reason: "polling-only", code: Marshal.GetLastWin32Error());
    }
    internal bool Register(string shortcut)
    {
        if (hotkeyRegistered) Native.UnregisterHotKey(source.Handle, 1);
        hotkeyRegistered = false;
        try
        {
            var (modifiers, key) = ParseHotkey(shortcut);
            if (!Native.RegisterHotKey(source.Handle, 1, modifiers | 0x4000, key))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            hotkeyRegistered = true;
            diagnostics.Hotkey = "已注册：" + shortcut;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or Win32Exception or NotSupportedException)
        {
            diagnostics.Hotkey = "注册失败：" + shortcut + "；请在设置中修改";
            diagnostics.Fail("hotkey.register", "快捷键无效或被占用，请通过托盘打开设置修改。", ex);
            return false;
        }
    }
    internal static (uint Modifiers, uint Key) ParseHotkey(string text)
    {
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        uint modifiers = 0;
        if (parts.Length < 2) throw new ArgumentException("快捷键必须包含修饰键。");
        foreach (var part in parts[..^1])
            modifiers |= part.ToLowerInvariant() switch
            { "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, "win" => 8u,
                _ => throw new ArgumentException("未知修饰键。") };
        var converted = new KeyConverter().ConvertFromInvariantString(parts[^1]);
        if (converted is not Key key || key == Key.None) throw new ArgumentException("无效按键。");
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is < 0x20 or > 0xFE) throw new ArgumentException("不支持此按键。");
        return (modifiers, (uint)virtualKey);
    }
    private IntPtr Message(IntPtr window, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        switch (message)
        {
            case Native.HotKeyMessage: handled = true; ToggleRequested?.Invoke(); break;
            case Native.ClipboardMessage: ClipboardChanged?.Invoke(); break;
            case Native.DisplayMessage:
            case Native.DpiMessage:
            case Native.SettingsMessage: EnvironmentChanged?.Invoke(); break;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (hotkeyRegistered) Native.UnregisterHotKey(source.Handle, 1);
        if (clipboardRegistered) Native.RemoveClipboardFormatListener(source.Handle);
        if (foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(foregroundHook);
        source.RemoveHook(Message); source.Dispose();
    }
}
