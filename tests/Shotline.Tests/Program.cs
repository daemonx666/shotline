using Shotline.Core;

var Now = Current();
var tests = new (string Name, Func<Task> Run)[]
{
    ("path duplicate and edited contents", () => Sync(() =>
    {
        var library = new ScreenshotLibrary(); var image = Item("one.png", "a");
        var first = library.Import(image, Now);
        Check(first.Added, "first image accepted");
        var second = library.Import(image with { Id = Guid.NewGuid() }, Now);
        Check(!second.Added && !second.Changed && library.Items.Count == 1, "repeat event ignored");
        var edited = library.Import(image with { Id = Guid.NewGuid(), Fingerprint = "edited" }, Now);
        Check(edited.Changed && edited.Item?.Id == image.Id, "edit updates existing identity");
    })),
    ("folder clipboard merge in either order", () => Sync(() =>
    {
        var library = new ScreenshotLibrary();
        var clipboard = Item("managed.png", "same") with { Managed = true, Origin = ImageOrigin.Clipboard };
        library.Import(clipboard, Now);
        var external = Item("external.png", "same");
        var result = library.Import(external, Now.AddSeconds(12));
        Check(!result.Added && result.Changed && library.Items.Single().Id == clipboard.Id &&
            !library.Items.Single().Managed && library.Items.Single().Path == external.Path, "prefer external source");
        var reverse = new ScreenshotLibrary(); reverse.Import(external, Now);
        Check(!reverse.Import(clipboard, Now.AddSeconds(12)).Added && reverse.Items.Count == 1, "reverse order deduplicated");
        Check(reverse.Import(Item("later.png", "same") with { AddedAt = Now.AddSeconds(40) }, Now.AddSeconds(40)).Added,
            "same image can be deliberately imported later");
    })),
    ("recently removed duplicate is suppressed", () => Sync(() =>
    {
        var library = new ScreenshotLibrary(); var image = Item("removed.png", "a"); library.Import(image, Now);
        library.Remove(image.Id);
        Check(!library.Import(Item("late.png", "a"), Now.AddSeconds(2)).Added, "delayed second source ignored");
    })),
    ("capacity removes records and preserves every file", () => Sync(() =>
    {
        var folder = Temp(); var library = new ScreenshotLibrary(); library.SetCapacity(3);
        var files = Enumerable.Range(0, 5).Select(i => Path.Combine(folder, $"{i}.png")).ToArray();
        foreach (var file in files) { File.WriteAllText(file, "source"); library.Import(Item(file, file), Now); }
        Check(library.Items.Count == 3 && library.Items[0].Path == files[2], "oldest records evicted");
        library.Clear(); Check(files.All(File.Exists), "clear and eviction retain all source files");
        library.SetCapacity(100); Check(library.Capacity == 12, "upper capacity clamp");
        library.SetCapacity(0); Check(library.Capacity == 3, "lower capacity clamp");
    })),
    ("rename preserves identity and avoids duplicate destination", () => Sync(() =>
    {
        var library = new ScreenshotLibrary(); var image = Item("old.png", "a"); library.Import(image, Now);
        library.Rename(image.Id, Path.GetFullPath("new.png"));
        Check(library.Items.Single().Id == image.Id && library.Items.Single().Path.EndsWith("new.png"), "rename preserves id");
        var another = Item("second.png", "b"); library.Import(another, Now);
        library.Rename(another.Id, library.Items[0].Path); Check(library.Items.Count == 1, "no duplicate path after rename");
    })),
    ("restore and missing paths", () => Sync(() =>
    {
        var library = new ScreenshotLibrary(); var image = Item("exists.png", "a");
        library.Restore([image, image, Item("missing.png", "b"), Item("bad.gif", "c")], p => !p.EndsWith("missing.png"));
        Check(library.Items.Count == 1 && library.Items[0].Id == image.Id, "restore valid unique supported files");
        Check(library.Prune(_ => false).Count == 1 && library.Items.Count == 0, "missing file pruned");
    })),
    ("wide monitor restoration preserves twelve records", () => Sync(() =>
    {
        var library = new ScreenshotLibrary(); library.SetCapacity(12);
        var saved = Enumerable.Range(0, 12).Select(i => Item($"wide-{i}.png", $"hash-{i}")).ToArray();
        library.Restore(saved, _ => true);
        Check(library.Items.Count == 12 && library.Items[0].Id == saved[0].Id, "restoration uses actual capacity");
    })),
    ("clipboard self copy and duplicate sequence", () => Sync(() =>
    {
        var policy = new ClipboardPolicy("this-app");
        Check(!policy.Accept(1, "this-app"), "self copy suppressed");
        Check(policy.Accept(2, null), "external image accepted");
        Check(!policy.Accept(2, null), "same sequence suppressed");
        Check(policy.Accept(3, "other-app"), "other owner accepted");
        Check(policy.Accept(0, null), "sequence rollover accepted");
    })),
    ("drag result must confirm move", () => Sync(() =>
    {
        foreach (var result in new[] { DropOutcome.Copy, DropOutcome.Cancelled, DropOutcome.Failed })
            Check(!ImageFiles.RemoveAfterDrop(result, false), "copy or failed result keeps card");
        Check(!ImageFiles.RemoveAfterDrop(DropOutcome.Move, true), "reported move without source change keeps card");
        Check(ImageFiles.RemoveAfterDrop(DropOutcome.Move, false), "confirmed move removes card");
    })),
    ("state and config round trip", async () =>
    {
        var store = new StateStore(Temp()); var image = Item("saved.png", "state");
        var config = new AppConfig { ClipboardEnabled = true, WatchEnabled = false, WatchFolder = Path.GetFullPath("shots"),
            SoundEnabled = false, LaunchAtLogin = true, ReduceMotion = true, Theme = ColorTheme.Dark,
            HotKey = "Ctrl+Shift+F10", EditorPath = "editor.exe" };
        await store.SaveAsync(new() { Config = config, Items = [image] });
        var loaded = await store.LoadAsync();
        Check(loaded.State.Config == config && loaded.State.Items.Single() == image, "all config and record fields round trip");
    }),
    ("corruption recovery keeps sole backup", async () =>
    {
        var directory = Temp(); var store = new StateStore(directory);
        var first = new AppState { Config = new() { SoundEnabled = false } };
        await store.SaveAsync(first); await store.SaveAsync(new());
        File.WriteAllText(store.StatePath, "broken primary");
        var recovery = new StateStore(directory); var loaded = await recovery.LoadAsync();
        Check(loaded.Recovered && !loaded.State.Config.SoundEnabled, "backup restored");
        await recovery.SaveAsync(loaded.State);
        Check(Directory.EnumerateFiles(directory, "state.json.corrupt-*").Count() == 1, "damaged primary preserved");
        Check(File.ReadAllText(recovery.BackupPath).Contains("false"), "valid backup not overwritten with corruption");
    }),
    ("unrecoverable and future state refuse restore", async () =>
    {
        var store = new StateStore(Temp()); File.WriteAllText(store.StatePath, "broken");
        await Throws<InvalidDataException>(() => store.LoadAsync());
        Check(File.ReadAllText(store.StatePath) == "broken", "unreadable data not overwritten");
        var future = new StateStore(Temp()); File.WriteAllText(future.StatePath, "{\"Version\":99}");
        await Throws<InvalidDataException>(() => future.LoadAsync());
    }),
    ("malformed config recovers instead of crashing", async () =>
    {
        var store = new StateStore(Temp()); await store.SaveAsync(new()); await store.SaveAsync(new());
        var invalid = File.ReadAllText(store.StatePath).Replace("\"WatchFolder\": \"\"", "\"WatchFolder\": null");
        File.WriteAllText(store.StatePath, invalid);
        Check((await new StateStore(Path.GetDirectoryName(store.StatePath)!).LoadAsync()).Recovered, "null folder recovered from backup");
    }),
    ("interrupted first save retains sole recovery copy", async () =>
    {
        var folder = Temp(); var store = new StateStore(folder);
        await store.SaveAsync(new AppState { Config = new() { SoundEnabled = false } });
        File.Move(store.StatePath, store.StatePath + ".tmp");
        var recovered = new StateStore(folder); var loaded = await recovered.LoadAsync();
        Check(loaded.Recovered && !loaded.State.Config.SoundEnabled, "interrupted first save recovered");
        await recovered.SaveAsync(loaded.State);
        Check(Directory.EnumerateFiles(folder, "state.json.tmp.recovered-*").Count() == 1, "sole valid temporary file preserved");
    }),
};
var failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
}
Console.WriteLine($"Core: {tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static DateTimeOffset Current() => new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
static Screenshot Item(string path, string hash) => new(Guid.NewGuid(), Path.GetFullPath(path), hash, ImageOrigin.Folder, false, Current(), 100, 80);
static string Temp() { var path = Path.Combine(Path.GetTempPath(), "ShotlineTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
static Task Sync(Action action) { action(); return Task.CompletedTask; }
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{ try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
