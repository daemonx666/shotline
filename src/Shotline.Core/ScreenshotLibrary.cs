namespace Shotline.Core;

/// <summary>All calls run on the owning UI thread; I/O and decoding live outside this class.</summary>
public sealed class ScreenshotLibrary
{
    private readonly List<Screenshot> items = [];
    private readonly Dictionary<string, DateTimeOffset> recent = [];
    public IReadOnlyList<Screenshot> Items => items;
    public int Capacity { get; private set; } = 8;
    public static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(30);

    public IReadOnlyList<Screenshot> SetCapacity(int capacity)
    {
        Capacity = Math.Clamp(capacity, 3, 12);
        return Trim();
    }

    public ImportResult Import(Screenshot candidate, DateTimeOffset now)
    {
        foreach (var key in recent.Where(p => now - p.Value > DuplicateWindow).Select(p => p.Key).ToArray())
            recent.Remove(key);
        var pathIndex = items.FindIndex(i => ImageFiles.SamePath(i.Path, candidate.Path));
        if (pathIndex >= 0)
        {
            var previous = items[pathIndex];
            var changed = previous.Fingerprint != candidate.Fingerprint;
            items[pathIndex] = candidate with { Id = previous.Id, AddedAt = previous.AddedAt,
                Managed = previous.Managed, Origin = previous.Origin };
            recent[candidate.Fingerprint] = now;
            return new(items[pathIndex], false, changed, []);
        }
        var duplicate = items.FindIndex(i => i.Fingerprint == candidate.Fingerprint &&
            now - i.AddedAt <= DuplicateWindow);
        if (duplicate >= 0)
        {
            var previous = items[duplicate];
            // A delayed on-disk screenshot becomes the preferred source. The old managed
            // file is retained; record merging must never delete either user's file.
            if (previous.Managed && !candidate.Managed)
            {
                items[duplicate] = candidate with { Id = previous.Id, AddedAt = previous.AddedAt };
                recent[candidate.Fingerprint] = now;
                return new(items[duplicate], false, true, []);
            }
            return new(previous, false, false, []);
        }
        if (recent.ContainsKey(candidate.Fingerprint)) return new(null, false, false, []);
        recent[candidate.Fingerprint] = now;
        items.Add(candidate);
        return new(candidate, true, true, Trim());
    }

    public bool WouldDuplicate(string fingerprint, DateTimeOffset now) =>
        recent.TryGetValue(fingerprint, out var date) && now - date <= DuplicateWindow ||
        items.Any(i => i.Fingerprint == fingerprint && now - i.AddedAt <= DuplicateWindow);

    public Screenshot? Find(Guid id) => items.FirstOrDefault(i => i.Id == id);
    public Screenshot? FindPath(string path) => items.FirstOrDefault(i => ImageFiles.SamePath(i.Path, path));
    public bool Rename(Guid id, string path)
    {
        var index = items.FindIndex(i => i.Id == id);
        if (index < 0) return false;
        var duplicate = items.FindIndex(i => i.Id != id && ImageFiles.SamePath(i.Path, path));
        if (duplicate >= 0) { items.RemoveAt(index); return true; }
        items[index] = items[index] with { Path = path };
        return true;
    }
    public bool Remove(Guid id) => items.RemoveAll(i => i.Id == id) > 0;
    public void Clear() => items.Clear();

    public IReadOnlyList<Screenshot> Prune(Func<string, bool> exists)
    {
        var missing = items.Where(i => !exists(i.Path)).ToArray();
        foreach (var item in missing) Remove(item.Id);
        return missing;
    }

    public void Restore(IEnumerable<Screenshot> saved, Func<string, bool> exists)
    {
        items.Clear();
        recent.Clear();
        foreach (var item in saved.OrderBy(i => i.AddedAt))
            if (exists(item.Path) && ImageFiles.IsSupported(item.Path) &&
                !items.Any(i => i.Id == item.Id || ImageFiles.SamePath(i.Path, item.Path))) items.Add(item);
        Trim();
    }

    private IReadOnlyList<Screenshot> Trim()
    {
        var evicted = new List<Screenshot>();
        while (items.Count > Capacity)
        {
            evicted.Add(items[0]);
            items.RemoveAt(0);
        }
        return evicted;
    }
}
