namespace Shotline.Core;

public enum ImageOrigin { Folder, Clipboard, Manual }
public enum ColorTheme { System, Light, Dark }
public enum DropOutcome { Cancelled, Copy, Move, Failed }

public sealed record AppConfig
{
    public string WatchFolder { get; init; } = "";
    public bool WatchEnabled { get; init; } = true;
    public bool ClipboardEnabled { get; init; }
    public bool SoundEnabled { get; init; } = true;
    public bool LaunchAtLogin { get; init; }
    public bool ReduceMotion { get; init; }
    public ColorTheme Theme { get; init; }
    public string EditorPath { get; init; } = "mspaint.exe";
    public string HotKey { get; init; } = "Ctrl+Alt+T";
}

public sealed record Screenshot(Guid Id, string Path, string Fingerprint, ImageOrigin Origin,
    bool Managed, DateTimeOffset AddedAt, int PixelWidth, int PixelHeight);

public sealed record AppState
{
    public int Version { get; init; } = 1;
    public AppConfig Config { get; init; } = new();
    public List<Screenshot> Items { get; init; } = [];
}

public sealed record ImportResult(Screenshot? Item, bool Added, bool Changed, IReadOnlyList<Screenshot> Evicted);

public static class ImageFiles
{
    public static bool IsSupported(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant()
        is ".png" or ".jpg" or ".jpeg" or ".bmp";

    public static bool SamePath(string a, string b) => string.Equals(
        System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public static bool RemoveAfterDrop(DropOutcome outcome, bool sourceExists) =>
        outcome == DropOutcome.Move && !sourceExists;
}

public sealed class ClipboardPolicy(string owner)
{
    private uint lastSequence;
    private bool observed;
    public bool Accept(uint sequence, string? marker)
    {
        if (observed && sequence == lastSequence) return false;
        observed = true;
        lastSequence = sequence;
        return marker != owner;
    }
}
