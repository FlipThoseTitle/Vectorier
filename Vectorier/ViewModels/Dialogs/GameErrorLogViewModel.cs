using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Gameplay.Diagnostics;
using Vectorier.Services;
using Vectorier.Services.Game;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>One report in the Game Error Log's list.</summary>
public sealed class GameErrorItem
{
    public GameErrorItem(GameErrorReport report) => Report = report;

    public GameErrorReport Report { get; }

    public bool IsWarning => Report.Severity == GameErrorSeverity.Warning;
    public bool IsError => Report.Severity == GameErrorSeverity.Error;
    public bool IsCrash => Report.Severity == GameErrorSeverity.Crash;

    public string Time => Report.Time.ToString("HH:mm:ss");
    public string Kind => Report.Severity switch
    {
        GameErrorSeverity.Warning => Loc.T("Warning"),
        GameErrorSeverity.Crash => Loc.T("Crash"),
        _ => Loc.T("Error"),
    };
    public string Summary => Report.Summary;

    // The details, each shown only when known.
    public string Stage => Report.Stage;
    public string Problem => Report.Problem;
    public string? File => Report.File;
    public string? Position => Report.Line > 0 ? Loc.F("Line {0}, column {1}", Report.Line, Report.Column) : null;
    public string? Element => Report.ElementPath;
    public string? UsedBy => Report.UsedBy;
    public bool HasUsedBy => UsedBy != null;
    public string? Attribute => Report.Attribute == null ? null
        : Report.Value != null ? $"{Report.Attribute} = \"{Report.Value}\"" : Report.Attribute;
    public string? Code => Report.CodeLocation == null ? null
        : Report.ExceptionType != null ? $"{Report.CodeLocation} ({Report.ExceptionType})" : Report.CodeLocation;
    public string? Details => Report.Details;

    public bool HasFile => File != null;
    public bool HasPosition => Position != null;
    public bool HasElement => Element != null;
    public bool HasAttribute => Attribute != null;
    public bool HasCode => Code != null;
    public bool HasDetails => !string.IsNullOrEmpty(Details);
}

/// <summary>
/// The game's errors: shown when a level cannot be played or the game stops (that report selected, with the
/// message at the top), and from File &gt; Game Error Log. Lists this session's warnings, errors and crashes with
/// where each comes from (file, line, element, attribute) and the technical details; the Crash Log tab shows the
/// crash log file, which keeps earlier sessions too.
/// </summary>
public sealed partial class GameErrorLogViewModel : DialogViewModel
{
    public GameErrorLogViewModel(string? title = null, string? headline = null, GameErrorReport? select = null)
    {
        Title = title ?? Loc.T("Game Error Log");
        Headline = headline;
        Refresh(select);
        CanOpenFolder = AppServices.Platform?.CanShowInExplorer == true
            && AppServices.FileSystem?.GetNativePath(GameErrorLogs.Folder) != null;
        LogLocation = Loc.F("Saved in {0}", GameErrorLogs.Folder + "/" + GameErrorLogs.ErrorFile + ", " + GameErrorLogs.CrashFile);
        _ = LoadCrashLogAsync();
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 880;
    public override double PreferredHeight => 600;

    /// <summary>What happened, when opened for a failure (e.g. "The level could not be played.").</summary>
    public string? Headline { get; }
    public bool HasHeadline => !string.IsNullOrEmpty(Headline);

    public ObservableCollection<GameErrorItem> Items { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private GameErrorItem? _selected;

    public bool HasSelection => Selected != null;
    public bool IsEmpty => Items.Count == 0;

    [ObservableProperty] private string _crashLog = string.Empty;
    public bool HasCrashLog => CrashLog.Length > 0;

    partial void OnCrashLogChanged(string value) => OnPropertyChanged(nameof(HasCrashLog));

    public bool CanOpenFolder { get; }
    public string LogLocation { get; }

    private void Refresh(GameErrorReport? select)
    {
        Items.Clear();
        foreach (var report in GameLog.Reports.Reverse()) // newest first
            Items.Add(new GameErrorItem(report));
        Selected = (select != null ? Items.FirstOrDefault(i => ReferenceEquals(i.Report, select)) : null)
            ?? Items.FirstOrDefault(i => !i.IsWarning) ?? Items.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadCrashLogAsync()
    {
        string text = await GameErrorLogs.ReadAsync(GameErrorSeverity.Crash);
        CrashLog = text.TrimEnd();
    }

    [RelayCommand]
    private async Task Copy()
    {
        var clipboard = AppServices.Shell?.CurrentTopLevel?.Clipboard;
        if (clipboard == null || Selected == null)
            return;
        await clipboard.SetTextAsync(Selected.Report.ToText());
    }

    [RelayCommand]
    private async Task CopyAll()
    {
        var clipboard = AppServices.Shell?.CurrentTopLevel?.Clipboard;
        if (clipboard == null || Items.Count == 0)
            return;
        await clipboard.SetTextAsync(string.Join(Environment.NewLine, Items.Select(i => i.Report.ToText())));
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (AppServices.FileSystem is not { } fs)
            return;
        string folder = GameErrorLogs.Folder;
        // The folder exists once something was logged; otherwise the Editor folder.
        if (fs.GetNativePath(folder) is { } native && !System.IO.Directory.Exists(native))
            folder = "Editor";
        AppServices.Platform.ShowInExplorer(fs, folder);
    }

    /// <summary>This session's list is cleared (the log files keep everything).</summary>
    [RelayCommand]
    private void Clear()
    {
        GameLog.Clear();
        Refresh(null);
    }

    [RelayCommand]
    private void CloseDialog() => Close(null);
}
