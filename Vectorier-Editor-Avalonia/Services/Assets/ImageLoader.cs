using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Vectorier_Editor_Avalonia.Services.IO;

namespace Vectorier_Editor_Avalonia.Services.Assets;

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
