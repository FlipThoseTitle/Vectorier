using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Triggers;
using Vectorier.Services.Xml;
using Vectorier.Services.Localization;

namespace Vectorier.ViewModels.Editor;

/// <summary>What the Trigger Editor shows: the trigger, the list of TriggerTemplates.xml, or one template.</summary>
public enum TriggerEditorMode
{
    Trigger,
    TemplateList,
    Template,
}

/// <summary>
/// Trigger Editor (Unity: Scripts/Trigger/TriggerEditor.cs). Edits a Trigger's Content as loops with
/// When / Only If / Do sections and a Variables panel; Apply writes the XML back to the Trigger.
/// Opens in the center of the editor and stays until closed.
/// </summary>
public sealed partial class TriggerEditorViewModel : ViewModelBase
{
    public const string PresetFolder = "Editor/TriggerPresets";
    private static readonly Dictionary<string, TriggerTemplateLibrary> Libraries = new(StringComparer.Ordinal);

    private readonly EditorViewModel _editor;

    public TriggerEditorViewModel(EditorViewModel editor, TriggerElement trigger)
    {
        _editor = editor;
        Trigger = trigger;
        if (trigger.VariableTypes.Count == 0 && editor.FindCachedTriggerTypes(trigger) is { } cached)
            trigger.VariableTypes = new Dictionary<string, string>(cached);
        Data = TriggerData.Parse(trigger.ContentXml);
        ReadTypes(Data, trigger.VariableTypes);
        SelectedLoop = Data.loops.Count > 0 ? 0 : -1;
        Library = Libraries.TryGetValue(editor.Project.Path, out var lib) ? lib : new TriggerTemplateLibrary();
        _ = InitializeAsync();
    }

    public EditorViewModel Editor => _editor;
    public TriggerElement Trigger { get; private set; }
    public TriggerData Data { get; private set; }
    public TriggerTemplateLibrary Library { get; private set; }

    public string Title => Mode switch
    {
        TriggerEditorMode.TemplateList => "TriggerTemplates.xml",
        TriggerEditorMode.Template when EditingTemplate != null => TriggerTemplateFile.NameOf(EditingTemplate) + (TemplateIsLibrary ? "  (" + Loc.T("Library") + ")" : string.Empty),
        _ => string.IsNullOrEmpty(Trigger.Name) ? Loc.T("Trigger") : Trigger.Name,
    };

    // ================= MODES: TRIGGER / TEMPLATE LIST / TEMPLATE ================= //

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
            file = await TriggerTemplateFile.LoadAsync(AppServices.FileSystem, TriggerTemplateFile.PathFor(_editor.Project));
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
        _editor.StatusText = Loc.T("Saved TriggerTemplates.xml");
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
        string folder = VPath.Combine(_editor.Project.Path, "sounds");
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
        return await ViewModels.Preview.CharacterPreviewViewModel.ShowAsync(_editor.Project, Loc.F("Preview - {0} ({1})", move, file),
            new[] { Services.Assets.CharacterModel.DefaultSkin }, ViewModels.Preview.CharacterPreviewViewModel.AnimationPath(_editor.Project, file),
            ViewModels.Preview.PreviewCamera.FollowCameraNode, startFrame, showApply: true, applyText: Loc.F("Apply {0}", move));
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

    // ================= SOURCES ================= //

