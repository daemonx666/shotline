namespace Shotline;

public partial class App : Application
{
    private SingleInstance? instance;
    private Diagnostics? diagnostics;
    private AppController? controller;
    private TrayService? tray;
    private bool quitting;
    private int exitCode;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var smoke = e.Args.Contains("--ci-smoke", StringComparer.OrdinalIgnoreCase);
            if (!smoke)
            {
                instance = new SingleInstance();
                if (!instance.IsFirst) { instance.Dispose(); instance = null; Shutdown(); return; }
            }
            diagnostics = new Diagnostics(smoke ? Path.Combine(Path.GetTempPath(), "ShotlineSmoke", Guid.NewGuid().ToString("N")) : null);
            if (smoke) await new StateStore(diagnostics.DataDirectory).SaveAsync(new AppState
            { Config = new AppConfig { WatchEnabled = false, SoundEnabled = false } });
            Theme.Apply(new AppConfig());
            controller = new AppController(diagnostics);
            tray = new TrayService(controller, Quit);
            DispatcherUnhandledException += (_, args) =>
            {
                diagnostics.Fail("app.unhandled", "晾图遇到无法继续的错误，正在退出；请保留日志。", args.Exception);
                args.Handled = true; Quit();
            };
            await controller.InitializeAsync();
            instance?.Listen(() => { controller.Presenter.Peek(); });
            if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) controller.ShowSettings();
            if (smoke)
            {
                if (diagnostics.Hotkey.StartsWith("注册失败", StringComparison.Ordinal)) throw new InvalidOperationException("Smoke hotkey registration failed.");
                await Task.Delay(800); Quit();
            }
        }
        catch (Exception exception)
        {
            exitCode = 1;
            diagnostics?.Fail("app.start-failed", "晾图启动失败，请检查本地数据目录权限。", exception);
            MessageBox.Show("晾图无法启动：" + exception.Message, "Shotline · 晾图", MessageBoxButton.OK, MessageBoxImage.Error);
            Quit();
        }
    }
    private async void Quit()
    {
        if (quitting) return; quitting = true;
        try { if (controller is not null) await controller.DisposeAsync(); }
        catch (Exception exception) { exitCode = 1; diagnostics?.Fail("app.exit-failed", "退出时未能完整保存状态，请保留日志。", exception); }
        finally { tray?.Dispose(); tray = null; instance?.Dispose(); instance = null; Shutdown(exitCode); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); instance?.Dispose(); base.OnExit(e);
    }
}
