using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;

namespace Vectorier.Services.Projects;

/// <summary>A level's thumbnail: icons/stories/&lt;level&gt;.png (or .jpg), as used by the Project Manager.</summary>
public sealed class LevelThumbnail
{
    public const int Width = 512;
    public const int Height = 340;
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

    private readonly ProjectInfo _project;
    private readonly string _level;

    public LevelThumbnail(ProjectInfo project, string level)
    {
        _project = project;
        _level = level;
    }

    private string Folder => VPath.Combine(_project.Path, "icons", "stories");

    public async Task<string?> FindAsync()
    {
        foreach (var ext in Extensions)
        {
            string path = VPath.Combine(Folder, _level + ext);
            if (await AppServices.FileSystem.FileExistsAsync(path))
                return path;
        }
        return null;
    }

    public async Task<Bitmap?> LoadAsync(int decodeWidth)
    {
        string? path = await FindAsync();
        return path == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, path, decodeWidth);
    }

    private async Task DeleteAllAsync()
    {
        foreach (var ext in Extensions)
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(Folder, _level + ext));
    }

    /// <summary>Uses an image file picked by the user.</summary>
    public async Task ReplaceAsync(IStorageFile file)
    {
        await AppServices.FileSystem.CreateDirectoryAsync(Folder);
        await DeleteAllAsync();
        string ext = VPath.GetExtension(file.Name).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg")) ext = ".png";
        await StorageImport.CopyFileAsync(AppServices.FileSystem, file, VPath.Combine(Folder, _level + ext));
    }

    /// <summary>Writes a rendered PNG.</summary>
    public async Task SavePngAsync(byte[] png)
    {
        await AppServices.FileSystem.CreateDirectoryAsync(Folder);
        await DeleteAllAsync();
        await AppServices.FileSystem.WriteAllBytesAsync(VPath.Combine(Folder, _level + ".png"), png);
    }
}
