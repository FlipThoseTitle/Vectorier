using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

public sealed partial class TricksPageViewModel : ShopPageViewModel
{
    public TricksPageViewModel(ProjectManagerViewModel owner) : base(owner, Loc.T("Tricks"), "Tricks") { }

    protected override string Group => ShopService.TrickGroup;
    protected override string Prefix => "TRICK_";
    protected override bool HasTrackImage => true;

    [RelayCommand]
    private async Task AddAsync()
    {
        string name = "TRICK_NEW";
        for (int i = 1; All.Any(a => a.Name == name); i++)
            name = $"TRICK_NEW_{i}";
        var item = new ShopItem { Name = name, ShopImage = "SHOP_" + name, TrackImage = "TRACK_" + name };
        var vm = new ShopItemViewModel(item, string.Empty);
        All.Add(vm);
        ApplyFilter();
        SelectedItem = vm;
        await SaveAsync();
    }

    protected override async Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName)
    {
        await RenameImageAsync(ShopIcons, item.Item.ShopImage, "SHOP_" + newName);
        await RenameImageAsync(TrickIcons, item.Item.TrackImage, "TRACK_" + newName);
        item.Item.ShopImage = "SHOP_" + newName;
        item.Item.TrackImage = "TRACK_" + newName;
    }

    protected override async Task DeleteAssetsAsync(ShopItemViewModel item)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(ShopIcons, item.Item.ShopImage + ext));
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(TrickIcons, item.Item.TrackImage + ext));
        }
    }


    [RelayCommand]
    private async Task ChangeTrackImageAsync()
    {
        var item = SelectedItem;
        if (item == null) return;
        var file = await PickImageAsync(Loc.T("Select Track Image (117 × 117)"));
        if (file == null) return;
        // The crop window always opens: the image is saved at exactly 117 × 117.
        byte[]? png = await ImageCrop.CropAsync(file, ImageTarget.TrackImage);
        if (png == null) return;
        if (string.IsNullOrEmpty(item.Item.TrackImage)) item.Item.TrackImage = "TRACK_" + item.Name;
        await ReplaceImageAsync(TrickIcons, item.Item.TrackImage, png);
        await LoadImagesAsync(item);
        await SaveAsync();
    }
}
