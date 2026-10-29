using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.Editor;

/// <summary>Edit ▸ Preferences: theme plus the Scene and Hierarchy options of Unity's Preference window.</summary>
public sealed partial class PreferencesViewModel : DialogViewModel
{
    public PreferencesViewModel()
    {
        Title = "Preferences";
        _isDark = EditorPreferences.Theme == "Dark";
        _showOutline = EditorPreferences.ShowOutline;
        _showPlatformOutline = EditorPreferences.ShowPlatformOutline;
        _showTriggerText = EditorPreferences.ShowTriggerText;
        _showAreaText = EditorPreferences.ShowAreaText;
        _showAreaType = EditorPreferences.ShowAreaType;
        _textAnchor = EditorPreferences.TextAnchor;
        _renameDuplicate = EditorPreferences.RenameDuplicate;
    }

    public static IReadOnlyList<LabelAnchor> Anchors { get; } = Enum.GetValues<LabelAnchor>();

    [ObservableProperty] private bool _isDark;
    [ObservableProperty] private bool _showOutline;
    [ObservableProperty] private bool _showPlatformOutline;
    [ObservableProperty] private bool _showTriggerText;
    [ObservableProperty] private bool _showAreaText;
    [ObservableProperty] private bool _showAreaType;
    [ObservableProperty] private LabelAnchor _textAnchor;
    [ObservableProperty] private bool _renameDuplicate;

    // Changes apply immediately, like Unity's preference window.
    partial void OnIsDarkChanged(bool value) => EditorPreferences.Theme = value ? "Dark" : "Light";
    partial void OnShowOutlineChanged(bool value) => EditorPreferences.ShowOutline = value;
    partial void OnShowPlatformOutlineChanged(bool value) => EditorPreferences.ShowPlatformOutline = value;
    partial void OnShowTriggerTextChanged(bool value) => EditorPreferences.ShowTriggerText = value;
    partial void OnShowAreaTextChanged(bool value) => EditorPreferences.ShowAreaText = value;
    partial void OnShowAreaTypeChanged(bool value) => EditorPreferences.ShowAreaType = value;
    partial void OnTextAnchorChanged(LabelAnchor value) => EditorPreferences.TextAnchor = value;
    partial void OnRenameDuplicateChanged(bool value) => EditorPreferences.RenameDuplicate = value;

    [RelayCommand]
    private void Done() => Close(true);
}
