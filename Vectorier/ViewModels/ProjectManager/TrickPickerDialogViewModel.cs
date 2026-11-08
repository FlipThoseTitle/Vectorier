using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services.Localization;
using Vectorier.ViewModels.Dialogs;

namespace Vectorier.ViewModels.ProjectManager;

/// <summary>A trick tile of the picker.</summary>
public sealed partial class TrickChoiceViewModel : ObservableObject
{
    private readonly Action<TrickChoiceViewModel, bool> _changed;
    private bool _syncing;

    public TrickChoiceViewModel(TrackEditorViewModel track, TrickInfo info)
        : this(info, track.HasTrick(info.Name), (c, on) => track.SetTrick(c.Info.Name, on)) { }

    public TrickChoiceViewModel(TrickInfo info, bool selected, Action<TrickChoiceViewModel, bool> changed)
    {
        Info = info;
        _isSelected = selected;
        _changed = changed;
    }

    public TrickInfo Info { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (!_syncing)
            _changed(this, value);
    }

    /// <summary>Changes the check without notifying (single-choice pickers).</summary>
    public void Sync(bool value)
    {
        _syncing = true;
        IsSelected = value;
        _syncing = false;
    }

    [RelayCommand]
    private void Toggle() => IsSelected = !IsSelected;
}

/// <summary>Grid of every trick; clicking one adds it to (or removes it from) the level immediately.</summary>
public sealed partial class TrickPickerDialogViewModel : DialogViewModel
{
    public TrickPickerDialogViewModel(TrackEditorViewModel track)
    {
        Title = Loc.T("Select Tricks");
        Choices = track.AvailableTricks.Select(t => new TrickChoiceViewModel(track, t)).ToList();
    }

    /// <summary>Single choice (an Area's trick): picking one replaces the previous one right away.</summary>
    public TrickPickerDialogViewModel(IEnumerable<TrickInfo> tricks, string? current, Action<string?> set)
    {
        Title = Loc.T("Select Trick");
        Hint = Loc.T("Click a trick to use it for this Area (only one). Changes apply right away.");
        var list = new List<TrickChoiceViewModel>();
        foreach (var info in tricks)
        {
            list.Add(new TrickChoiceViewModel(info, info.Name == current, (choice, on) =>
            {
                foreach (var other in list.Where(c => !ReferenceEquals(c, choice)))
                    other.Sync(false);
                set(on ? choice.Info.Name : null);
            }));
        }
        Choices = list;
    }

    public string Hint { get; } = Loc.T("Click a trick to add it to the level or remove it. Changes apply right away.");

    public IReadOnlyList<TrickChoiceViewModel> Choices { get; }
    public bool IsEmpty => Choices.Count == 0;

    public override bool Resizable => true;
    public override double PreferredWidth => 640;
    public override double PreferredHeight => 520;

    [RelayCommand]
    private void Done() => Close(true);
}
