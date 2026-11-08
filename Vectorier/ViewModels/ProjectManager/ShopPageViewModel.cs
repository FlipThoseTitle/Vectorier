using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

// ===================================================================== TRICKS / GEARS

public sealed partial class ShopItemViewModel : ObservableObject
{
    public ShopItemViewModel(ShopItem item, string displayName)
    {
        Item = item;
        _displayName = displayName;
    }

    public ShopItem Item { get; }
    public string Name => Item.Name;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private Bitmap? _shopImage;
    [ObservableProperty] private Bitmap? _trackImage;
    [ObservableProperty] private bool _hasModel;

    public void RaiseName() => OnPropertyChanged(nameof(Name));
}

/// <summary>Shared logic of the Tricks and Gears pages (Shop_payed.xml groups + localization + icons).</summary>
public abstract partial class ShopPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => RemoveCommand;

    protected ShopPageViewModel(ProjectManagerViewModel owner, string title, string icon) : base(owner, title, icon) { }

    public override bool HasSearch => true;

    protected abstract string Group { get; }
    protected abstract string Prefix { get; }
    protected abstract bool HasTrackImage { get; }

    protected string ShopIcons => VPath.Combine(Project.Path, "icons", "shop");
    protected string TrickIcons => VPath.Combine(Project.Path, "icons", "tricks");

    protected readonly List<ShopItemViewModel> All = new();

    public ObservableCollection<ShopItemViewModel> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand), nameof(MoveUpCommand), nameof(MoveDownCommand))]
    private ShopItemViewModel? _selectedItem;

    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _editDisplayName = string.Empty;
    [ObservableProperty] private int _editPrice;

    private bool _suppress;
    public bool HasSelection => SelectedItem != null;

    partial void OnSelectedItemChanged(ShopItemViewModel? value)
    {
        _suppress = true;
        EditName = value?.Name ?? string.Empty;
        EditDisplayName = value?.DisplayName ?? string.Empty;
        EditPrice = value?.Item.Price ?? 0;
        _suppress = false;
    }

    protected override async Task LoadAsync()
    {
        await Owner.EnsureSharedDataAsync();
        string? keep = SelectedItem?.Name;
        All.Clear();
        foreach (var item in Owner.Shop.GetItems(Group))
        {
            var vm = new ShopItemViewModel(item, Owner.Localization.Get(item.Name) ?? string.Empty);
            All.Add(vm);
            _ = LoadImagesAsync(vm);
        }
        ApplyFilter();
        SelectedItem = Items.FirstOrDefault(i => i.Name == keep);
    }

    protected override void ApplyFilter()
    {
        Items.Clear();
        foreach (var item in All.Where(i => Matches(i.Name) || Matches(i.DisplayName)))
            Items.Add(item);
    }

    protected async Task<string?> FindImageAsync(string folder, string name)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string path = VPath.Combine(folder, name + ext);
            if (await AppServices.FileSystem.FileExistsAsync(path))
                return path;
        }
        return null;
    }

    protected virtual async Task LoadImagesAsync(ShopItemViewModel vm)
    {
        string? shop = await FindImageAsync(ShopIcons, vm.Item.ShopImage);
        vm.ShopImage = shop == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, shop, 256);
        if (HasTrackImage)
        {
            string? track = await FindImageAsync(TrickIcons, vm.Item.TrackImage);
            vm.TrackImage = track == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, track, 117);
        }
    }

    protected async Task SaveAsync()
    {
        Owner.Shop.SetItems(Group, All.Select(a => a.Item), HasTrackImage);
        await Owner.Shop.SaveAsync();
        await Owner.Localization.SaveAsync();
    }

    public string FormatName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        string name = input.Trim().ToUpperInvariant().Replace(' ', '_');
        return name.StartsWith(Prefix, StringComparison.Ordinal) ? name : Prefix + name;
    }

    protected abstract Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName);

    [RelayCommand]
    public async Task CommitNameAsync()
    {
        var item = SelectedItem;
        if (item == null || _suppress)
            return;
        string formatted = FormatName(EditName);
        if (formatted.Length == 0 || formatted == item.Name)
        {
            EditName = item.Name;
            return;
        }
        if (All.Any(a => a != item && a.Name == formatted))
        {
            await AppServices.Dialogs.ShowMessageAsync(Title, Loc.F("'{0}' already exists.", formatted), MessageKind.Warning);
            EditName = item.Name;
            return;
        }
        try
        {
            string old = item.Name;
            await RenameAssetsAsync(item, old, formatted);
            item.Item.Name = formatted;
            Owner.Localization.Rename(old, formatted);
            item.RaiseName();
            EditName = formatted;
            await SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Title, ex);
        }
    }

    [RelayCommand]
    public async Task CommitDetailsAsync()
    {
        var item = SelectedItem;
        if (item == null || _suppress)
            return;
        bool changed = false;
        if (EditDisplayName != item.DisplayName)
        {
            item.DisplayName = EditDisplayName;
            Owner.Localization.Set(item.Name, EditDisplayName);
            changed = true;
        }
        if (EditPrice != item.Item.Price)
        {
            item.Item.Price = Math.Max(0, EditPrice);
            changed = true;
        }
        if (changed)
            await SaveAsync();
    }

    partial void OnEditPriceChanged(int value)
    {
        if (!_suppress)
            _ = CommitDetailsAsync();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveAsync()
    {
        var item = SelectedItem;
        if (item == null)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync(Title, Loc.F("Remove '{0}' and its images?", item.Name), Loc.T("Remove"), Loc.T("Cancel"), MessageKind.Warning))
            return;
        await DeleteAssetsAsync(item);
        Owner.Localization.Remove(item.Name);
        All.Remove(item);
        SelectedItem = null;
        ApplyFilter();
        await SaveAsync();
    }

    protected abstract Task DeleteAssetsAsync(ShopItemViewModel item);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveUpAsync() => MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private Task MoveDownAsync() => MoveAsync(1);

    private async Task MoveAsync(int direction)
    {
        var item = SelectedItem;
        if (item == null)
            return;
        int index = All.IndexOf(item);
        int target = index + direction;
        if (target < 0 || target >= All.Count)
            return;
        All.RemoveAt(index);
        All.Insert(target, item);
        ApplyFilter();
        SelectedItem = item;
        await SaveAsync();
    }

    [RelayCommand]
    private async Task ChangeShopImageAsync()
    {
        var item = SelectedItem;
        if (item == null) return;
        var file = await PickImageAsync(Loc.T("Select Shop Image (256 × 226 recommended)"));
        if (file == null) return;
        // The crop window always opens: the image is saved at exactly 256 × 226.
        byte[]? png = await ImageCrop.CropAsync(file, ImageTarget.ShopImage);
        if (png == null) return;
        if (string.IsNullOrEmpty(item.Item.ShopImage)) item.Item.ShopImage = "SHOP_" + item.Name;
        await ReplaceImageAsync(ShopIcons, item.Item.ShopImage, png);
        await LoadImagesAsync(item);
        await SaveAsync();
    }

    protected async Task<IStorageFile?> PickImageAsync(string title)
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Images")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        return files.Count > 0 ? files[0] : null;
    }

    /// <summary>Replaces an image with a cropped PNG (&lt;name&gt;.png; other extensions are removed).</summary>
    protected async Task ReplaceImageAsync(string folder, string name, byte[] png)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(folder, name + ext));
        await AppServices.FileSystem.CreateDirectoryAsync(folder);
        await AppServices.FileSystem.WriteAllBytesAsync(VPath.Combine(folder, name + ".png"), png);
    }

    protected async Task RenameImageAsync(string folder, string oldName, string newName)
    {
        string? path = await FindImageAsync(folder, oldName);
        if (path != null)
            await AppServices.FileSystem.MoveFileAsync(path, VPath.Combine(folder, newName + VPath.GetExtension(path)), true);
    }
}
