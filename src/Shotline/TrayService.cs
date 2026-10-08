namespace Shotline;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ContextMenuStrip menu = new();
    private readonly AppController controller;
    private readonly Action exit;
    private readonly System.Drawing.Icon icon;
    internal TrayService(AppController controller, Action exit)
    {
        this.controller = controller; this.exit = exit;
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Shotline.ico")).Stream;
        icon = new System.Drawing.Icon(resource);
        tray = new Forms.NotifyIcon { Icon = icon, Text = "Shotline · 晾图", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => controller.Presenter.Toggle();
        menu.Opening += (_, _) => Rebuild();
        controller.Diagnostics.ErrorRaised += ShowError;
        Rebuild();
    }
    private void Rebuild()
    {
        // Dispose menu items without touching files or application records.
        foreach (Forms.ToolStripItem item in menu.Items.Cast<Forms.ToolStripItem>().ToArray()) item.Dispose();
        menu.Items.Clear();
        Add(controller.Presenter.IsRevealed ? "收起晾图" : "显示晾图", controller.Presenter.Toggle);
        Add("截图文件夹监听", () => Change(controller.Config with { WatchEnabled = !controller.Config.WatchEnabled }), controller.Config.WatchEnabled);
        Add("剪贴板图片导入", () => Change(controller.Config with { ClipboardEnabled = !controller.Config.ClipboardEnabled }), controller.Config.ClipboardEnabled);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("设置与运行状态…", controller.ShowSettings);
        Add("打开监听目录", () => controller.OpenDirectory(controller.Config.WatchFolder));
        Add("清空晾图（保留文件）", controller.Clear);
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add("退出晾图", exit);
    }
    private void Add(string text, Action action, bool? check = null)
    {
        var item = new Forms.ToolStripMenuItem(text);
        if (check is not null) item.Checked = check.Value;
        item.Click += (_, _) => Application.Current.Dispatcher.InvokeAsync(action);
        menu.Items.Add(item);
    }
    private async void Change(AppConfig config)
    {
        try { await controller.ApplyConfigAsync(config); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { controller.Diagnostics.Fail("config.change", "设置保存失败，请通过设置窗口检查。", ex); }
    }
    private void ShowError(string message)
    { tray.ShowBalloonTip(5000, "晾图 · 操作未完成", message, Forms.ToolTipIcon.Warning); controller.Presenter.Notify(message); }
    public void Dispose()
    { controller.Diagnostics.ErrorRaised -= ShowError; tray.Visible = false; tray.Dispose(); menu.Dispose(); icon.Dispose(); }
}
