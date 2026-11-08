using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.Services.Assets;

/// <summary>A kind of picture the editor asks for, with the resolution it is saved at.</summary>
public sealed record ImageTarget(string Name, int Width, int Height)
{
    public static ImageTarget ProjectThumbnail => new(Loc.T("Project Thumbnail"), ProjectService.ThumbnailWidth, ProjectService.ThumbnailHeight);
    public static ImageTarget LevelThumbnail => new(Loc.T("Level Thumbnail"), Projects.LevelThumbnail.Width, Projects.LevelThumbnail.Height);
    public static ImageTarget LocationImage => new(Loc.T("Location Image"), 1329, 512);
    public static ImageTarget ShopImage => new(Loc.T("Shop Image"), 256, 226);
    public static ImageTarget TrackImage => new(Loc.T("Track Image"), 117, 117);
}

/// <summary>
/// The crop step of the image-change options (project, level and location thumbnails, shop and track images):
/// every picked image opens the crop window on a canvas of the target's resolution, and what is saved is always
/// a PNG of exactly that resolution. Texture importing does not use it.
/// </summary>
public static class ImageCrop
{
    /// <summary>
    /// Opens the crop window for a picked image. Returns the PNG to save, or null when the user cancelled or the
    /// file is not a readable image (the user is told).
    /// </summary>
    public static async Task<byte[]?> CropAsync(IStorageFile file, ImageTarget target)
    {
        using var source = await ImageLoader.LoadAsync(file);
        if (source == null || source.PixelSize.Width <= 0 || source.PixelSize.Height <= 0)
        {
            await AppServices.Dialogs.ShowMessageAsync(target.Name, Loc.F("'{0}' could not be read as an image.", file.Name), MessageKind.Error);
            return null;
        }
        var dialog = new ImageCropViewModel(source, target.Width, target.Height, target.Name);
        return await AppServices.Dialogs.ShowAsync(dialog) as byte[];
    }
}
