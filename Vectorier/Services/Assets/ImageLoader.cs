using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Vectorier.Services.IO;

namespace Vectorier.Services.Assets;

public static class ImageLoader
{
    /// <summary>Loads an image file. When <paramref name="decodeWidth"/> is set the bitmap is downscaled while decoding (thumbnails).</summary>
    public static async Task<Bitmap?> LoadAsync(IFileSystem fs, string path, int? decodeWidth = null)
    {
        try
        {
            if (!await fs.FileExistsAsync(path))
                return null;
            byte[] data = await fs.ReadAllBytesAsync(path);
            return await Task.Run(() => Decode(data, decodeWidth));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ImageLoader] {path}: {ex.Message}");
            return null;
        }
    }

    public static async Task<Bitmap?> LoadAsync(IStorageFile file, int? decodeWidth = null)
    {
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            byte[] data = memory.ToArray();
            return await Task.Run(() => Decode(data, decodeWidth));
        }
        catch
        {
            return null;
        }
    }

    public static Bitmap? Decode(byte[] data, int? decodeWidth = null)
    {
        try
        {
            using var stream = new MemoryStream(data);
            return decodeWidth is int width
                ? Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.MediumQuality)
                : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Reads an image's pixel size from its header (PNG, JPEG) without decoding it.</summary>
public static class ImageHeader
{
    public static bool TryReadSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = height = 0;
        // PNG: signature, then the IHDR chunk with width and height.
        if (data.Length >= 24 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        {
            width = (data[16] << 24) | (data[17] << 16) | (data[18] << 8) | data[19];
            height = (data[20] << 24) | (data[21] << 16) | (data[22] << 8) | data[23];
            return width > 0 && height > 0;
        }
        // JPEG: walk the segments to the first start-of-frame marker.
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                byte marker = data[i + 1];
                if (marker == 0xFF) { i++; continue; }
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                int length = (data[i + 2] << 8) | data[i + 3];
                bool sof = marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC;
                if (sof)
                {
                    height = (data[i + 5] << 8) | data[i + 6];
                    width = (data[i + 7] << 8) | data[i + 8];
                    return width > 0 && height > 0;
                }
                if (length < 2) return false;
                i += 2 + length;
            }
        }
        return false;
    }
}
