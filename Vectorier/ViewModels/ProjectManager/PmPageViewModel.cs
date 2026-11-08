using System;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

/// <summary>Base class for the pages listed in the Project Manager's navigation bar.</summary>
public abstract partial class PmPageViewModel : ViewModelBase
{
    protected PmPageViewModel(ProjectManagerViewModel owner, string title, string iconKey)
    {
        Owner = owner;
        Title = title;
        IconKey = iconKey;
    }

    public ProjectManagerViewModel Owner { get; }
    public ProjectInfo Project => Owner.Project;
    public string Title { get; }
    public string IconKey { get; }
    public Avalonia.Media.StreamGeometry? Icon =>
        Application.Current != null && Application.Current.TryGetResource("Icon." + IconKey, null, out var resource)
            ? resource as Avalonia.Media.StreamGeometry
            : null;

    public virtual bool HasSearch => false;

    /// <summary>What the Delete key does on this page (the page's own delete / remove action), or null.</summary>
    public virtual System.Windows.Input.ICommand? DeleteShortcut => null;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;

    private bool _loaded;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    protected virtual void ApplyFilter() { }

    /// <summary>Called when another category is selected (stops sound previews, for example).</summary>
    public virtual void OnDeactivated() { }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;
        _loaded = true;
        await ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        IsLoading = true;
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Title, Loc.T("Failed to load:\n") + ex.Message, MessageKind.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected abstract Task LoadAsync();

    protected bool Matches(string text) =>
        string.IsNullOrWhiteSpace(SearchText) || text.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

    protected static Task ShowErrorAsync(string title, Exception ex) =>
        AppServices.Dialogs.ShowMessageAsync(title, ex.Message, MessageKind.Error);
}
