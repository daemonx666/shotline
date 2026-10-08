using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shotline.Core;

public sealed record StateLoad(AppState State, string? Warning, bool Recovered);

public sealed class StateStore(string directory)
{
    private static readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool preservePrimary;
    private bool preserveTemporary;
    public string StatePath => Path.Combine(directory, "state.json");
    public string BackupPath => StatePath + ".bak";

    public async Task<StateLoad> LoadAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(StatePath) && !File.Exists(BackupPath) && !File.Exists(StatePath + ".tmp")) return new(new(), null, false);
        Exception? primaryError = null;
        if (File.Exists(StatePath))
        {
            try { return new(await ReadAsync(StatePath, cancellationToken), null, false); }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            { primaryError = ex; preservePrimary = true; }
        }
        if (File.Exists(BackupPath))
        {
            try
            {
                var recovered = await ReadAsync(BackupPath, cancellationToken);
                preservePrimary = true;
                return new(recovered, "状态主文件不可用，已从备份恢复；原文件将保留。", true);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            { primaryError ??= ex; }
        }
        if (File.Exists(StatePath + ".tmp"))
        {
            try
            {
                var recovered = await ReadAsync(StatePath + ".tmp", cancellationToken);
                preserveTemporary = true;
                return new(recovered, "状态主文件及备份不可用，已从中断写入的临时文件恢复；临时文件将保留。", true);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            { primaryError ??= ex; }
        }
        throw new InvalidDataException("状态、备份及临时文件均无法恢复。应用不会覆盖原文件。", primaryError);
    }

    private static async Task<AppState> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<AppState>(file, options, cancellationToken);
        if (state is null || state.Version != 1 || state.Config is null || state.Items is null ||
            !Enum.IsDefined(state.Config.Theme) || string.IsNullOrWhiteSpace(state.Config.HotKey) ||
            state.Config.WatchFolder is null || string.IsNullOrWhiteSpace(state.Config.EditorPath) ||
            state.Config.WatchFolder.Length > 0 && !Path.IsPathFullyQualified(state.Config.WatchFolder) ||
            state.Items.Any(i => i is null || i.Id == Guid.Empty || string.IsNullOrWhiteSpace(i.Path) ||
                !Path.IsPathFullyQualified(i.Path) || string.IsNullOrWhiteSpace(i.Fingerprint) ||
                i.PixelWidth <= 0 || i.PixelHeight <= 0 || !Enum.IsDefined(i.Origin)))
            throw new InvalidDataException("不支持或损坏的状态格式。");
        return state;
    }

    public async Task SaveAsync(AppState state, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(directory);
            var temp = StatePath + ".tmp";
            if (preserveTemporary && File.Exists(temp))
            {
                File.Move(temp, temp + ".recovered-" + Guid.NewGuid().ToString("N"));
                preserveTemporary = false;
            }
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, state, options, cancellationToken);
                await file.FlushAsync(cancellationToken);
                file.Flush(true);
            }
            if (preservePrimary && File.Exists(StatePath))
            {
                File.Move(StatePath, StatePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
                preservePrimary = false;
            }
            if (File.Exists(StatePath)) File.Replace(temp, StatePath, BackupPath, true);
            else File.Move(temp, StatePath);
        }
        finally { gate.Release(); }
    }
}
