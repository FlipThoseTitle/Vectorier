using Vectorier.ViewModels;
using Vectorier.ViewModels.Dialogs;
using Vectorier.ViewModels.ProjectManager;
using Vectorier.Views.Dialogs;

namespace Vectorier.Views;

/// <summary>View model → view registrations used by <see cref="ViewLocator"/>.</summary>
public static class ViewRegistry
{
    private static bool _registered;

    public static void RegisterAll()
    {
        if (_registered)
            return;
        _registered = true;

        ViewLocator.Register<RootPickerViewModel>(() => new RootPickerView());
        ViewLocator.Register<ProjectSelectionViewModel>(() => new ProjectSelectionView());
        ViewLocator.Register<MessageDialogViewModel>(() => new MessageDialogView());
        ViewLocator.Register<ImportDialogViewModel>(() => new ImportDialogView());
        ViewLocator.Register<ProjectManagerViewModel>(() => new ProjectManager.ProjectManagerView());
        ViewLocator.Register<LevelsPageViewModel>(() => new ProjectManager.LevelsPageView());
        ViewLocator.Register<LocationsPageViewModel>(() => new ProjectManager.LocationsPageView());
        ViewLocator.Register<TexturesPageViewModel>(() => new ProjectManager.TexturesPageView());
        ViewLocator.Register<AnimationsPageViewModel>(() => new ProjectManager.AnimationsPageView());
        ViewLocator.Register<TricksPageViewModel>(() => new ProjectManager.TricksPageView());
        ViewLocator.Register<GearsPageViewModel>(() => new ProjectManager.GearsPageView());
        ViewLocator.Register<FileListPageViewModel>(() => new ProjectManager.FileListPageView());
        ViewLocator.Register<TemplatesDialogViewModel>(() => new ProjectManager.TemplatesDialogView());
        ViewLocator.Register<UnlockEditorViewModel>(() => new ProjectManager.UnlockEditorView());
        ViewLocator.Register<TrickPickerDialogViewModel>(() => new ProjectManager.TrickPickerDialogView());
        ViewLocator.Register<ViewModels.Editor.EditorViewModel>(() => new Editor.EditorView());
        ViewLocator.Register<ViewModels.Editor.LevelSettingsViewModel>(() => new Editor.LevelSettingsView());
        ViewLocator.Register<ViewModels.Preview.CharacterPreviewViewModel>(() => new Preview.CharacterPreviewView());
        ViewLocator.Register<ViewModels.Editor.ThumbnailCaptureViewModel>(() => new Editor.ThumbnailCaptureDialogView());
        ViewLocator.Register<ViewModels.Editor.SkinPickerDialogViewModel>(() => new Editor.SkinPickerDialogView());
        ViewLocator.Register<ViewModels.Editor.PreferencesViewModel>(() => new Editor.PreferencesView());
        ViewLocator.Register<ViewModels.Editor.TriggerEditorViewModel>(() => new Editor.TriggerEditorView());
    }
}
