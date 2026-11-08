using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

public sealed partial class GearsPageViewModel : ShopPageViewModel
{
    public GearsPageViewModel(ProjectManagerViewModel owner) : base(owner, Loc.T("Gears"), "Gears")
    {
        // Preview needs the selected gear's model file (found in the background).
        ShopItemViewModel? watched = null;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SelectedItem))
                return;
            if (watched != null) watched.PropertyChanged -= OnItemChanged;
            watched = SelectedItem;
            if (watched != null) watched.PropertyChanged += OnItemChanged;
            OnPropertyChanged(nameof(CanPreview));
        };
    }

    private void OnItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShopItemViewModel.HasModel))
            OnPropertyChanged(nameof(CanPreview));
    }

    /// <summary>One gear is selected and its model file exists.</summary>
    public bool CanPreview => SelectedItem is { HasModel: true };

    /// <summary>
    /// Like the Level Settings skin preview: run.bin on models/0.xml with 1.xml and the gear's own model XML attached.
    /// </summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedItem is not { HasModel: true } item)
            return;
        await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Project, Loc.F("Preview - {0}", item.Name),
            new[] { Services.Assets.CharacterModel.DefaultSkin, item.Name },
            ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(Project, ViewModels.Preview.CharacterPreviewViewModel.RunAnimation));
    }

    protected override string Group => ShopService.ClothingGroup;
    protected override string Prefix => "GEAR_";
    protected override bool HasTrackImage => false;

    private string ModelPath(string name) => VPath.Combine(Project.ModelsFolder, name + ".xml");

    protected override async Task LoadImagesAsync(ShopItemViewModel vm)
    {
        await base.LoadImagesAsync(vm);
        vm.HasModel = await AppServices.FileSystem.FileExistsAsync(ModelPath(vm.Name));
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Select the gear's model XML"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Model XML")) { Patterns = new[] { "*.xml" } } },
        });

        string name = files.Count > 0 ? FormatName(VPath.GetFileNameWithoutExtension(files[0].Name)) : "GEAR_NEW";
        string unique = name;
        for (int i = 1; All.Any(a => a.Name == unique); i++)
            unique = $"{name}_{i}";

        try
        {
            if (files.Count > 0)
            {
                await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], ModelPath(unique));
                ProjectContext.For(Project).Models.Invalidate(unique);
            }
            var item = new ShopItem { Name = unique, ShopImage = "SHOP_" + unique };
            var vm = new ShopItemViewModel(item, files.Count > 0 ? VPath.GetFileNameWithoutExtension(files[0].Name) : string.Empty);
            if (vm.DisplayName.Length > 0)
                Owner.Localization.Set(unique, vm.DisplayName);
            All.Add(vm);
            ApplyFilter();
            SelectedItem = vm;
            _ = LoadImagesAsync(vm);
            await SaveAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Title, ex);
        }
    }

    [RelayCommand]
    private async Task ChangeModelAsync()
    {
        var item = SelectedItem;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Select the gear's model XML"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Model XML")) { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;
        await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], ModelPath(item.Name));
        ProjectContext.For(Project).Models.Invalidate(item.Name);
        await LoadImagesAsync(item);
    }

    protected override async Task RenameAssetsAsync(ShopItemViewModel item, string oldName, string newName)
    {
        if (await AppServices.FileSystem.FileExistsAsync(ModelPath(oldName)))
            await AppServices.FileSystem.MoveFileAsync(ModelPath(oldName), ModelPath(newName), true);
        await RenameImageAsync(ShopIcons, item.Item.ShopImage, "SHOP_" + newName);
        item.Item.ShopImage = "SHOP_" + newName;
        ProjectContext.For(Project).Models.Invalidate();
    }

    protected override async Task DeleteAssetsAsync(ShopItemViewModel item)
    {
        await AppServices.FileSystem.DeleteFileAsync(ModelPath(item.Name));
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(ShopIcons, item.Item.ShopImage + ext));
        ProjectContext.For(Project).Models.Invalidate(item.Name);
    }

}
