using System.Security.Cryptography;
using System.Buffers.Binary;

namespace Shotline;

internal sealed record DecodedImage(BitmapSource Image, BitmapSource Thumbnail, string Fingerprint, int Width, int Height);

internal static class ImageCodec
{
    internal const long MaxFileBytes = 64 * 1024 * 1024;
    internal const long MaxPixels = 40_000_000;
    internal static DecodedImage Read(string path)
    {
        if (!ImageFiles.IsSupported(path)) throw new InvalidDataException("仅支持 PNG、JPEG、BMP。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("图片文件超过 64 MiB。");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return Decode(decoder.Frames[0]);
    }
    internal static DecodedImage Decode(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException("图片尺寸无效或超过 4000 万像素。");
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(width * height * 4)];
        converted.CopyPixels(pixels, width * 4, 0);
        // Ignore invisible RGB values so different PNG encoders yield the same fingerprint.
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i + 3] == 0) pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> dimensions = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(dimensions, width);
        BinaryPrimitives.WriteInt32LittleEndian(dimensions[4..], height);
        hash.AppendData(dimensions);
        hash.AppendData(pixels);
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        image.Freeze();
        BitmapSource thumbnail = image;
        var factor = Math.Min(1, 480.0 / Math.Max(width, height));
        if (factor < 1)
        { thumbnail = new TransformedBitmap(image, new ScaleTransform(factor, factor)); thumbnail.Freeze(); }
        return new(image, thumbnail, fingerprint, width, height);
    }
    internal static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(file);
        file.Flush(true);
    }
}
