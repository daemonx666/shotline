using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Shotline;

internal sealed class ImportService : IAsyncDisposable
{
    private readonly Diagnostics diagnostics;
    private readonly Func<string, DecodedImage, Task> imported;
    private readonly Func<string, string?, Task> changedPath;
    private readonly CancellationTokenSource stop = new();
    private readonly Channel<string> queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly ConcurrentDictionary<string, int> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> tracked = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime started = DateTime.UtcNow;
    private readonly Task worker;
    private readonly DispatcherTimer health;
    private string watchFolder = "";
    private bool enabled;

    internal ImportService(Diagnostics diagnostics, Func<string, DecodedImage, Task> imported,
        Func<string, string?, Task> changedPath)
    {
        this.diagnostics = diagnostics; this.imported = imported; this.changedPath = changedPath;
        worker = Task.Run(ProcessAsync);
        health = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        health.Tick += (_, _) => Rebuild();
        health.Start();
    }
    internal void Configure(AppConfig config, IEnumerable<Screenshot> items)
    {
        watchFolder = string.IsNullOrWhiteSpace(config.WatchFolder) ? "" : Path.GetFullPath(config.WatchFolder);
        enabled = config.WatchEnabled;
        tracked.Clear();
        foreach (var item in items) tracked[item.Path] = 0;
        Rebuild();
    }
    internal void Track(IEnumerable<Screenshot> items)
    {
        tracked.Clear();
        foreach (var item in items) tracked[item.Path] = 0;
        Rebuild();
    }
    internal void Enqueue(string path)
    {
        if (!ImageFiles.IsSupported(path) || stop.IsCancellationRequested) return;
        path = Path.GetFullPath(path);
        while (true)
        {
            if (pending.TryGetValue(path, out var version))
            { if (pending.TryUpdate(path, version + 1, version)) return; }
            else if (pending.TryAdd(path, 0)) break;
        }
        if (!queue.Writer.TryWrite(path))
        {
            pending.TryRemove(path, out _);
            diagnostics.Fail("import.queue", "导入队列已满，请稍后通过设置手动导入遗漏图片。",
                new IOException("queue capacity reached"));
        }
    }
    private void Rebuild()
    {
        var folders = tracked.Keys.Select(p => Path.GetDirectoryName(p)!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled && watchFolder.Length > 0) folders.Add(watchFolder);
        foreach (var folder in watchers.Keys.Where(f => !folders.Contains(f) || !Directory.Exists(f)).ToArray())
        { watchers[folder].Dispose(); watchers.Remove(folder); }
        foreach (var folder in folders.Where(f => !watchers.ContainsKey(f)))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder)
                { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false, InternalBufferSize = 16384 };
                watcher.Created += (_, e) => { if (IsMain(folder) || tracked.ContainsKey(e.FullPath)) Enqueue(e.FullPath); };
                watcher.Changed += (_, e) => { if (tracked.ContainsKey(e.FullPath) || pending.ContainsKey(e.FullPath)) Enqueue(e.FullPath); };
                watcher.Renamed += (_, e) =>
                {
                    _ = RenameAsync(e.OldFullPath, e.FullPath, IsMain(folder));
                };
                watcher.Deleted += (_, e) => { if (tracked.ContainsKey(e.FullPath)) _ = MissingAsync(e.FullPath); };
                watcher.Error += (_, e) => Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    diagnostics.Fail("watch.error", "文件监听异常，正在重建监听并补查近期图片。", e.GetException());
                    if (watchers.Remove(folder, out var failed)) failed.Dispose();
                    Rebuild();
                    _ = RecoverAsync(folder);
                });
                watchers.Add(folder, watcher);
                watcher.EnableRaisingEvents = true;
                if (IsMain(folder)) _ = RecoverAsync(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { diagnostics.Fail("watch.start", "无法监听目录，请检查目录是否存在及访问权限。", ex); }
        }
        diagnostics.Watch = !enabled ? "已暂停" : watchers.ContainsKey(watchFolder)
            ? "监听中：" + watchFolder : "未监听：目录不存在或无法访问；每 5 秒检查恢复";
    }
    private bool IsMain(string folder) => enabled && string.Equals(folder, watchFolder, StringComparison.OrdinalIgnoreCase);
    private async Task RecoverAsync(string folder)
    {
        try
        {
            await Task.Run(() =>
            {
                foreach (var path in Directory.EnumerateFiles(folder))
                    if (ImageFiles.IsSupported(path) && (tracked.ContainsKey(path) ||
                        IsMain(folder) && File.GetCreationTimeUtc(path) >= started)) Enqueue(path);
            }, stop.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { diagnostics.Fail("watch.recover", "监听恢复补查失败，可在设置中重新选择目录。", ex); }
    }
    private async Task RenameAsync(string oldPath, string path, bool main)
    {
        try
        {
            if (tracked.ContainsKey(oldPath)) await changedPath(oldPath, ImageFiles.IsSupported(path) ? path : null);
            if (main || tracked.ContainsKey(path)) Enqueue(path);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        { diagnostics.Fail("watch.rename", "图片重命名处理失败。", ex); }
    }
    private async Task MissingAsync(string path)
    {
        try
        {
            await Task.Delay(900, stop.Token); // editors may replace a file atomically
            if (!File.Exists(path)) await changedPath(path, null);
            else Enqueue(path);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var path in queue.Reader.ReadAllAsync(stop.Token))
            {
                pending.TryGetValue(path, out var revision);
                try
                {
                    DecodedImage? image = null;
                    Exception? failure = null;
                    for (var attempt = 0; attempt < 8; attempt++)
                    {
                        try
                        {
                            var info = new FileInfo(path);
                            var length = info.Length;
                            var writeTime = info.LastWriteTimeUtc;
                            await Task.Delay(160 + attempt * 100, stop.Token);
                            info.Refresh();
                            if (length == 0 || length != info.Length || writeTime != info.LastWriteTimeUtc)
                                throw new IOException("file still changing");
                            image = ImageCodec.Read(path); break;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                            NotSupportedException or FileFormatException or ArgumentException or InvalidDataException)
                        { failure = ex; }
                    }
                    if (image is not null) await imported(path, image);
                    else diagnostics.Fail("import.decode", "图片导入失败：写入未完成、文件占用、损坏或超过尺寸限制。可稍后手动重试。",
                        failure ?? new IOException("unknown decode failure"));
                }
                finally
                {
                    if (!((ICollection<KeyValuePair<string, int>>)pending).Remove(new(path, revision)) && !stop.IsCancellationRequested)
                    {
                        if (!queue.Writer.TryWrite(path))
                        {
                            pending.TryRemove(path, out _);
                            diagnostics.Fail("import.requeue", "图片变化补查队列已满，请手动导入重试。", new IOException("queue capacity reached"));
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        { diagnostics.Fail("import.worker", "导入处理已停止，请重启晾图并查看日志。", ex); }
    }
    public async ValueTask DisposeAsync()
    {
        health.Stop();
        foreach (var watcher in watchers.Values) watcher.Dispose();
        watchers.Clear(); queue.Writer.TryComplete(); stop.Cancel();
        await worker; stop.Dispose();
    }
}
