using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;
using Vectorier.ViewModels.Editor;

namespace Vectorier.ViewModels.ProjectManager;

/// <summary>Second window: manages one project's levels, locations and assets.</summary>
public sealed partial class ProjectManagerViewModel : ViewModelBase, IScreen
{
    public ProjectManagerViewModel(ProjectInfo project, string? initialPage = null, string? selectLevel = null)
    {
        Project = project;
        var fs = AppServices.FileSystem;
        LocationList = new LocationListService(fs, project);
        Localization = new LocalizationService(fs, project);
        Shop = new ShopService(fs, project);

        Levels = new LevelsPageViewModel(this);
        Locations = new LocationsPageViewModel(this);
        Pages.Add(Levels);
        Pages.Add(Locations);
        Pages.Add(new TexturesPageViewModel(this));
        Pages.Add(new AnimationsPageViewModel(this));
        Pages.Add(new TricksPageViewModel(this));
        Pages.Add(new GearsPageViewModel(this));
        Pages.Add(FileListPageViewModel.Models(this));
        Pages.Add(FileListPageViewModel.Musics(this));
        Pages.Add(FileListPageViewModel.Sounds(this));
        Pages.Add(FileListPageViewModel.Videos(this));
        Pages.Add(FileListPageViewModel.XmlFiles(this));

        _pendingLevelSelection = selectLevel;
        SelectedPage = Pages.FirstOrDefault(p => p.IconKey == initialPage) ?? Levels;
    }

    private string? _pendingLevelSelection;

    public ProjectInfo Project { get; }
    public string Title => Loc.F("Project Manager - {0}", Project.Name);
    public Size PreferredSize => new(1280, 800);
    public bool StartMaximized => false;

    public LocationListService LocationList { get; }
    public LocalizationService Localization { get; }
    public ShopService Shop { get; }

    public LevelsPageViewModel Levels { get; }
    public LocationsPageViewModel Locations { get; }

    public ObservableCollection<PmPageViewModel> Pages { get; } = new();

    [ObservableProperty] private PmPageViewModel? _selectedPage;

    private Task? _sharedLoad;

    /// <summary>Loads List_Payed.xml, localization and shop once for all pages.</summary>
    public Task EnsureSharedDataAsync() => _sharedLoad ??= LoadSharedAsync();

    private async Task LoadSharedAsync()
    {
        await LocationList.LoadAsync();
        await Localization.LoadAsync();
        await Shop.LoadAsync();
    }

    partial void OnSelectedPageChanged(PmPageViewModel? oldValue, PmPageViewModel? newValue) => oldValue?.OnDeactivated();

    async partial void OnSelectedPageChanged(PmPageViewModel? value)
    {
        if (value == null)
            return;
        await value.EnsureLoadedAsync();
        if (value == Locations)
            await Locations.EnsureFreshAsync();
        if (value == Levels && _pendingLevelSelection != null)
        {
            Levels.SelectLevelByName(_pendingLevelSelection);
            _pendingLevelSelection = null;
        }
    }

    public Task<bool> CanCloseAsync() => Task.FromResult(true);

    public void OnClosed() => AppServices.Platform.StopSound();

    [RelayCommand]
    private void BackToProjects() => AppServices.Shell.Show(new ProjectSelectionViewModel());

    /// <summary>Closes the Project Manager and opens the level in the editor.</summary>
    public void OpenLevel(LevelData level)
    {
        string path = Services.IO.VPath.Combine(Project.LevelsFolder, level.Name + ".xml");
        AppServices.Shell.Show(new EditorViewModel(Project, level.Name, path));
    }

    [RelayCommand]
    private void OpenEmptyEditor() => AppServices.Shell.Show(new EditorViewModel(Project, null, null));
}
