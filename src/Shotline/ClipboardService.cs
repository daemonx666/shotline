namespace Shotline;

internal sealed class ClipboardService
{
    internal const string OwnerFormat = "Shotline.ClipboardOwner";
    private readonly string owner = Guid.NewGuid().ToString("N");
    private readonly ClipboardPolicy policy;
    private readonly Diagnostics diagnostics;
    private readonly Func<DecodedImage, Task> imported;
    private bool enabled, running, requested, stopped;
    internal ClipboardService(Diagnostics diagnostics, Func<DecodedImage, Task> imported)
    { this.diagnostics = diagnostics; this.imported = imported; policy = new(owner); }
    internal void Configure(bool value)
    {
        enabled = value;
        // Opt-in starts with the next change; existing clipboard contents are not captured.
        policy.Accept(Native.GetClipboardSequenceNumber(), owner);
        diagnostics.ClipboardStatus = value ? "监听中（也会接收普通复制图片）" : "已关闭";
    }
    internal async void Changed()
    {
        if (!enabled || stopped) return;
        requested = true;
        if (running) return;
        running = true;
        try
        {
            while (requested && enabled && !stopped)
            {
                requested = false;
                BitmapSource? image = null;
                uint sequence = 0;
                string? marker = null;
                await RetryAsync(() =>
                {
                    sequence = Native.GetClipboardSequenceNumber();
                    var data = Clipboard.GetDataObject();
                    marker = data?.GetData(OwnerFormat) as string;
                    if (marker == owner) return;
                    // Windows screenshot providers may supply PNG in addition to CF_BITMAP/DIB.
                    if (data?.GetData("PNG") is MemoryStream png)
                    {
                        using var stream = new MemoryStream(png.ToArray(), false);
                        image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat,
                            BitmapCacheOption.OnLoad).Frames[0];
                    }
                    else if (Clipboard.ContainsImage()) image = Clipboard.GetImage();
                    image?.Freeze();
                });
                if (!enabled || stopped || !policy.Accept(sequence, marker) || image is null) continue;
                var snapshot = image;
                var decoded = await Task.Run(() => ImageCodec.Decode(snapshot));
                if (enabled && !stopped) await imported(decoded);
            }
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or IOException or
            NotSupportedException or ArgumentException or FileFormatException)
        { diagnostics.Fail("clipboard.import", "剪贴板图片读取失败，可能被占用或格式无法解码；下一次变化会重试。", ex); }
        finally { running = false; }
    }
    internal async Task CopyAsync(Screenshot item)
    {
        var image = await Task.Run(() => ImageCodec.Read(item.Path));
        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, image.Image);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image.Image));
        var png = new MemoryStream(); encoder.Save(png); png.Position = 0;
        data.SetData("PNG", png);
        data.SetData(OwnerFormat, owner);
        data.SetData(DataFormats.FileDrop, new[] { item.Path });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u)));
        await RetryAsync(() => Clipboard.SetDataObject(data, true));
        policy.Accept(Native.GetClipboardSequenceNumber(), owner);
        diagnostics.Event("clipboard.copy", item.Id);
    }
    private static async Task RetryAsync(Action operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { operation(); return; }
            catch (ExternalException) when (attempt < 5) { await Task.Delay(70 + attempt * 50); }
        }
    }
    internal void Stop() { stopped = true; enabled = false; }
}
