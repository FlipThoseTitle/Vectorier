using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;

namespace Vectorier.ViewModels.Editor;

public partial class TriggerEditorViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTriggerMode), nameof(IsTemplateList), nameof(IsTemplateMode), nameof(IsForm), nameof(Title), nameof(HeaderText))]
    private TriggerEditorMode _mode = TriggerEditorMode.Trigger;

    public bool IsTriggerMode => Mode == TriggerEditorMode.Trigger;
    public bool IsTemplateList => Mode == TriggerEditorMode.TemplateList;
    public bool IsTemplateMode => Mode == TriggerEditorMode.Template;

    /// <summary>The loops / variables / When-Only If-Do form is shown (a trigger or a template).</summary>
    public bool IsForm => Mode != TriggerEditorMode.TemplateList;

    public string HeaderText => Mode == TriggerEditorMode.Trigger ? Loc.T("Trigger Editor") : Loc.T("Trigger Template Editing");

    /// <summary>TriggerTemplates.xml being edited (Edit Templates), null otherwise.</summary>
    public TriggerTemplateFile? TemplateFile { get; private set; }

    /// <summary>The template open in Template mode.</summary>
    public System.Xml.Linq.XElement? EditingTemplate { get; private set; }

    public bool TemplateIsLibrary => EditingTemplate != null && TriggerTemplateFile.IsLibrary(EditingTemplate);

    /// <summary>The template list (order, Library flags) or a template has changes not written to the file yet.</summary>
    [ObservableProperty] private bool _templatesDirty;

    // The trigger's form, kept while templates are edited.
    private TriggerData? _triggerData;
    private int _triggerSelectedLoop;
    private bool _triggerDirty;

    /// <summary>Edit Templates: reads {project}/xmlroot/TriggerTemplates.xml and shows its templates.</summary>
    [RelayCommand]
    private async Task EditTemplatesAsync()
    {
        TriggerTemplateFile file;
        try
        {
            file = await TriggerTemplateFile.LoadAsync(AppServices.FileSystem, TriggerTemplateFile.PathFor(Editor.Project));
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Templates"), Loc.T("TriggerTemplates.xml could not be read:\n") + ex.Message, MessageKind.Error);
            return;
        }
        TemplateFile = file;
        TemplatesDirty = false;
        _triggerData = Data;
        _triggerSelectedLoop = SelectedLoop;
        _triggerDirty = IsDirty;
        ShowXml = false;
        Mode = TriggerEditorMode.TemplateList;
        StructureChanged?.Invoke();
    }

    /// <summary>Opens a template of the list in the editing form.</summary>
    public void OpenTemplate(System.Xml.Linq.XElement template)
    {
        EditingTemplate = template;
        Data = TriggerData.Parse(TriggerTemplateFile.ContentOf(template), TriggerTemplateFile.IsLibrary(template));
        InferUsingTypes();
        SelectedLoop = Data.loops.Count > 0 ? 0 : -1;
        IsDirty = false;
        Mode = TriggerEditorMode.Template;
        OnPropertyChanged(nameof(Title));
        RaiseStructure();
    }

    /// <summary>Add Template: asks for a name and opens the new, empty template.</summary>
    public async Task AddTemplateAsync()
    {
        if (TemplateFile is not { } file)
            return;
        var names = file.Templates.Select(TriggerTemplateFile.NameOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string suggestion = "NewTemplate";
        for (int n = 2; names.Contains(suggestion); n++)
            suggestion = "NewTemplate" + n;
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Add Template"), Loc.T("Template name:"), suggestion,
            text => string.IsNullOrWhiteSpace(text) ? Loc.T("Enter a name.")
                : names.Contains(text.Trim()) ? Loc.T("A template with this name already exists.")
                : null,
            Loc.T("Add"));
        if (string.IsNullOrWhiteSpace(name))
            return;
        var template = file.Add(name.Trim());
        TemplatesDirty = true;
        OpenTemplate(template);
    }

    /// <summary>Removes a template from the list (after asking).</summary>
    public async Task RemoveTemplateAsync(System.Xml.Linq.XElement template)
    {
        if (TemplateFile is not { } file)
            return;
        string name = TriggerTemplateFile.NameOf(template);
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Remove Template"),
                Loc.F("Remove the template '{0}'? Triggers that use it will no longer find it.", name), Loc.T("Remove"), Loc.T("Cancel"), MessageKind.Warning))
            return;
        file.Remove(template);
        TemplatesDirty = true;
        StructureChanged?.Invoke();
    }

    /// <summary>Moves a template up (-1) or down (+1) in the list.</summary>
    public void MoveTemplate(System.Xml.Linq.XElement template, int direction)
    {
        if (TemplateFile?.Move(template, direction) == true)
        {
            TemplatesDirty = true;
            StructureChanged?.Invoke();
        }
    }

    /// <summary>Library templates are building blocks for other templates: they have no &lt;Init&gt;.</summary>
    public void SetTemplateLibrary(System.Xml.Linq.XElement template, bool library)
    {
        if (TriggerTemplateFile.IsLibrary(template) == library)
            return;
        TriggerTemplateFile.SetLibrary(template, library);
        if (library)
            template.Elements("Init").Remove();
        TemplatesDirty = true;
        StructureChanged?.Invoke();
    }

    /// <summary>Writes the open template into the file (in memory).</summary>
    private void CommitTemplate()
    {
        if (EditingTemplate == null)
            return;
        Data.omitInit = TemplateIsLibrary;
        TriggerTemplateFile.SetContent(EditingTemplate, Data.ToXml());
        IsDirty = false;
        TemplatesDirty = true;
    }

    /// <summary>Apply in template modes: writes TriggerTemplates.xml and re-reads the templates.</summary>
    public async Task<bool> SaveTemplatesAsync()
    {
        if (TemplateFile == null)
            return false;
        if (IsTemplateMode)
            CommitTemplate();
        try
        {
            await TemplateFile.SaveAsync(AppServices.FileSystem);
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Edit Templates"), Loc.T("TriggerTemplates.xml could not be saved:\n") + ex.Message, MessageKind.Error);
            return false;
        }
        TemplatesDirty = false;
        Editor.StatusText = Loc.T("Saved TriggerTemplates.xml");
        await ReloadTemplatesAsync();
        return true;
    }

    /// <summary>Apply button: the trigger, or TriggerTemplates.xml in template modes.</summary>
    [RelayCommand]
    private async Task ApplyCurrentAsync()
    {
        if (IsTriggerMode) Apply();
        else await SaveTemplatesAsync();
    }

    /// <summary>Back: from a template to the list, from the list to the trigger (asking about unsaved changes).</summary>
    [RelayCommand]
    private async Task BackAsync()
    {
        if (IsTemplateMode)
        {
            if (IsDirty)
            {
                var choice = await AppServices.Dialogs.AskSaveChangesAsync(TriggerTemplateFile.NameOf(EditingTemplate!) + " (template)");
                if (choice == SaveChoice.Cancel) return;
                if (choice == SaveChoice.Save && !await SaveTemplatesAsync()) return;
            }
            EditingTemplate = null;
            IsDirty = false;
            Mode = TriggerEditorMode.TemplateList;
            StructureChanged?.Invoke();
            return;
        }
        if (IsTemplateList)
        {
            if (!await ResolveTemplateChangesAsync())
                return;
            LeaveTemplates();
        }
    }

    /// <summary>Unsaved template changes: Save writes the file, Don't Save drops them. False when cancelled.</summary>
    private async Task<bool> ResolveTemplateChangesAsync()
    {
        if (!TemplatesDirty && !(IsTemplateMode && IsDirty))
            return true;
        var choice = await AppServices.Dialogs.AskSaveChangesAsync("TriggerTemplates.xml");
        if (choice == SaveChoice.Cancel) return false;
        if (choice == SaveChoice.Save) return await SaveTemplatesAsync();
        return true;
    }

    private void LeaveTemplates()
    {
        TemplateFile = null;
        EditingTemplate = null;
        TemplatesDirty = false;
        if (_triggerData != null)
        {
            Data = _triggerData;
            SelectedLoop = Math.Clamp(_triggerSelectedLoop, -1, Data.loops.Count - 1);
            IsDirty = _triggerDirty;
        }
        _triggerData = null;
        Mode = TriggerEditorMode.Trigger;
        RaiseStructure();
    }

    // ---- <Using> (template loops) ----

    public void AddUsing(TLoop loop)
    {
        string n = "NewVariable";
        int k = 1;
        var names = Data.VarNames();
        while (names.Contains(n)) n = "NewVariable" + (++k);
        loop.usings.Add(new TUsing { name = n, type = "String" });
        Changed();
    }

    public void RemoveUsing(TLoop loop, TUsing u)
    {
        loop.usings.Remove(u);
        Changed();
    }

    /// <summary>Renames a &lt;Using&gt; variable and the references to it in its loop.</summary>
    public void RenameUsing(TLoop loop, TUsing u, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == u.name || loop.usings.Any(x => x != u && x.name == newName)) return;
        string old = u.name;
        u.name = newName;
        foreach (var e in loop.events) foreach (var p in e.ps) RenameIn(p.v, old, newName);
        foreach (var c in loop.conds) RenameInCond(c, old, newName);
        foreach (var a in loop.acts) RenameInAct(a, old, newName);
        Changed();
    }

    /// <summary>Using types written in other spellings (integer, boolean...) are shown with the editor's names.</summary>
    private void InferUsingTypes()
    {
        foreach (var loop in Data.loops)
            foreach (var u in loop.usings)
                if (string.IsNullOrEmpty(u.type)) u.type = "String";
    }

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _showXml;
    [ObservableProperty] private string _xmlPreview = string.Empty;
    [ObservableProperty] private string _loopSearch = string.Empty;

    /// <summary>Index of the loop shown in the main panel, -1 for none.</summary>
    [ObservableProperty] private int _selectedLoop = -1;

    public ObservableCollection<string> Presets { get; } = new() { "Default" };

    [ObservableProperty] private int _presetIndex;

    public bool IsCustomPreset => PresetIndex > 0;

    /// <summary>Raised when the shape of the form changes (loops, entries, modes): the view rebuilds.</summary>
    public event Action? StructureChanged;

    // ---- Dropdown sources (Unity: ValueSources) ----
    public List<string> Models { get; private set; } = new();
    public List<string> Spawns { get; private set; } = new();
    public List<string> Animations { get; private set; } = new();
    public List<string> Sounds { get; private set; } = new();

    /// <summary>Move name → its .bin (moves.xml FileName).</summary>
    public Dictionary<string, string> MoveFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Full path of a sound by its name (any extension, any sub folder), or null.</summary>
    public async Task<string?> FindSoundAsync(string name)
    {
        var fs = AppServices.FileSystem;
        string folder = VPath.Combine(Editor.Project.Path, "sounds");
        foreach (var file in await fs.GetFilesAsync(folder, recursive: true))
            if (string.Equals(VPath.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        return null;
    }

    /// <summary>
    /// Opens the character preview on a move (its .bin from moves.xml), starting at <paramref name="startFrame"/>.
    /// Returns the trim when Apply is pressed.
    /// </summary>
    public async Task<ViewModels.Preview.CharacterPreviewResult?> PreviewMoveAsync(string move, int startFrame)
    {
        if (!MoveFiles.TryGetValue(move, out var file))
        {
            await AppServices.Dialogs.ShowMessageAsync(Loc.T("Preview Animation"), Loc.F("'{0}' has no FileName in moves.xml.", move), MessageKind.Warning);
            return null;
        }
        return await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(Editor.Project, Loc.F("Preview - {0} ({1})", move, file),
            new[] { Services.Assets.CharacterModel.DefaultSkin }, ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(Editor.Project, file), startFrame, showApply: true, applyText: Loc.F("Apply {0}", move));
    }

    public IList<string> ModelChoices => Models.Count > 0 ? Models : new[] { VTypes.NoModel };

    private async Task InitializeAsync()
    {
        RefreshSceneSources();
        await LoadProjectSourcesAsync();
        if (!Library.Available && !Library.LoadFailed)
            await ReloadTemplatesAsync();
        else
        {
            InferTypesFromTemplates();
            RaiseStructure();
        }
        await RefreshPresetsAsync();
    }

    partial void OnSelectedLoopChanged(int value) => StructureChanged?.Invoke();

    partial void OnShowXmlChanged(bool value) => UpdateXml();

    partial void OnPresetIndexChanged(int value) => OnPropertyChanged(nameof(IsCustomPreset));

    partial void OnLoopSearchChanged(string value) => StructureChanged?.Invoke();

    /// <summary>A value changed: marks the trigger as having unapplied changes.</summary>
    public void Touch()
    {
        IsDirty = true;
        UpdateXml();
    }

    /// <summary>The form must be rebuilt (entry added/removed, a mode changed...).</summary>
    public void RaiseStructure()
    {
        UpdateXml();
        StructureChanged?.Invoke();
    }

    public void Changed()
    {
        Touch();
        StructureChanged?.Invoke();
    }

    private void UpdateXml()
    {
        if (ShowXml)
            XmlPreview = Data.ToXml();
    }
}
