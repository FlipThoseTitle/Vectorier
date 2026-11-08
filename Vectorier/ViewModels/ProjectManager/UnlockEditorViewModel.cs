using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.ViewModels.ProjectManager;

/// <summary>
/// Highest "Stars Required" for an unlock: 3 for a track; 3 × the location's Story + Bonus levels for a location;
/// 3 × every Story + Bonus level of the project when there is no subject.
/// </summary>
public static class StarLimits
{
    public static int Max(LocationListService list, SubjectType subject, string? subjectName)
    {
        static int Count(LocationData location) =>
            (location.Groups.TryGetValue(LocationListService.StoryMode, out var story) ? story.Count : 0) +
            (location.Groups.TryGetValue(LocationListService.BonusMode, out var bonus) ? bonus.Count : 0);

        return subject switch
        {
            SubjectType.Track => 3,
            SubjectType.Location => 3 * (subjectName != null && list.FindLocation(subjectName) is { } loc ? Count(loc) : 0),
            _ => 3 * list.Locations.Sum(Count),
        };
    }
}

/// <summary>Edits unlock conditions (price, stars, subject) and reports every change.</summary>
public sealed partial class UnlockEditorViewModel : ObservableObject
{
    private readonly UnlockData _data;
    private readonly Action _changed;
    private readonly Func<IEnumerable<string>> _tracks;
    private readonly Func<IEnumerable<string>> _locations;
    private readonly Func<SubjectType, string?, int>? _maxStarsFunc;
    private bool _loading;

    public UnlockEditorViewModel(UnlockData data, Action changed, Func<IEnumerable<string>> tracks, Func<IEnumerable<string>> locations,
        Func<SubjectType, string?, int>? maxStars = null)
    {
        _data = data;
        _changed = changed;
        _tracks = tracks;
        _locations = locations;
        _maxStarsFunc = maxStars;

        _loading = true;
        UnlockPrice = Math.Max(0, data.UnlockPrice);
        StarsRequired = Math.Max(0, data.StarsRequired);
        Subject = data.Subject;
        RefreshSubjectNames();
        SubjectName = data.SubjectName;
        _loading = false;
        UpdateLimit();
    }

    public static IReadOnlyList<SubjectType> Subjects { get; } = Enum.GetValues<SubjectType>();

    [ObservableProperty] private int _unlockPrice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubject))]
    private int _starsRequired;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSubjectName))]
    private SubjectType _subject;

    [ObservableProperty] private string? _subjectName;

    /// <summary>Highest allowed Stars Required for the current subject.</summary>
    [ObservableProperty] private int _maxStars = int.MaxValue;

    [ObservableProperty] private string _starsHint = string.Empty;

    public ObservableCollection<string> SubjectNames { get; } = new();

    public bool ShowSubject => StarsRequired > 0;
    public bool ShowSubjectName => StarsRequired > 0 && Subject != SubjectType.None;

    partial void OnUnlockPriceChanged(int value)
    {
        if (value < 0)
        {
            UnlockPrice = 0;
            return;
        }
        if (_loading) return;
        _data.UnlockPrice = value;
        _changed();
    }

    partial void OnStarsRequiredChanged(int value)
    {
        if (_loading) return;
        int stars = Math.Clamp(value, 0, MaxStars);
        _data.StarsRequired = stars;
        if (stars != value)
        {
            _loading = true;
            StarsRequired = stars;
            _loading = false;
        }
        _changed();
    }

    partial void OnSubjectChanged(SubjectType value)
    {
        RefreshSubjectNames();
        if (_loading) return;
        _data.Subject = value;
        SubjectName = SubjectNames.Contains(_data.SubjectName) ? _data.SubjectName : SubjectNames.FirstOrDefault();
        UpdateLimit();
        _changed();
    }

    partial void OnSubjectNameChanged(string? value)
    {
        if (_loading || value == null) return;
        _data.SubjectName = value;
        UpdateLimit();
        _changed();
    }

    /// <summary>Recomputes the star limit and clamps the current value to it.</summary>
    private void UpdateLimit()
    {
        if (_maxStarsFunc == null)
        {
            MaxStars = Subject == SubjectType.Track ? 3 : int.MaxValue;
        }
        else
        {
            MaxStars = _maxStarsFunc(Subject, Subject == SubjectType.None ? null : SubjectName);
        }
        StarsHint = MaxStars == int.MaxValue ? string.Empty : Subject switch
        {
            SubjectType.Track => "0 to 3 stars for a track.",
            SubjectType.Location => Loc.F("At most {0} (3 × the location's Story and Bonus levels).", MaxStars),
            _ => Loc.F("At most {0} (3 × all Story and Bonus levels).", MaxStars),
        };
        if (StarsRequired > MaxStars)
            StarsRequired = MaxStars;
    }

    private void RefreshSubjectNames()
    {
        SubjectNames.Clear();
        var source = Subject switch
        {
            SubjectType.Track => _tracks(),
            SubjectType.Location => _locations(),
            _ => Enumerable.Empty<string>(),
        };
        foreach (var name in source)
            SubjectNames.Add(name);
    }
}
