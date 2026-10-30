using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>A picked file or folder waiting to be imported.</summary>
public sealed class ImportEntry
{
    public ImportEntry(IStorageItem item)
    {
        Item = item;
        Name = item.Name;
        IsFolder = item is IStorageFolder;
        Location = item.TryGetLocalPath() ?? item.Path.ToString();
    }

    public IStorageItem Item { get; }
    public string Name { get; }
    public bool IsFolder { get; }
    public bool IsFile => !IsFolder;
    public string Location { get; }
}

/// <summary>
/// "Import Textures..." / "Import Models...": collects files and folders (folders are imported with everything in
/// them) in one list, so files and folders can be imported together. Returns the picked items, or null.
/// </summary>
public sealed partial class ImportDialogViewModel : DialogViewModel
{
    private readonly string[] _patterns;
    private readonly string _fileTypeName;

    public ImportDialogViewModel(string title, string hint, string fileTypeName, string[] patterns)
    {
        Title = title;
        Hint = hint;
        _fileTypeName = fileTypeName;
        _patterns = patterns;
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(Summary));
            ImportCommand.NotifyCanExecuteChanged();
        };
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 560;
    public override double PreferredHeight => 420;

    public string Hint { get; }

    public ObservableCollection<ImportEntry> Entries { get; } = new();

    public bool HasEntries => Entries.Count > 0;

    public string Summary
    {
        get
        {
            int folders = Entries.Count(e => e.IsFolder), files = Entries.Count - folders;
            return Entries.Count == 0 ? Loc.T("Nothing selected yet.") : Loc.F("{0} file(s), {1} folder(s)", files, folders);
        }
    }

    public bool CanPickFolders => AppServices.Shell?.CurrentTopLevel?.StorageProvider.CanPickFolder == true;

    public void Add(IEnumerable<IStorageItem> items)
    {
        foreach (var item in items)
        {
            var entry = new ImportEntry(item);
            if (Entries.Any(e => e.Location == entry.Location && e.IsFolder == entry.IsFolder))
                continue;
            Entries.Add(entry);
        }
    }

    [RelayCommand]
    private async Task AddFiles()
    {
        var provider = AppServices.Shell?.CurrentTopLevel?.StorageProvider;
        if (provider == null)
            return;
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Title,
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType(_fileTypeName) { Patterns = _patterns }, FilePickerFileTypes.All },
        });
        Add(files);
    }

    [RelayCommand]
    private async Task AddFolders()
    {
        var provider = AppServices.Shell?.CurrentTopLevel?.StorageProvider;
        if (provider == null || !provider.CanPickFolder)
            return;
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Title + Loc.T(" (folders are imported with their content)"), AllowMultiple = true });
        Add(folders);
    }

    [RelayCommand]
    private void Remove(ImportEntry? entry)
    {
        if (entry != null)
            Entries.Remove(entry);
    }

    [RelayCommand]
    private void Clear() => Entries.Clear();

    [RelayCommand(CanExecute = nameof(HasEntries))]
    private void Import() => Close(Entries.Select(e => e.Item).ToList());

    [RelayCommand]
    private void CancelDialog() => Cancel();
}
