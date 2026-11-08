using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

// ===================================================================== ANIMATIONS

/// <summary>Animations page: the moves XML and the list of animation .bin files.</summary>
public sealed partial class AnimationsPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => DeleteSelectedCommand;

    public const string MovesFileName = "moves.xml";
    private readonly List<string> _all = new();

    public AnimationsPageViewModel(ProjectManagerViewModel owner) : base(owner, Loc.T("Animations"), "Animations")
    {
        SelectedItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanPreview));
    }

    /// <summary>One .bin selected: it can be previewed on the character.</summary>
    public bool CanPreview => SelectedItems.Count == 1 && VPath.HasExtension(SelectedItems[0], ".bin", ".bytes");

    /// <summary>Plays the selected animation on models/0.xml + 1.xml, the camera following the Camera node.</summary>
    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!CanPreview)
            return;
        string name = SelectedItems[0];
        await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Project, Loc.F("Preview - {0}", name),
            new[] { Services.Assets.CharacterModel.DefaultSkin }, VPath.Combine(DataFolder, name));
    }

    public override bool HasSearch => true;

    private string DataFolder => VPath.Combine(Project.AnimationsFolder, "data");
    private string MovesPath => VPath.Combine(Project.AnimationsFolder, MovesFileName);

    public ObservableCollection<string> Items { get; } = new();
    public ObservableCollection<string> SelectedItems { get; } = new();

    [ObservableProperty] private string _movesFile = "None";
    [ObservableProperty] private string _countText = string.Empty;

    protected override async Task LoadAsync()
    {
        MovesFile = await AppServices.FileSystem.FileExistsAsync(MovesPath) ? MovesFileName : "None";
        _all.Clear();
        foreach (var file in await AppServices.FileSystem.GetFilesAsync(DataFolder))
        {
            if (VPath.HasExtension(file, ".bin", ".bytes"))
                _all.Add(VPath.GetFileName(file));
        }
        ApplyFilter();
    }

    protected override void ApplyFilter()
    {
        Items.Clear();
        foreach (var name in _all.Where(Matches))
            Items.Add(name);
        CountText = Loc.F("Animations: {0}", Items.Count);
    }

    /// <summary>Replaces animations/moves.xml (always saved under that name, whatever the source is called).</summary>
    [RelayCommand]
    private async Task ChooseMovesAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Select the Moves XML"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Moves XML")) { Patterns = new[] { "*.xml" } } },
        });
        if (files.Count == 0)
            return;
        try
        {
            await StorageImport.CopyFileAsync(AppServices.FileSystem, files[0], MovesPath);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.T("Moves XML"), ex);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Import Animations"),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Animation")) { Patterns = new[] { "*.bin", "*.bytes" } } },
        });
        await ImportItemsAsync(files);
    }

    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        try
        {
            var replace = new ReplaceAsker(Loc.T("Import"));
            foreach (var (file, _) in await TextureImport.CollectAsync(items))
            {
                if (VPath.HasExtension(file.Name, ".bin", ".bytes"))
                    await StorageImport.CopyFileAsync(AppServices.FileSystem, file, VPath.Combine(DataFolder, file.Name));
                else if (string.Equals(file.Name, MovesFileName, StringComparison.OrdinalIgnoreCase))
                    await StorageImport.CopyFileAsync(AppServices.FileSystem, file, MovesPath);
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.T("Import Animations"), ex);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedItems.ToList();
        if (selected.Count == 0)
            return;
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Animations"), Loc.F("Delete {0} animation file(s)?", selected.Count), Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
            return;
        foreach (var name in selected)
            await AppServices.FileSystem.DeleteFileAsync(VPath.Combine(DataFolder, name));
        await LoadAsync();
    }
}
