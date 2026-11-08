using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Assets;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

// ===================================================================== TEXTURES

public sealed partial class TextureItemViewModel : ObservableObject
{
    public TextureItemViewModel(TextureEntry entry, TextureLibrary library)
    {
        Entry = entry;
        Library = library;
    }

    public TextureEntry Entry { get; }
    public TextureLibrary Library { get; }
    public string Name => Entry.Name;
    public string FileName => Entry.FileName;
    public bool IsAnimated => Entry.IsAnimated;
    public bool IsProtected => TextureLibrary.IsProtected(Entry);

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Textures page: one grid with every static and animated texture of the project.</summary>
public sealed partial class TexturesPageViewModel : PmPageViewModel
{
    public override System.Windows.Input.ICommand? DeleteShortcut => DeleteSelectedCommand;

    private readonly List<TextureItemViewModel> _all = new();

    public TexturesPageViewModel(ProjectManagerViewModel owner) : base(owner, Loc.T("Textures"), "Textures") { }

    public override bool HasSearch => true;

    public TextureLibrary Library => ProjectContext.For(Project).Textures;

    public ObservableCollection<TextureItemViewModel> Items { get; } = new();
    public ObservableCollection<TextureItemViewModel> SelectedItems { get; } = new();

    [ObservableProperty] private string _countText = string.Empty;

    protected override async Task LoadAsync()
    {
        await Library.RefreshIndexAsync();
        _all.Clear();
        SelectedItems.Clear();
        _anchor = null;
        // Every file is listed here, including the game's own sprite sheets (they are protected, not hidden).
        foreach (var entry in Library.Entries)
            _all.Add(new TextureItemViewModel(entry, Library));
        ApplyFilter();
    }

    protected override void ApplyFilter()
    {
        var list = _all.Where(i => Matches(i.FileName)).ToList();
        Items.Clear();
        foreach (var item in list)
            Items.Add(item);
        foreach (var gone in SelectedItems.Where(s => !list.Contains(s)).ToList())
        {
            gone.IsSelected = false;
            SelectedItems.Remove(gone);
        }
        CountText = Loc.F("Textures: {0}", Items.Count);
        ItemsRebuilt?.Invoke();
    }

    /// <summary>The grid rebuilds its rows (only visible rows are created).</summary>
    public event Action? ItemsRebuilt;

    private TextureItemViewModel? _anchor;

    /// <summary>Click: select only this one. Ctrl: add or remove it. Shift: the range from the last clicked tile.</summary>
    public void Click(TextureItemViewModel item, bool ctrl, bool shift)
    {
        if (shift && _anchor != null && Items.Contains(_anchor))
        {
            int a = Items.IndexOf(_anchor), b = Items.IndexOf(item);
            if (a > b) (a, b) = (b, a);
            if (!ctrl) ClearSelection();
            for (int i = a; i <= b; i++)
                Select(Items[i], true);
            return;
        }
        if (ctrl)
        {
            Select(item, !item.IsSelected);
        }
        else
        {
            ClearSelection();
            Select(item, true);
        }
        _anchor = item;
    }

    private void Select(TextureItemViewModel item, bool on)
    {
        item.IsSelected = on;
        if (on && !SelectedItems.Contains(item)) SelectedItems.Add(item);
        else if (!on) SelectedItems.Remove(item);
    }

    public void ClearSelection()
    {
        foreach (var s in SelectedItems) s.IsSelected = false;
        SelectedItems.Clear();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Items) Select(item, true);
    }

    [RelayCommand]
    private async Task ImportFilesAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("Import Textures (select an image and its .plist together for animated textures)"),
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(Loc.T("Textures")) { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.plist", "*.json" } } },
        });
        if (files.Count > 0)
            await ImportItemsAsync(files);
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null || !topLevel.StorageProvider.CanPickFolder)
            return;
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Import a folder of textures"), AllowMultiple = false });
        if (folders.Count > 0)
            await ImportItemsAsync(folders);
    }

    /// <summary>Imports dropped or picked files and folders (folders are processed recursively).</summary>
    public async Task ImportItemsAsync(IEnumerable<IStorageItem> items)
    {
        IsLoading = true;
        try
        {
            var files = await TextureImport.CollectAsync(items);
            var replace = new ReplaceAsker(Loc.T("Import Textures"));
            await Library.ImportAsync(files.Select(f => (f.File.Name, (Func<Task<byte[]>>)(() => StorageImport.ReadAllBytesAsync(f.File)))).ToList(),
                name => replace.AskAsync(Loc.F("'{0}' already exists. Replace it?", name)));
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Loc.T("Import Textures"), ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = SelectedItems.ToList();
        if (selected.Count == 0)
            return;
        var deletable = selected.Where(s => !s.IsProtected).ToList();
        if (deletable.Count == 0)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Delete Textures"), Loc.T("The selected textures are used by the game itself and cannot be deleted."), MessageKind.Info);
            return;
        }
        string list = string.Join(", ", deletable.Take(5).Select(d => d.FileName)) + (deletable.Count > 5 ? $" and {deletable.Count - 5} more" : "");
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete Textures"), Loc.F("Delete {0}?\nLevels that use them will show them as missing.", list), Loc.T("Delete"), Loc.T("Cancel"), MessageKind.Warning))
            return;

        foreach (var item in deletable)
            await Library.DeleteAsync(item.Entry);
        await LoadAsync();
    }
}

public static class TextureImport
{
    /// <summary>Flattens picked/dropped items into files (recursing into folders).</summary>
    public static async Task<List<(IStorageFile File, string Folder)>> CollectAsync(IEnumerable<IStorageItem> items)
    {
        var result = new List<(IStorageFile, string)>();
        foreach (var item in items)
        {
            if (item is IStorageFile file)
                result.Add((file, string.Empty));
            else if (item is IStorageFolder folder)
            {
                foreach (var (f, relative) in await StorageImport.ListFilesAsync(folder))
                    result.Add((f, VPath.Combine(folder.Name, relative)));
            }
        }
        return result;
    }
}
