using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shotline;
using Shotline.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Theme.Apply(new AppConfig());
        var exit = 0;
        app.Dispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(args.Contains("--desktop")); }
            catch (Exception ex) { Console.Error.WriteLine("FAIL Windows: " + ex); exit = 1; }
            finally { app.Shutdown(exit); }
        });
        app.Run(); return exit;
    }
    private static async Task RunAsync(bool desktop)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ShotlineWindowsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var original = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 12, 40, 88, 255, 30, 50, 20, 255, 10, 50, 30, 255,
                80, 60, 30, 255, 90, 20, 50, 255, 90, 40, 80, 255 }, 12);
        original.Freeze();
        var png = System.IO.Path.Combine(root, "source.png"); ImageCodec.SavePng(original, png);
        var bmp = System.IO.Path.Combine(root, "source.bmp"); Save(original, bmp, new BmpBitmapEncoder());
        var jpg = System.IO.Path.Combine(root, "source.jpg"); Save(original, jpg, new JpegBitmapEncoder());
        Check(ImageCodec.Read(png).Fingerprint == ImageCodec.Read(bmp).Fingerprint, "PNG and BMP canonical pixel fingerprint");
        Check(ImageCodec.Read(jpg).Width == 3 && ImageCodec.Read(jpg).Height == 2, "JPEG decoder");
        Check(ImageCodec.Read(png).Thumbnail.IsFrozen, "worker-safe frozen thumbnail");
        var transparentA = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0 }, 4);
        var transparentB = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 50, 100, 80, 0 }, 4);
        Check(ImageCodec.Decode(transparentA).Fingerprint == ImageCodec.Decode(transparentB).Fingerprint, "transparent RGB normalization");
        var shortcut = DesktopBridge.ParseHotkey("Ctrl+Alt+T");
        Check(shortcut.Modifiers == 3 && shortcut.Key == 0x54, "default hotkey mapping");
        var functionKey = DesktopBridge.ParseHotkey("Ctrl+Shift+F10");
        Check(functionKey.Modifiers == 6 && functionKey.Key == 0x79, "custom hotkey mapping");
        await WatcherAsync(root, original);
        if (desktop) await DesktopAsync(png);
        else Console.WriteLine("Desktop smoke checks not run; pass --desktop on an interactive Windows desktop.");
        Console.WriteLine("PASS Windows codec, file watcher, and shortcut tests");
    }
    private static async Task WatcherAsync(string root, BitmapSource image)
    {
        var folder = System.IO.Path.Combine(root, "watch"); Directory.CreateDirectory(folder);
        var library = new ScreenshotLibrary(); var diagnostics = new Diagnostics();
        ImportService? service = null;
        service = new ImportService(diagnostics,
            (path, decoded) => Application.Current.Dispatcher.InvokeAsync(() =>
            {
                library.Import(new(Guid.NewGuid(), path, decoded.Fingerprint, ImageOrigin.Folder, false,
                    DateTimeOffset.UtcNow, decoded.Width, decoded.Height), DateTimeOffset.UtcNow);
                service!.Track(library.Items);
            }).Task,
            (oldPath, path) => Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var item = library.FindPath(oldPath);
                if (item is not null) { if (path is null) library.Remove(item.Id); else library.Rename(item.Id, path); }
                service!.Track(library.Items);
            }).Task);
        await using (service)
        {
            service.Configure(new AppConfig { WatchFolder = folder }, []);
            var file = System.IO.Path.Combine(folder, "slow.png");
            var encoded = new MemoryStream(); var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(encoded); var bytes = encoded.ToArray();
            await using (var writing = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                await writing.WriteAsync(bytes.AsMemory(0, bytes.Length / 2)); await writing.FlushAsync();
                await Task.Delay(300); Check(library.Items.Count == 0, "partial file not imported");
                await writing.WriteAsync(bytes.AsMemory(bytes.Length / 2));
            }
            await Until(() => library.Items.Count == 1, "completed file imported");
            var first = library.Items.Single();
            for (var i = 0; i < 8; i++) service.Enqueue(file);
            await Task.Delay(500); Check(library.Items.Count == 1, "duplicate events coalesced");
            var renamed = System.IO.Path.Combine(folder, "renamed.png"); File.Move(file, renamed);
            await Until(() => library.Items.Single().Path == renamed, "rename updated");
            Check(library.Items.Single().Id == first.Id, "rename retains identity");
            var edited = new byte[] { 20, 10, 240, 255 };
            var replacement = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, edited, 4); replacement.Freeze();
            using (var stream = new FileStream(renamed, FileMode.Create, FileAccess.Write, FileShare.None))
            { var save = new PngBitmapEncoder(); save.Frames.Add(BitmapFrame.Create(replacement)); save.Save(stream); }
            await Until(() => library.Items.Single().Fingerprint != first.Fingerprint, "editor save refreshed");
            Check(File.Exists(renamed), "source retained");
            library.Clear(); service.Track(library.Items); Check(File.Exists(renamed), "clear keeps external source");
        }
    }
    private static async Task DesktopAsync(string png)
    {
        var diagnostics = new Diagnostics(); var count = 0;
        var clipboard = new ClipboardService(diagnostics, _ => { count++; return Task.CompletedTask; });
        using var bridge = new DesktopBridge(diagnostics); bridge.ClipboardChanged += clipboard.Changed;
        clipboard.Configure(true);
        await clipboard.CopyAsync(new(Guid.NewGuid(), png, "test", ImageOrigin.Folder, false, DateTimeOffset.UtcNow, 3, 2));
        await Task.Delay(500); Check(count == 0 && System.Windows.Clipboard.ContainsImage(), "real self-copy suppressed");
        System.Windows.Clipboard.SetImage(ImageCodec.Read(png).Image);
        await Until(() => count == 1, "external clipboard notification imported"); clipboard.Stop();
        Check(bridge.Register("Ctrl+Alt+F12"), "global hotkey registered");
        using (var conflict = new DesktopBridge(diagnostics)) Check(!conflict.Register("Ctrl+Alt+F12"), "hotkey collision diagnosed");
        var before = GetForegroundWindow();
        var rope = new RopeWindow(); rope.Place(Native.AtPointer()); rope.Reveal(true);
        await Task.Delay(200);
        Check(GetForegroundWindow() == before, "passive window preserves foreground");
        var styles = GetWindowLongPtr(new WindowInteropHelper(rope).Handle, -20).ToInt64();
        Check((styles & 0x08000020) == 0x08000020, "noactivate and pass-through styles"); rope.Close();
        var recyclable = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(png)!, "recycle-test.png");
        File.Copy(png, recyclable); ShellOperations.Recycle(recyclable);
        Check(!File.Exists(recyclable) && File.Exists(png), "explicit recycle moves only selected test copy");
        Console.WriteLine("PASS desktop smoke checks; clipboard content changed by this explicit test.");
    }
    private static void Save(BitmapSource image, string path, BitmapEncoder encoder)
    { encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    private static async Task Until(Func<bool> predicate, string description)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(60);
        Check(predicate(), description);
    }
    private static void Check(bool value, string description)
    { if (!value) throw new InvalidOperationException(description); Console.WriteLine("PASS " + description); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
