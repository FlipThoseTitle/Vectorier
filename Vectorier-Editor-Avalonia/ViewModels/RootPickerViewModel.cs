using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.IO;

namespace Vectorier_Editor_Avalonia.ViewModels;

/// <summary>
/// First screen when the Vectorier folder is not known: always in the browser (local folders need an
/// explicit grant on every launch) and on the desktop when the executable is not inside that folder.
/// </summary>
public sealed partial class RootPickerViewModel : ViewModelBase, IScreen
{
    public string Title => "Vectorier";
    public Size PreferredSize => new(720, 460);
    public bool StartMaximized => false;

    public bool IsBrowser => AppServices.Platform.IsBrowser;

    public string Explanation => IsBrowser
        ? "The browser version works directly with the files on your computer. Select the Vectorier folder " +
          "(the folder that contains Editor, Game and Projects). Files are read and written locally; nothing is uploaded."
        : "The Vectorier folder (the folder that contains Editor, Game and Projects) was not found next to the " +
          "application. Select it to continue. Your choice is remembered.";

    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _isBusy;

    public Task<bool> CanCloseAsync() => Task.FromResult(true);
    public void OnClosed() { }

    [RelayCommand]
    private async Task PickFolderAsync()
    {
        Error = null;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null)
            return;

        if (!topLevel.StorageProvider.CanPickFolder)
        {
            Error = "This browser cannot open local folders. Use a Chromium-based browser (Chrome, Edge, Opera) or the desktop version.";
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the Vectorier folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return;

        IsBusy = true;
        try
        {
            var fs = AppServices.Platform.CreateFileSystemFromPickedFolder(folders[0]);
            if (fs == null)
            {
                Error = "The selected folder cannot be opened.";
                return;
            }

            bool hasProjects = await fs.DirectoryExistsAsync("Projects");
            bool hasEditor = await fs.DirectoryExistsAsync("Editor");
            if (!hasProjects || !hasEditor)
            {
                bool proceed = await AppServices.Dialogs.ConfirmAsync("Vectorier Folder",
                    "The selected folder does not contain both 'Editor' and 'Projects'.\n\nUse it anyway? A 'Projects' folder will be created.",
                    "Use Folder", "Cancel", Services.MessageKind.Warning);
                if (!proceed)
                    return;
                await fs.CreateDirectoryAsync("Projects");
            }

            AppServices.SetFileSystem(fs);
            AppServices.Shell.Show(new ProjectSelectionViewModel());
        }
        finally
        {
            IsBusy = false;
        }
    }
}
