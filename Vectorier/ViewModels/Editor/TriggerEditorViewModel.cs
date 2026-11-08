using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using Vectorier.Models.Scene;
using Vectorier.Services;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Triggers;
using Vectorier.Services.Xml;

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
    // The templates read last (one project's: another project reads its own).
    private static (string ProjectPath, TriggerTemplateLibrary Library)? _lastLibrary;

    public TriggerEditorViewModel(EditorViewModel editor, TriggerElement trigger)
    {
        Editor = editor;
        Trigger = trigger;
        if (trigger.VariableTypes.Count == 0 && editor.FindCachedTriggerTypes(trigger) is { } cached)
            trigger.VariableTypes = new Dictionary<string, string>(cached);
        Data = TriggerData.Parse(trigger.ContentXml);
        ReadTypes(Data, trigger.VariableTypes);
        SelectedLoop = Data.loops.Count > 0 ? 0 : -1;
        Library = _lastLibrary is { } last && last.ProjectPath == editor.Project.Path ? last.Library : new TriggerTemplateLibrary();
        _ = InitializeAsync();
    }

    public EditorViewModel Editor { get; }
    public TriggerElement Trigger { get; private set; }
    public TriggerData Data { get; private set; }
    public TriggerTemplateLibrary Library { get; private set; }

    public string Title => Mode switch
    {
        TriggerEditorMode.TemplateList => "TriggerTemplates.xml",
        TriggerEditorMode.Template when EditingTemplate != null => TriggerTemplateFile.NameOf(EditingTemplate) + (TemplateIsLibrary ? "  (" + Loc.T("Library") + ")" : string.Empty),
        _ => string.IsNullOrEmpty(Trigger.Name) ? Loc.T("Trigger") : Trigger.Name,
    };

    // ================= SOURCES ================= //

    private void RefreshSceneSources()
    {
        var settings = Editor.Document.Settings;
        Models = settings.CommonModels.Concat(settings.HunterModels)
            .Select(m => m.Get("Name")).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
        Spawns = Editor.Document.AllElements().OfType<SpawnElement>().Select(s => s.Name)
            .Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private async Task LoadProjectSourcesAsync()
    {
        var fs = AppServices.FileSystem;
        var project = Editor.Project;
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
        await library.LoadAsync(AppServices.FileSystem, VPath.Combine(Editor.Project.XmlRoot, "TriggerTemplates.xml"));
        Library = library;
        _lastLibrary = (Editor.Project.Path, library);
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
        Trigger = Editor.Document.FindById(Trigger.Id) as TriggerElement ?? Trigger;
        Trigger.ContentXml = Data.ToXml();
        Trigger.VariableTypes = WriteTypes(Data);
        IsDirty = false;
        Editor.StatusText = Loc.F("Applied trigger '{0}'", Title);
        _ = Editor.CacheTriggerTypesAsync(Trigger);
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
        Editor.CloseTriggerEditor();
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
        Editor.StatusText = Loc.F("Saved preset '{0}'", Presets[PresetIndex]);
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
