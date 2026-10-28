using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Assets;
using Vectorier_Editor_Avalonia.Services.Projects;
using Vectorier_Editor_Avalonia.ViewModels.ProjectManager;

namespace Vectorier_Editor_Avalonia.ViewModels;

public sealed partial class ProjectItemViewModel : ObservableObject
{
    public ProjectItemViewModel(ProjectInfo project)
    {
        Project = project;
    }

    [ObservableProperty] private ProjectInfo _project;

    public string Name => Project.Name;
    public string DisplayPath => Project.DisplayPath;

    partial void OnProjectChanged(ProjectInfo value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DisplayPath));
    }
}

/// <summary>First window: lists the projects in the Projects folder and edits their metadata.</summary>
public sealed partial class ProjectSelectionViewModel : ViewModelBase, IScreen
{
    private ProjectService Projects => AppServices.Projects;
    private bool _loadingDetails;

    public ProjectSelectionViewModel()
    {
        _ = RefreshAsync();
    }

    public string Title => "Project Selection";
    public Size PreferredSize => new(1060, 680);
    public bool StartMaximized => false;

    public ObservableCollection<ProjectItemViewModel> ProjectList { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DeleteProjectCommand), nameof(DuplicateProjectCommand), nameof(ShowInExplorerCommand), nameof(OpenProjectCommand), nameof(ChangeThumbnailCommand), nameof(PublishProjectCommand))]
    private ProjectItemViewModel? _selectedProject;

    public bool HasSelection => SelectedProject != null;

    [ObservableProperty] private Bitmap? _thumbnail;
    [ObservableProperty] private string _editName = string.Empty;
    [ObservableProperty] private string _author = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string? _nameError;
    [ObservableProperty] private string? _busyText;
    [ObservableProperty] private bool _isEmpty;

    private string _savedAuthor = string.Empty;
    private string _savedDescription = string.Empty;

    public bool CanShowInExplorer => AppServices.Platform.CanShowInExplorer;
    public string RootDisplay => AppServices.FileSystem.RootDisplayName;

    public async Task<bool> CanCloseAsync()
    {
        await CommitAllAsync();
        return true;
    }

    public void OnClosed() { }

    // ================= LIST ================= //

    [RelayCommand]
    private async Task RefreshAsync()
    {
        string? keep = SelectedProject?.Name;
        var projects = await Projects.GetProjectsAsync();
        ProjectList.Clear();
        foreach (var project in projects)
            ProjectList.Add(new ProjectItemViewModel(project));
        IsEmpty = ProjectList.Count == 0;
        SelectedProject = keep == null ? null : ProjectList.FirstOrDefault(p => p.Name == keep);
    }

    async partial void OnSelectedProjectChanged(ProjectItemViewModel? oldValue, ProjectItemViewModel? newValue)
    {
        if (oldValue != null)
            await CommitMetadataAsync(oldValue.Project);
        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        _loadingDetails = true;
        try
        {
            NameError = null;
            var item = SelectedProject;
            if (item == null)
            {
                Thumbnail = null;
                EditName = Author = Description = string.Empty;
                return;
            }

            EditName = item.Name;
            Author = _savedAuthor = (await Projects.ReadTextAsync(item.Project, ProjectService.AuthorFile)).Trim();
            Description = _savedDescription = await Projects.ReadTextAsync(item.Project, ProjectService.DescriptionFile);
            string? thumb = await Projects.FindThumbnailAsync(item.Project);
            Thumbnail = thumb == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, thumb, 960);
        }
        finally
        {
            _loadingDetails = false;
        }
    }

    partial void OnEditNameChanged(string value)
    {
        if (_loadingDetails || SelectedProject == null)
            return;
        NameError = value == SelectedProject.Name ? null : ProjectService.ValidateName(value);
    }

    // ================= METADATA ================= //

    /// <summary>Applies a changed project name (rename the folder). Called when the field loses focus or Enter is pressed.</summary>
    [RelayCommand]
    public async Task CommitNameAsync()
    {
        var item = SelectedProject;
        if (item == null || _loadingDetails)
            return;

        string newName = EditName.Trim();
        if (newName == item.Name)
        {
            EditName = item.Name;
            NameError = null;
            return;
        }

        string? error = ProjectService.ValidateName(newName);
        var existing = ProjectList.FirstOrDefault(p => !ReferenceEquals(p, item) && string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase));
        if (error == null && existing != null)
            error = $"A project named '{newName}' already exists.";

        if (error != null)
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename Project", error, MessageKind.Warning);
            EditName = item.Name;
            NameError = null;
            return;
        }

        try
        {
            await CommitMetadataAsync(item.Project);
            item.Project = await Projects.RenameAsync(item.Project, newName);
            EditName = item.Name;
            NameError = null;
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Rename Project", "The project could not be renamed:\n" + ex.Message, MessageKind.Error);
            EditName = item.Name;
        }
    }

    [RelayCommand]
    public async Task CommitAuthorAsync()
    {
        if (SelectedProject != null && !_loadingDetails)
            await CommitMetadataAsync(SelectedProject.Project);
    }

    private async Task CommitMetadataAsync(ProjectInfo project)
    {
        try
        {
            string author = Author.Trim();
            if (author != _savedAuthor)
            {
                await Projects.WriteTextAsync(project, ProjectService.AuthorFile, author);
                _savedAuthor = author;
            }
            if (Description != _savedDescription)
            {
                await Projects.WriteTextAsync(project, ProjectService.DescriptionFile, Description);
                _savedDescription = Description;
            }
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Project", "The project details could not be saved:\n" + ex.Message, MessageKind.Error);
        }
    }

    private async Task CommitAllAsync()
    {
        if (SelectedProject == null)
            return;
        await CommitMetadataAsync(SelectedProject.Project);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ChangeThumbnailAsync()
    {
        var item = SelectedProject;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Project Thumbnail (960 × 540 recommended)",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg" } } },
        });
        if (files.Count == 0)
            return;

        try
        {
            await Projects.SetThumbnailAsync(item.Project, files[0]);
            string? thumb = await Projects.FindThumbnailAsync(item.Project);
            Thumbnail = thumb == null ? null : await ImageLoader.LoadAsync(AppServices.FileSystem, thumb, 960);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Thumbnail", "The thumbnail could not be replaced:\n" + ex.Message, MessageKind.Error);
        }
    }

    // ================= PROJECT OPERATIONS ================= //

    [RelayCommand]
    private async Task AddProjectAsync()
    {
        if (!await Projects.TemplateExistsAsync())
        {
            await AppServices.Dialogs.ShowMessageAsync("Create Project",
                $"The project template was not found in Projects/{ProjectService.TemplateName}.", MessageKind.Error);
            return;
        }

        string? name = await AppServices.Dialogs.PromptAsync("Create New Project", "Enter a name for the new project:", "New Project",
            ProjectService.ValidateName, "Create");
        if (name == null)
            return;

        await RunBusyAsync("Creating project…", async progress =>
        {
            var project = await Projects.CreateAsync(name, progress);
            await RefreshAsync();
            SelectedProject = ProjectList.FirstOrDefault(p => p.Name == project.Name);
        }, "The project could not be created");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteProjectAsync()
    {
        var item = SelectedProject;
        if (item == null)
            return;

        if (!await AppServices.Dialogs.ConfirmAsync("Delete Project",
                $"Are you sure you want to delete '{item.Name}'?\nThis cannot be undone.", "Delete", "Cancel", MessageKind.Warning))
            return;

        await RunBusyAsync("Deleting project…", async _ =>
        {
            _loadingDetails = true;
            SelectedProject = null;
            _loadingDetails = false;
            await Projects.DeleteAsync(item.Project);
            await RefreshAsync();
        }, "The project could not be deleted");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateProjectAsync()
    {
        var item = SelectedProject;
        if (item == null)
            return;

        await RunBusyAsync("Duplicating project…", async progress =>
        {
            await CommitMetadataAsync(item.Project);
            var copy = await Projects.DuplicateAsync(item.Project, progress);
            await RefreshAsync();
            SelectedProject = ProjectList.FirstOrDefault(p => p.Name == copy.Name);
        }, "The project could not be duplicated");
    }

    [RelayCommand]
    private async Task ImportProjectAsync()
    {
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (topLevel == null || !topLevel.StorageProvider.CanPickFolder)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a project folder to import",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return;

        await RunBusyAsync("Importing project…", async progress =>
        {
            var project = await Projects.ImportAsync(folders[0], progress);
            await RefreshAsync();
            SelectedProject = ProjectList.FirstOrDefault(p => p.Name == project.Name);
        }, "The project could not be imported");
    }

    /// <summary>Copies the selected project into a picked folder, leaving out the editor's .vectorier cache.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PublishProjectAsync()
    {
        var item = SelectedProject;
        var topLevel = AppServices.Shell.CurrentTopLevel;
        if (item == null || topLevel == null || !topLevel.StorageProvider.CanPickFolder)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = $"Publish '{item.Name}' to…",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return;
        var destination = folders[0];

        string? local = destination.TryGetLocalPath();
        if (local != null && System.IO.Directory.Exists(System.IO.Path.Combine(local, item.Name)) &&
            !await AppServices.Dialogs.ConfirmAsync("Publish Project",
                $"'{item.Name}' already exists in the selected folder.\nIts files will be overwritten.", "Publish", "Cancel", MessageKind.Warning))
            return;

        await RunBusyAsync("Publishing project…", async progress =>
        {
            await CommitMetadataAsync(item.Project);
            await Projects.PublishAsync(item.Project, destination, progress);
            BusyText = null;
            await AppServices.Dialogs.ShowMessageAsync("Publish Project", $"'{item.Name}' was published to:\n{local ?? destination.Name}");
        }, "The project could not be published");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ShowInExplorer()
    {
        if (SelectedProject != null)
            AppServices.Platform.ShowInExplorer(AppServices.FileSystem, SelectedProject.Project.Path);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public async Task OpenProjectAsync()
    {
        var item = SelectedProject;
        if (item == null)
            return;
        await CommitMetadataAsync(item.Project);
        AppServices.Shell.Show(new ProjectManagerViewModel(item.Project));
    }

    [RelayCommand]
    private void ClearSelection() => SelectedProject = null;

    private async Task RunBusyAsync(string text, Func<IProgress<string>, Task> action, string errorTitle)
    {
        BusyText = text;
        var progress = new ThrottledProgress<string>(new Progress<string>(file => BusyText = $"{text}  {file}"), TimeSpan.FromMilliseconds(100));
        try
        {
            await action(progress);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Vectorier", $"{errorTitle}:\n{ex.Message}", MessageKind.Error);
        }
        finally
        {
            BusyText = null;
        }
    }
}
