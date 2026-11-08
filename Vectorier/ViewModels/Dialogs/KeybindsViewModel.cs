using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Dialogs;

/// <summary>Part of a shortcut: a key drawn as a key cap, or plain text between them ("+", "/", "drag").</summary>
public sealed record KeyPart(string Text, bool IsKey);

/// <summary>One shortcut: its keys and what it does.</summary>
public sealed class KeybindRow
{
    private static readonly Regex Keys = new("«([^»]+)»", RegexOptions.Compiled);

    public KeybindRow(string keys, string action)
    {
        Action = action;
        var parts = new List<KeyPart>();
        int at = 0;
        foreach (Match m in Keys.Matches(keys))
        {
            if (m.Index > at && keys[at..m.Index].Trim() is { Length: > 0 } text)
                parts.Add(new KeyPart(text, false));
            parts.Add(new KeyPart(m.Groups[1].Value, true));
            at = m.Index + m.Length;
        }
        if (at < keys.Length && keys[at..].Trim() is { Length: > 0 } rest)
            parts.Add(new KeyPart(rest, false));
        Parts = parts;
        SearchText = (string.Concat(parts.Select(p => p.Text + " ")) + action).ToLowerInvariant();
    }

    public IReadOnlyList<KeyPart> Parts { get; }
    public string Action { get; }
    public string SearchText { get; }
}

public sealed class KeybindSection
{
    public KeybindSection(string title, IReadOnlyList<KeybindRow> rows)
    {
        Title = title;
        Rows = rows;
    }

    public string Title { get; }
    public IReadOnlyList<KeybindRow> Rows { get; }
}

/// <summary>
/// Help › Keybinds: every shortcut, grouped by where it is used. The groups are listed on the left (one at a time,
/// or all of them), and the search box finds a shortcut by its keys or by what it does.
/// </summary>
public sealed partial class KeybindsViewModel : DialogViewModel
{
    private readonly List<KeybindSection> _sections;

    public KeybindsViewModel()
    {
        Title = Loc.T("Keybinds");
        _sections = Build();
        Categories = new[] { Loc.T("All") }.Concat(_sections.Select(s => s.Title)).ToList();
        _selectedCategory = Categories[0];
        Refresh();
    }

    public override bool Resizable => true;
    public override double PreferredWidth => 760;
    public override double PreferredHeight => 560;

    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty] private string _selectedCategory;
    [ObservableProperty] private string _searchText = string.Empty;

    public ObservableCollection<KeybindSection> Shown { get; } = new();

    public bool NothingFound => Shown.Count == 0;

    partial void OnSelectedCategoryChanged(string value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();

    private void Refresh()
    {
        string search = (SearchText ?? string.Empty).Trim().ToLowerInvariant();
        bool all = SelectedCategory == null || SelectedCategory == Categories[0] || search.Length > 0;
        Shown.Clear();
        foreach (var section in _sections)
        {
            if (!all && section.Title != SelectedCategory)
                continue;
            var rows = search.Length == 0 ? section.Rows : section.Rows.Where(r => r.SearchText.Contains(search)).ToList();
            if (rows.Count > 0)
                Shown.Add(new KeybindSection(section.Title, rows));
        }
        OnPropertyChanged(nameof(NothingFound));
    }

    [RelayCommand]
    private void CloseDialog() => Close(null);

    // Keys in «» are drawn as key caps; the rest is plain text.
    private static List<KeybindSection> Build()
    {
        static KeybindSection Section(string title, params (string Keys, string Action)[] rows) =>
            new(Loc.T(title), rows.Select(r => new KeybindRow(r.Keys, Loc.T(r.Action))).ToList());
        string drag = Loc.T("drag"), click = Loc.T("click"), held = Loc.T("held");

        return new List<KeybindSection>
        {
            Section("Scene View",
                ("«RMB» " + drag, "Pan"),
                ("«MMB» " + drag, "Pan"),
                ("«Space» + «LMB» " + drag, "Pan"),
                ("«Alt» + «RMB» " + drag, "Zoom"),
                ("«" + Loc.T("Mouse wheel") + "»", "Zoom at the pointer"),
                ("«Shift» + " + drag, "Pan faster"),
                ("«F»", "Frame the selection"),
                ("«RMB» " + click, "Create menu")),
            Section("Selection",
                ("«LMB» " + click, "Select"),
                ("«Ctrl» / «Shift» + «LMB»", "Add to / remove from the selection"),
                ("«" + Loc.T("Double click") + "»", "Select the next element underneath"),
                ("«MMB» " + click, "Select the element's root Object"),
                ("«Ctrl» + «A»", "Select all"),
                ("«Ctrl» + «Shift» + «D» / «Esc»", "Clear the selection"),
                ("«H»", "Hide / show the selection"),
                ("«L»", "Lock / unlock the selection (everything when nothing is selected)")),
            Section("Tools",
                ("«Q» / «W» / «E» / «T» / «B»", "Move / Rotate / Scale / Rect / Brush tool"),
                ("«G» / «R» / «S»", "Move / rotate / scale with the pointer (click confirms, right click or Esc cancels)"),
                ("«X» / «Y»", "Lock G / R / S to an axis"),
                ("«Ctrl»", "Snap rotation by 15°"),
                ("«Z»", "Pivot / Center"),
                ("«X»", "Global / Local"),
                ("«" + Loc.T("Arrow keys") + "» (+ «Shift»)", "Nudge by 1 (10)"),
                ("«[» / «]»", "Order in Layer (brush size with the Brush)"),
                ("«1» … «0», «Shift» + «1» … «3»", "Pick an Element Bar entry")),
            Section("Snapping",
                ("«V» " + held, "Snap to everything"),
                ("«C» " + held, "Snap to Platforms"),
                ("«A» " + Loc.T("while snapping"), "Scale instead of move")),
            Section("Editing",
                ("«Ctrl» + «Z» / «Ctrl» + «Y»", "Undo / Redo"),
                ("«Ctrl» + «C» / «X» / «V»", "Copy / Cut / Paste"),
                ("«Ctrl» + «Shift» + «V»", "Paste into the selected Object"),
                ("«Ctrl» + «D»", "Duplicate (while dragging or G / R / S: keep moving the copies)"),
                ("«Alt» + " + drag, "Duplicate and drag (keep Alt held to stamp more)"),
                ("«Ctrl» + «G» / «Ctrl» + «Shift» + «G»", "Group / Ungroup"),
                ("«Delete»", "Delete")),
            Section("File",
                ("«Ctrl» + «N» / «Ctrl» + «O»", "New / Open level"),
                ("«Ctrl» + «S» / «Ctrl» + «Shift» + «S»", "Save / Save As"),
                ("«F5» / «Shift» + «F5»", "Play level / Play in Hunter Mode")),
            Section("Dynamic Editor (timeline focused)",
                ("«Space»", "Play / Pause"),
                ("«" + Loc.T("Arrow keys") + "» (+ «Shift»)", "Previous / next frame (10 frames)"),
                ("«Ctrl» + «C» / «X» / «V» / «D»", "Copy / Cut / Paste / Duplicate keys"),
                ("«Delete»", "Delete keys")),
        };
    }
}
