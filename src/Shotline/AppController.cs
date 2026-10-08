using Microsoft.Win32;

namespace Shotline;

internal sealed class AppController : IAsyncDisposable
{
    internal Diagnostics Diagnostics { get; }
    internal ScreenshotLibrary Library { get; } = new();
    internal LineViewModel Model { get; } = new();
    internal AppConfig Config { get; private set; } = new();
    internal ClipboardService ClipboardService { get; }
    internal DesktopBridge Bridge { get; }
    internal LinePresenter Presenter { get; }
    private readonly ImportService imports;
    private readonly StateStore store;
    private readonly DispatcherTimer saveTimer;
    private Task saveTask = Task.CompletedTask;
    private bool savingAllowed = true, exiting;
    private SettingsWindow? settings;
    internal event Action? ConfigChanged;

    internal AppController(Diagnostics diagnostics)
    {
        Diagnostics = diagnostics; store = new(diagnostics.DataDirectory);
        ClipboardService = new(diagnostics, ImportClipboardAsync);
        Bridge = new(diagnostics);
        imports = new(diagnostics,
            (path, image) => Application.Current.Dispatcher.InvokeAsync(() => ImportFile(path, image)).Task,
            (oldPath, path) => Application.Current.Dispatcher.InvokeAsync(() => PathChanged(oldPath, path)).Task);
        Presenter = new(this);
        Bridge.ToggleRequested += Presenter.Toggle;
        Bridge.ClipboardChanged += ClipboardService.Changed;
        Bridge.EnvironmentChanged += () => { Theme.Apply(Config); Presenter.RefreshEnvironment(); };
        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveNow(); };
    }
    internal async Task InitializeAsync()
    {
        AppState state;
        try
        {
            var loaded = await Task.Run(() => store.LoadAsync()); state = loaded.State;
            Diagnostics.Persistence = loaded.Recovered ? "从备份恢复，原文件保留" : "已加载";
            if (loaded.Warning is not null) Diagnostics.Fail("state.recover", loaded.Warning, new InvalidDataException(loaded.Warning));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            state = new(); savingAllowed = false; Diagnostics.Persistence = "恢复失败；写入已禁用，原文件保留";
            Diagnostics.Fail("state.load", "状态无法恢复，写入已禁用。请打开数据目录保留并修复 state.json / state.json.bak 后重启。", ex);
        }
        Config = state.Config;
        if (Config.WatchFolder.Length == 0) Config = Config with { WatchFolder = DefaultFolder() };
        Config = Config with { LaunchAtLogin = IsLaunchAtLogin() };
        Theme.Apply(Config);
        var initialMonitor = Native.AtPointer();
        Library.SetCapacity((int)((initialMonitor.Work.Width / initialMonitor.Scale - 160) / Theme.CardSpacing));
        Library.Restore(state.Items, File.Exists);
        Model.Sync(Library.Items);
        Presenter.Initialize();
        imports.Configure(Config, Library.Items);
        Bridge.Register(Config.HotKey);
        ClipboardService.Configure(Config.ClipboardEnabled);
        foreach (var item in Library.Items.ToArray()) imports.Enqueue(item.Path);
        ScheduleSave();
        Diagnostics.Event("app.start", reason: "Windows-" + Environment.OSVersion.Version);
    }
    private static string DefaultFolder()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
        if (key?.GetValue("{B7BEDE81-DF94-4682-A7D8-57A52620B86F}") is string folder)
            return Environment.ExpandEnvironmentVariables(folder);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
    internal async Task ApplyConfigAsync(AppConfig config)
    {
        DesktopBridge.ParseHotkey(config.HotKey);
        if (string.IsNullOrWhiteSpace(config.WatchFolder) || !Path.IsPathFullyQualified(config.WatchFolder))
            throw new ArgumentException("监听目录必须是完整路径。");
        if (string.IsNullOrWhiteSpace(config.EditorPath)) throw new ArgumentException("编辑器不能为空。");
        if (Config.LaunchAtLogin != config.LaunchAtLogin) SetLaunchAtLogin(config.LaunchAtLogin);
        var old = Config; Config = config;
        Theme.Apply(config);
        if (old.HotKey != config.HotKey) Bridge.Register(config.HotKey);
        if (old.ClipboardEnabled != config.ClipboardEnabled) ClipboardService.Configure(config.ClipboardEnabled);
        imports.Configure(config, Library.Items);
        Presenter.RefreshEnvironment(); ConfigChanged?.Invoke();
        ScheduleSave();
        SaveNow(); await saveTask;
    }
    private void ImportFile(string path, DecodedImage image)
    {
        if (exiting) return;
        var previous = Library.FindPath(path);
        var managed = previous?.Managed ?? false;
        var item = new Screenshot(Guid.NewGuid(), path, image.Fingerprint,
            previous?.Origin ?? ImageOrigin.Folder, managed, DateTimeOffset.UtcNow, image.Width, image.Height);
        var result = Library.Import(item, DateTimeOffset.UtcNow);
        if (result.Item is not null) Model.Sync(Library.Items, result.Item.Id, image.Thumbnail);
        if (result.Changed) Updated();
        if (result.Added) { Diagnostics.Event("import.file", result.Item!.Id); Presenter.Peek(); Sound(); }
    }
    private async Task ImportClipboardAsync(DecodedImage image)
    {
        if (exiting || Library.WouldDuplicate(image.Fingerprint, DateTimeOffset.UtcNow)) return;
        var id = Guid.NewGuid();
        var path = Path.Combine(Diagnostics.ImportsDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + id.ToString("N") + ".png");
        try
        {
            await Task.Run(() => ImageCodec.SavePng(image.Image, path));
            if (exiting) return;
            var result = Library.Import(new(id, path, image.Fingerprint, ImageOrigin.Clipboard, true,
                DateTimeOffset.UtcNow, image.Width, image.Height), DateTimeOffset.UtcNow);
            if (result.Changed) { Model.Sync(Library.Items, result.Item!.Id, image.Thumbnail); Updated(); }
            if (result.Added) { Diagnostics.Event("import.clipboard", id); Presenter.Peek(); Sound(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Diagnostics.Fail("import.save", "剪贴板图片无法保存到数据目录。", ex, id); }
    }
    private void PathChanged(string oldPath, string? path)
    {
        if (exiting) return;
        var item = Library.FindPath(oldPath);
        if (item is null) return;
        if (path is null) Library.Remove(item.Id);
        else
        {
            Library.Rename(item.Id, path);
            imports.Enqueue(path);
        }
        Model.Sync(Library.Items); Updated();
    }
    private void Updated() { imports.Track(Library.Items); Presenter.Sync(); ScheduleSave(); }
    internal void SetCapacity(int capacity)
    {
        if (Library.Capacity == Math.Clamp(capacity, 3, 12)) return;
        var evicted = Library.SetCapacity(capacity);
        if (evicted.Count > 0) { Model.Sync(Library.Items); imports.Track(Library.Items); ScheduleSave(); }
    }
    internal void AddFiles(IEnumerable<string> paths) { foreach (var path in paths) imports.Enqueue(path); }
    internal async Task<bool> CopyAsync(CardViewModel card)
    {
        try { await ClipboardService.CopyAsync(card.Item); Sound(); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or
            InvalidOperationException or NotSupportedException or FileFormatException or ArgumentException)
        { Diagnostics.Fail("copy.failed", "复制失败：文件不可读取或剪贴板被占用，请重试。", ex, card.Id); return false; }
    }
    internal void Open(CardViewModel card) => Run("file.open", card.Id, () => Shell(card.Item.Path));
    internal void Edit(CardViewModel card) => Run("file.edit", card.Id, () =>
    {
        var info = new ProcessStartInfo(Config.EditorPath) { UseShellExecute = true };
        info.ArgumentList.Add(card.Item.Path); Process.Start(info);
        Diagnostics.Event("file.edit", card.Id);
    });
    internal void Reveal(CardViewModel card) => Run("file.reveal", card.Id, () =>
    {
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true,
            Arguments = "/select,\"" + card.Item.Path + "\"" }; Process.Start(info);
    });
    internal async Task SaveAsAsync(CardViewModel card)
    {
        var dialog = new SaveFileDialog { Title = "另存为（保留原文件）", FileName = card.FileName,
            Filter = "原始图片|*" + Path.GetExtension(card.Item.Path), AddExtension = true,
            DefaultExt = Path.GetExtension(card.Item.Path), OverwritePrompt = true };
        Presenter.Busy = true;
        try
        {
            if (dialog.ShowDialog() != true || ImageFiles.SamePath(dialog.FileName, card.Item.Path)) return;
            await Task.Run(() => File.Copy(card.Item.Path, dialog.FileName, true));
            Diagnostics.Event("file.save-as", card.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Diagnostics.Fail("file.save-as", "另存为失败，原文件保留。", ex, card.Id); }
        finally { Presenter.Busy = false; }
    }
    internal void Remove(CardViewModel card)
    {
        if (Library.Remove(card.Id)) { Model.Sync(Library.Items); Updated(); Diagnostics.Event("record.remove", card.Id); }
    }
    internal void Clear()
    { Library.Clear(); Model.Sync(Library.Items); Updated(); Diagnostics.Event("record.clear"); }
    internal async Task RecycleAsync(CardViewModel card)
    {
        Presenter.Busy = true;
        try
        {
            if (MessageBox.Show($"将“{card.FileName}”移到回收站？\n{(card.Item.Managed ? "这是晾图管理的副本。" : "这会移动外部源文件。")}",
                "移到回收站", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            // Shell COM operation runs on the UI STA; it moves only this explicitly selected file.
            ShellOperations.Recycle(card.Item.Path);
            await Task.Delay(100);
            if (File.Exists(card.Item.Path)) throw new IOException("source still exists after recycle");
            Remove(card); Diagnostics.Event("file.recycle", card.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or NotSupportedException)
        { Diagnostics.Fail("file.recycle", "移到回收站失败，卡片保留。", ex, card.Id); }
        finally { Presenter.Busy = false; }
    }
    internal async Task CompleteDragAsync(CardViewModel card, DropOutcome outcome)
    {
        if (outcome == DropOutcome.Move)
        {
            for (var attempt = 0; attempt < 5 && File.Exists(card.Item.Path); attempt++) await Task.Delay(200);
            if (ImageFiles.RemoveAfterDrop(outcome, File.Exists(card.Item.Path))) Remove(card);
        }
        Diagnostics.Event("drag.complete", card.Id, outcome.ToString());
    }
    internal void ShowSettings()
    {
        if (settings is null)
        { settings = new(this); settings.Closed += (_, _) => settings = null; settings.Show(); }
        if (settings.WindowState == WindowState.Minimized) settings.WindowState = WindowState.Normal;
        settings.Activate();
    }
    internal static void Shell(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    internal void OpenDirectory(string path) => Run("directory.open", null, () => Shell(path));
    private void Run(string name, Guid? id, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { Diagnostics.Fail(name, "文件操作失败，请检查文件、默认应用或编辑器设置。", ex, id); }
    }
    private void Sound() { if (Config.SoundEnabled) System.Media.SystemSounds.Asterisk.Play(); }
    private static bool IsLaunchAtLogin()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Shotline") as string == "\"" + Environment.ProcessPath + "\"";
    }
    private static void SetLaunchAtLogin(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("Shotline", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("Shotline", false);
    }
    private void ScheduleSave() { if (!exiting) { saveTimer.Stop(); saveTimer.Start(); } }
    private void SaveNow()
    {
        saveTimer.Stop(); if (!savingAllowed) return;
        var state = new AppState { Config = Config, Items = Library.Items.ToList() };
        saveTask = SaveAfterAsync(saveTask, state);
    }
    private async Task SaveAfterAsync(Task previous, AppState state)
    {
        await previous;
        try { await Task.Run(() => store.SaveAsync(state)); Diagnostics.Persistence = "已保存：" + DateTime.Now.ToString("HH:mm:ss"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Diagnostics.Persistence = "保存失败；上次状态保留"; Diagnostics.Fail("state.save", "配置或列表保存失败，请检查数据目录权限及剩余空间。", ex); }
    }
    public async ValueTask DisposeAsync()
    {
        exiting = true; saveTimer.Stop(); ClipboardService.Stop(); Bridge.Dispose();
        Presenter.Dispose(); settings?.Close(); await imports.DisposeAsync();
        SaveNow(); await saveTask; Diagnostics.Event("app.exit");
    }
}