    private void RefreshSceneSources()
    {
        var settings = _editor.Document.Settings;
        Models = settings.CommonModels.Concat(settings.HunterModels)
            .Select(m => m.Get("Name")).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        Spawns = _editor.Document.AllElements().OfType<SpawnElement>().Select(s => s.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private async Task LoadProjectSourcesAsync()
    {
        var fs = AppServices.FileSystem;
        var project = _editor.Project;
        try
        {
            string moves = VPath.Combine(project.AnimationsFolder, "moves.xml");
            var list = new List<string>();
            if (await fs.FileExistsAsync(moves))
            {
                var doc = XmlHelpers.ParseDocument(await fs.ReadAllBytesAsync(moves));
                var movesEl = doc.Root?.Element("Moves");
                if (movesEl != null)
                {
                    list.AddRange(movesEl.Elements().Select(e => e.Name.LocalName).Distinct());
                    MoveFiles.Clear();
                    foreach (var move in movesEl.Elements())
                        if ((string?)move.Attribute("FileName") is { Length: > 0 } file)
                            MoveFiles.TryAdd(move.Name.LocalName, file);
                }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            Animations = list;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Trigger Editor] moves.xml: " + ex.Message);
        }

        try
        {
            string folder = VPath.Combine(project.Path, "sounds");
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in await fs.GetFilesAsync(folder, recursive: true))
            {
                string name = VPath.GetFileName(file);
                if (name.StartsWith('.')) continue;
                string stem = VPath.GetFileNameWithoutExtension(file);
                if (!string.IsNullOrEmpty(stem)) set.Add(stem);
            }
            Sounds = set.ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("[Trigger Editor] sounds: " + ex.Message);
        }
    }

    /// <summary>Re-reads TriggerTemplates.xml and the dropdown sources.</summary>
    [RelayCommand]
    public async Task ReloadTemplatesAsync()
    {
        var library = new TriggerTemplateLibrary();
        await library.LoadAsync(AppServices.FileSystem, VPath.Combine(_editor.Project.XmlRoot, "TriggerTemplates.xml"));
        Library = library;
        Libraries[_editor.Project.Path] = library;
        RefreshSceneSources();
        await LoadProjectSourcesAsync();
        InferTypesFromTemplates();
        RaiseStructure();
    }

    // ================= VARIABLE TYPES ================= //

    // Types are not in the XML; they are kept on the side (Trigger.VariableTypes / preset files).
    private static void ReadTypes(TriggerData d, IReadOnlyDictionary<string, string>? types)
    {
        if (types == null) return;
        foreach (var v in d.vars)
        {
            if (v.locked) continue;
            if (types.TryGetValue(v.name, out var t)) v.type = VTypes.FromName(t);
        }
    }

    private static Dictionary<string, string> WriteTypes(TriggerData d)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var v in d.vars)
            if (!v.locked && !string.IsNullOrEmpty(v.name) && v.type != VType.String)
                map[v.name] = v.type.ToString();
        return map;
    }

    /// <summary>Variables still typed as String get the type a referenced template declares for them.</summary>
    private void InferTypesFromTemplates()
    {
        if (!Library.Available) return;
        var roots = new List<TriggerTemplateLibrary.LoopInfo>();
        foreach (var loop in Data.loops)
        {
            if (loop.kind == LoopKind.FullTemplate && Library.FindTemplate(loop.template) is { } t) roots.AddRange(t.loops);
            else if (loop.kind == LoopKind.TemplateLoop && Library.Find(loop.template) is { } l) roots.Add(l);
            foreach (var e in loop.events.Where(e => e.kind == EvKind.Block)) if (Library.Find(e.template) is { } el) roots.Add(el);
            foreach (var c in loop.conds.Where(c => c.kind == CondKind.Block)) if (Library.Find(c.template) is { } cl) roots.Add(cl);
            foreach (var a in loop.acts.Where(a => a.kind == ActKind.Block)) if (Library.Find(a.template) is { } al) roots.Add(al);
        }
        var uses = Library.CollectUses(roots, "");
        foreach (var v in Data.vars)
        {
            if (v.locked || v.type != VType.String || Trigger.VariableTypes.ContainsKey(v.name)) continue;
            var u = uses.FirstOrDefault(x => x.name == v.name && !string.IsNullOrEmpty(x.type));
            if (u != null) v.type = u.vtype;
        }
    }

    // ================= APPLY / CLOSE ================= //

    [RelayCommand]
    public void Apply()
    {
        Trigger = _editor.Document.FindById(Trigger.Id) as TriggerElement ?? Trigger;
        Trigger.ContentXml = Data.ToXml();
        Trigger.VariableTypes = WriteTypes(Data);
        IsDirty = false;
        _editor.StatusText = Loc.F("Applied trigger '{0}'", Title);
        _ = _editor.CacheTriggerTypesAsync(Trigger);
    }

    [RelayCommand]
    private async Task CloseEditor()
    {
        if (!IsTriggerMode)
        {
            if (!await ResolveTemplateChangesAsync())
                return;
            LeaveTemplates();
        }
        if (IsDirty)
        {
            var choice = await AppServices.Dialogs.AskSaveChangesAsync(Title + " (trigger)");
            if (choice == SaveChoice.Cancel) return;
            if (choice == SaveChoice.Save) Apply();
        }
        _editor.CloseTriggerEditor();
    }

    public void Close() { }

    // ================= LOOPS ================= //

    [RelayCommand]
    private void AddLoop()
    {
        Data.loops.Add(new TLoop());
        SelectedLoop = Data.loops.Count - 1;
        Changed();
    }

    [RelayCommand]
    private async Task DeleteLoop()
    {
        if (SelectedLoop < 0 || SelectedLoop >= Data.loops.Count) return;
        var loop = Data.loops[SelectedLoop];
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete loop"), Loc.F("Delete '{0}'?", loop.Display), Loc.T("Delete"))) return;
        Data.loops.RemoveAt(SelectedLoop);
        SelectedLoop = Math.Clamp(SelectedLoop, -1, Data.loops.Count - 1);
        Changed();
    }

    public void MoveLoop(int from, int to)
    {
        if (from < 0 || from >= Data.loops.Count) return;
        to = Math.Clamp(to, 0, Data.loops.Count);
        var item = Data.loops[from];
        Data.loops.RemoveAt(from);
        int target = to > from ? to - 1 : to;
        Data.loops.Insert(target, item);
        SelectedLoop = target;
        Changed();
    }

    // ================= VARIABLES ================= //

    [RelayCommand]
    private void AddVariable()
    {
        string n = "NewVariable";
        int k = 1;
        while (Data.vars.Exists(x => x.name == n)) n = "NewVariable" + (++k);
        Data.vars.Add(new TVar(n, "", VType.String));
        Changed();
    }

    public void RemoveVariable(TVar v)
    {
        Data.vars.Remove(v);
        Changed();
    }

    /// <summary>Renames a variable and every reference to it.</summary>
    public void RenameVariable(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
        if (Data.vars.Exists(x => x.name == newName)) return;
        var v = Data.vars.Find(x => x.name == oldName);
        if (v == null) return;
        v.name = newName;
        foreach (var L in Data.loops)
        {
            foreach (var e in L.events) foreach (var p in e.ps) RenameIn(p.v, oldName, newName);
            foreach (var c in L.conds) RenameInCond(c, oldName, newName);
            foreach (var a in L.acts) RenameInAct(a, oldName, newName);
        }
        Changed();
    }

    private static void RenameIn(TValue v, string o, string n)
    {
        if (v == null) return;
        if (v.mode == ValueMode.Variable && v.varName == o) v.varName = n;
        if (v.modelIsVar && v.model == o) v.model = n;
        if (v.nodeIsVar && v.node == o) v.node = n;
        if (v.mode == ValueMode.Literal && v.text == o) v.text = n;
    }

    private static void RenameInCond(TCond c, string o, string n)
    {
        RenameIn(c.a, o, n); RenameIn(c.b, o, n);
        RenameIn(c.selObject, o, n); RenameIn(c.selFrom, o, n);
        foreach (var k in c.children) RenameInCond(k, o, n);
    }

    private static void RenameInAct(TAct a, string o, string n)
    {
        foreach (var p in a.ps) RenameIn(p.v, o, n);
        foreach (var k in a.children) RenameInAct(k, o, n);
    }

    public void AddMissingVariables(IEnumerable<TriggerTemplateLibrary.UsingVar> uses)
    {
        foreach (var u in uses)
            if (!Data.vars.Exists(x => x.name == u.name))
                Data.vars.Add(new TVar(u.name, VTypes.Coerce(u.vtype, u.def, Models), u.vtype));
        Changed();
    }

    // ================= ENTRIES ================= //

    public static TEvent NewEvent(EntryDef def)
    {
        var e = new TEvent { id = def.id };
        foreach (var p in def.ps) e.ps.Add(new TParam(p.attr, TValue.Parse(p.def)));
        return e;
    }

    public static TAct NewAction(EntryDef def)
    {
        var a = new TAct { kind = ActKind.Action, id = def.id };
        foreach (var p in def.ps) a.ps.Add(new TParam(p.attr, TValue.Parse(p.def)));
        return a;
    }

    // ================= PRESETS ================= //

    private async Task RefreshPresetsAsync()
    {
        string current = PresetIndex > 0 && PresetIndex < Presets.Count ? Presets[PresetIndex] : "Default";
        var names = new List<string>();
        try
        {
            foreach (var file in await AppServices.FileSystem.GetFilesAsync(PresetFolder))
                if (VPath.HasExtension(file, ".xml"))
                    names.Add(VPath.GetFileNameWithoutExtension(file));
        }
        catch
        {
            // no preset folder yet
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        Presets.Clear();
        Presets.Add("Default");
        foreach (var n in names) Presets.Add(n);
        PresetIndex = Math.Max(0, Presets.IndexOf(current));
    }

    private static string PresetPath(string name) => VPath.Combine(PresetFolder, name + ".xml");

    /// <summary>Default = the trigger's own content; other entries replace the form with a saved preset.</summary>
    public async Task LoadPresetAsync(int index)
    {
        if (index == PresetIndex && index != 0) return;
        if (IsDirty && !await AppServices.Dialogs.ConfirmAsync(Loc.T("Unapplied changes"), Loc.T("Loading a preset replaces everything on screen. Continue?"), Loc.T("Load")))
        {
            OnPropertyChanged(nameof(PresetIndex));
            return;
        }

        if (index <= 0)
        {
            Data = TriggerData.Parse(Trigger.ContentXml);
            ReadTypes(Data, Trigger.VariableTypes);
            IsDirty = false;
        }
        else
        {
            try
            {
                var doc = XmlHelpers.ParseDocument(await AppServices.FileSystem.ReadAllBytesAsync(PresetPath(Presets[index])));
                var content = doc.Root?.Element("Content");
                string inner = content == null ? string.Empty : XmlHelpers.FormatFragment(content.Nodes());
                Data = TriggerData.Parse(inner);
                var types = doc.Root?.Element("VariableTypes")?.Elements("Variable")
                    .ToDictionary(v => (string?)v.Attribute("Name") ?? string.Empty, v => (string?)v.Attribute("Type") ?? "String");
                ReadTypes(Data, types);
                IsDirty = true;
            }
            catch (Exception ex)
            {
                await AppServices.Dialogs.ShowMessageAsync(Loc.T("Preset"), Loc.T("The preset could not be read:\n") + ex.Message, MessageKind.Error);
                return;
            }
        }
        PresetIndex = index;
        SelectedLoop = Data.loops.Count > 0 ? 0 : -1;
        RaiseStructure();
    }

    private async Task WritePresetAsync(string name)
    {
        var root = new XElement("TriggerPreset");
        var types = new XElement("VariableTypes");
        foreach (var (n, t) in WriteTypes(Data))
            types.Add(new XElement("Variable", new XAttribute("Name", n), new XAttribute("Type", t)));
        root.Add(types);
        var content = new XElement("Content");
        foreach (var node in XmlHelpers.ParseFragment(Data.ToXml()))
            content.Add(node);
        root.Add(content);
        await AppServices.FileSystem.CreateDirectoryAsync(PresetFolder);
        await AppServices.FileSystem.WriteAllBytesAsync(PresetPath(name), XmlHelpers.ToBytes(new XDocument(root)));
    }

    private static string? ValidatePresetName(string text, IEnumerable<string> existing)
    {
        if (string.IsNullOrWhiteSpace(text)) return Loc.T("Enter a name.");
        if (text.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase)) return Loc.T("'Default' is reserved.");
        if (text.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || text.Contains('/')) return Loc.T("The name contains invalid characters.");
        if (existing.Any(e => e.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase))) return Loc.T("A preset with this name already exists.");
        return null;
    }

    [RelayCommand]
    private async Task NewPreset()
    {
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("New Preset"), Loc.T("Save the current trigger as a preset named:"), "NewPreset",
            t => ValidatePresetName(t, Presets));
        if (name == null) return;
        await WritePresetAsync(name);
        await RefreshPresetsAsync();
        PresetIndex = Math.Max(0, Presets.IndexOf(name));
    }

    [RelayCommand]
    private async Task SavePreset()
    {
        if (PresetIndex <= 0) return;
        await WritePresetAsync(Presets[PresetIndex]);
        _editor.StatusText = Loc.F("Saved preset '{0}'", Presets[PresetIndex]);
    }

    [RelayCommand]
    private async Task DeletePreset()
    {
        if (PresetIndex <= 0) return;
        string name = Presets[PresetIndex];
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("Delete preset"), Loc.F("Delete the preset '{0}'?", name), Loc.T("Delete"), kind: MessageKind.Warning)) return;
        await AppServices.FileSystem.DeleteFileAsync(PresetPath(name));
        PresetIndex = 0;
        await RefreshPresetsAsync();
        await LoadPresetAsync(0);
    }

    [RelayCommand]
    private async Task RenamePreset()
    {
        if (PresetIndex <= 0) return;
        string old = Presets[PresetIndex];
        string? name = await AppServices.Dialogs.PromptAsync(Loc.T("Rename Preset"), Loc.T("Name:"), old, t => t.Trim() == old ? null : ValidatePresetName(t, Presets));
        if (name == null || name == old) return;
        await AppServices.FileSystem.MoveFileAsync(PresetPath(old), PresetPath(name));
        await RefreshPresetsAsync();
        PresetIndex = Math.Max(0, Presets.IndexOf(name));
    }
}
