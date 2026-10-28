using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorier_Editor_Avalonia.Models.Scene;
using Vectorier_Editor_Avalonia.Services;
using Vectorier_Editor_Avalonia.Services.IO;
using Vectorier_Editor_Avalonia.Services.Xml;

namespace Vectorier_Editor_Avalonia.ViewModels.Editor;

/// <summary>An object definition open in Edit Object mode, with what is needed to return to the level.</summary>
public sealed class ObjectEditSession
{
    public required ObjectDefinition Definition { get; init; }
    public required string File { get; init; }
    public required bool IsBuilding { get; init; }
    public required SceneDocument LevelDocument { get; init; }
    public required object LevelHistory { get; init; }
    public required int[] LevelSelection { get; init; }
    public required Point LevelCenter { get; init; }

    /// <summary>Object XML files saved during the session (definitions to reload when returning to the level).</summary>
    public HashSet<string> SavedFiles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Name => Definition.Name;
}

/// <summary>Ungroup and Edit Object mode.</summary>
public sealed partial class EditorViewModel
{
    /// <summary>The definition being edited in the Object tab, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingObject), nameof(IsEditingBuilding), nameof(ObjectTabTitle), nameof(Title))]
    private ObjectEditSession? _objectEdit;

    public bool IsEditingObject => ObjectEdit != null;
    public bool IsEditingBuilding => ObjectEdit?.IsBuilding == true;
    public string ObjectTabTitle => ObjectEdit == null ? "Object" : $"Object: {ObjectEdit.Name}";

    /// <summary>Asks the Scene to look at a level point (Edit Object centers on the origin).</summary>
    public event Action<Point>? CenterRequested;

    private bool _switchingTab;

    partial void OnCenterTabChanged(CenterTab oldValue, CenterTab newValue)
    {
        // Leaving the Object tab ends Edit Object mode (its changes are saved to the object's file).
        if (oldValue == CenterTab.Object && newValue != CenterTab.Object && ObjectEdit != null && !_switchingTab)
            _ = LeaveObjectEditAsync(save: true);
    }

    // ================= UNGROUP ================= //

    public bool CanUngroup => GetTopmostSelection().Any(e => e is ObjectElement);

    /// <summary>
    /// Ungroup: an Object without definition is replaced by its children; one with a definition is replaced by
    /// copies of its content (and children), which become ordinary editable elements. Positions stay in place.
    /// </summary>
    [RelayCommand]
    public void Ungroup()
    {
        var objects = GetTopmostSelection().OfType<ObjectElement>().ToList();
        if (objects.Count == 0)
            return;
        BeginGesture();
        var released = new List<SceneElement>();
        foreach (var obj in objects)
        {
            var parent = obj.Parent;
            var siblings = Document.SiblingsOf(obj);
            int index = siblings.IndexOf(obj);
            var offset = new Vector(obj.X, obj.Y);
            var items = new List<SceneElement>();
            if (obj.Reference is { } def)
            {
                BuildDefinition(def);
                items.AddRange(def.Content.Select(c => c.Clone(keepIds: false)));
            }
            foreach (var child in obj.Children.ToList())
            {
                obj.RemoveChild(child);
                items.Add(child);
            }
            Document.Remove(obj);
            foreach (var item in items)
            {
                item.X += offset.X;
                item.Y += offset.Y;
                if (parent == null && obj.LayerEditable && item.LayerEditable)
                    item.Layer = obj.Layer;
                Document.Insert(item, parent, index < 0 ? -1 : index++);
                released.Add(item);
            }
        }
        RaiseStructureChanged();
        MarkChanged();
        EndGesture();
        Select(released);
        StatusText = $"Ungrouped {objects.Count} object(s) into {released.Count} element(s)";
    }

    // ================= EDIT OBJECT ================= //

    /// <summary>Edit Object from a level instance (Hierarchy, Inspector).</summary>
    [RelayCommand]
    public async Task EditObjectAsync(ObjectElement? obj)
    {
        obj ??= ActiveElement as ObjectElement;
        if (obj?.Reference is { } def)
            await EnterObjectEditAsync(def);
    }

    /// <summary>
    /// Opens a definition in the Object tab: the Scene shows its content around the origin and edits it like a
    /// level. Leaving the tab writes it back to its Object XML.
    /// </summary>
    public async Task EnterObjectEditAsync(ObjectDefinition definition)
    {
        var previous = ObjectEdit;
        if (previous != null)
        {
            if (ReferenceEquals(previous.Definition, definition) || previous.Name == definition.Name)
            {
                CenterTab = CenterTab.Object;
                return;
            }
            // Another object: the Object tab stays; the current one is saved and replaced once the new one is ready.
            CommitPending();
            if (IsDirty && !await SaveObjectAsync())
                return;
        }

        CommitPending();
        BuildDefinition(definition);
        bool building = await Context.Objects.IsBuildingFileAsync(definition.SourceFile);

        var session = new ObjectEditSession
        {
            Definition = definition,
            File = definition.SourceFile,
            IsBuilding = building,
            LevelDocument = previous?.LevelDocument ?? Document,
            LevelHistory = previous?.LevelHistory ?? SaveHistory(),
            LevelSelection = previous?.LevelSelection ?? Selection.Select(e => e.Id).ToArray(),
            LevelCenter = previous?.LevelCenter ?? SceneViewCenter,
            SavedFiles = previous?.SavedFiles ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };

        var doc = new SceneDocument { LevelName = definition.Name, Settings = (previous?.LevelDocument ?? Document).Settings.Clone() };
        foreach (var element in definition.Content)
            doc.Roots.Add(element.Clone(keepIds: false));
        if (building)
        {
            var content = doc.Roots.ToList();
            var (inPt, outPt, _) = ObjectFileOps.ComputeBuilding(content, definition.In, definition.Out);
            doc.Roots.Add(new BuildingMarkerElement(true) { X = inPt.X, Y = inPt.Y });
            doc.Roots.Add(new BuildingMarkerElement(false) { X = outPt.X, Y = outPt.Y });
        }

        _switchingTab = true;
        ObjectEdit = session;
        SetDocument(doc, null);
        // A fresh document is not dirty.
        IsDirty = false;
        CenterTab = CenterTab.Object;
        _switchingTab = false;
        RefreshElementBar();
        CenterRequested?.Invoke(new Point(0, 0));
        StatusText = $"Editing object {definition.Name} ({definition.SourceFile}). Switch to the Scene tab to return to the level.";
    }

    /// <summary>Returns to the level, saving the object first. Returns false when saving failed and the user stayed.</summary>
    public async Task<bool> LeaveObjectEditAsync(bool save)
    {
        var session = ObjectEdit;
        if (session == null)
            return true;
        CommitPending();
        if (save && IsDirty && !await SaveObjectAsync())
        {
            _switchingTab = true;
            CenterTab = CenterTab.Object;
            _switchingTab = false;
            return false;
        }

        _switchingTab = true;
        ObjectEdit = null;
        SetDocument(session.LevelDocument, session.LevelSelection);
        RestoreHistory(session.LevelHistory);
        if (CenterTab == CenterTab.Object)
            CenterTab = CenterTab.Scene;
        _switchingTab = false;
        RefreshElementBar();

        // Instances show the edited definitions.
        Context.Objects.Invalidate(session.File);
        foreach (var file in session.SavedFiles)
            Context.Objects.Invalidate(file);
        await ResolveDefinitionsAsync(Document.Settings);
        using (SuppressTracking())
            ResolveReferences();
        RaiseStructureChanged();
        CenterRequested?.Invoke(session.LevelCenter);
        await ProjectWindow.ReloadAsync();
        return true;
    }

    /// <summary>Writes the edited content back to the definition in its Object XML (Building: In / Out / Box too).</summary>
    public async Task<bool> SaveObjectAsync()
    {
        var session = ObjectEdit;
        if (session == null)
            return false;
        try
        {
            string path = VPath.Combine(Project.XmlRoot, session.File);
            var doc = await ObjectFileOps.ReadAsync(path);
            var old = ObjectFileOps.FindDefinition(doc, session.Name);
            var content = Document.Roots.Where(e => e is not BuildingMarkerElement).ToList();
            XElement definition;
            using (Dynamics.SuspendPreview())
            {
                var writer = CreateWriter();
                definition = writer.WriteDefinition(session.Name, content);
                if (writer.Errors.Count > 0)
                    await AppServices.Dialogs.ShowMessageAsync("Save Object", string.Join("\n", writer.Errors), MessageKind.Warning);
            }
            // Keep the definition's other attributes; In / Out / Box are recomputed for Buildings.
            if (old != null)
                foreach (var attr in old.Attributes().Where(a => a.Name.LocalName != "Name" && !ObjectDefinition.BuildingAttributes.Contains(a.Name.LocalName)))
                    definition.SetAttributeValue(attr.Name, attr.Value);
            if (session.IsBuilding)
            {
                var markers = Document.Roots.OfType<BuildingMarkerElement>().ToList();
                var inMarker = markers.FirstOrDefault(m => m.IsIn);
                var outMarker = markers.FirstOrDefault(m => !m.IsIn);
                var (inPt, outPt, box) = ObjectFileOps.ComputeBuilding(content,
                    inMarker != null ? new Point(inMarker.X, inMarker.Y) : null,
                    outMarker != null ? new Point(outMarker.X, outMarker.Y) : null);
                ObjectFileOps.WriteBuildingAttributes(definition, inPt, outPt, box);
            }
            // Attributes first: Name, then the rest in the original order.
            if (old != null)
                old.ReplaceWith(definition);
            else
                (doc.Root?.Element("Objects") ?? throw new InvalidOperationException("The file has no <Objects>.")).Add(definition);
            await ObjectFileOps.WriteAsync(path, doc);
            Context.Objects.Invalidate(session.File);
            session.SavedFiles.Add(session.File);
            MarkSaved();
            StatusText = $"Saved object {session.Name} to {session.File}";
            return true;
        }
        catch (Exception ex)
        {
            await AppServices.Dialogs.ShowMessageAsync("Save Object", "The object could not be saved:\n" + ex.Message, MessageKind.Error);
            return false;
        }
    }

    /// <summary>While editing an object, it cannot be placed inside itself (nor anything that contains it).</summary>
    public bool IsPlacementDenied(ElementSource? source, out string reason)
    {
        reason = string.Empty;
        if (ObjectEdit == null || source?.Definition is not { } def)
            return false;
        if (def.Name == ObjectEdit.Name || ContainsDefinition(def, ObjectEdit.Name, 0))
        {
            reason = $"'{def.Name}' cannot be placed inside the object being edited ({ObjectEdit.Name}).";
            return true;
        }
        return false;
    }

    private bool ContainsDefinition(ObjectDefinition def, string name, int depth)
    {
        if (depth > 16)
            return false;
        BuildDefinition(def);
        foreach (var e in def.Content.SelectMany(SceneDocument.Walk))
        {
            if (e is ObjectElement { Reference: { } inner } && (inner.Name == name || ContainsDefinition(inner, name, depth + 1)))
                return true;
        }
        return false;
    }

    /// <summary>In / Out appear in the Element Bar while editing a Building.</summary>
    private void RefreshElementBar()
    {
        var existing = ElementTypes.Where(t => t.Type is ElementType.BuildingIn or ElementType.BuildingOut).ToList();
        if (IsEditingBuilding && existing.Count == 0)
        {
            ElementTypes.Add(new ElementTypeInfo(ElementType.BuildingIn, ElementTypes.Count));
            ElementTypes.Add(new ElementTypeInfo(ElementType.BuildingOut, ElementTypes.Count));
        }
        else if (!IsEditingBuilding)
        {
            foreach (var info in existing)
                ElementTypes.Remove(info);
            if (ActiveElementType is { Type: ElementType.BuildingIn or ElementType.BuildingOut })
            {
                ActiveElementType = ElementTypes[0];
                PlacementType = null;
            }
        }
    }

    /// <summary>
    /// A definition was renamed project-wide (files already rewritten): instances of the open level follow, without
    /// an undo step (the level file on disk was updated the same way).
    /// </summary>
    public async Task OnDefinitionRenamedAsync(string oldName, string newName)
    {
        var level = ObjectEdit?.LevelDocument ?? Document;
        using (SuppressTracking())
        {
            foreach (var obj in level.AllElements().OfType<ObjectElement>().Where(o => o.Name == oldName))
                obj.Name = newName;
        }
        await ResolveDefinitionsAsync(Document.Settings);
        using (SuppressTracking())
            ResolveReferences();
        // The baseline must not show the rename as a change.
        _baseline = CaptureEntry();
        RaiseStructureChanged();
    }

    // ================= HISTORY SWAP ================= //

    private sealed record SavedHistory(List<HistoryEntry> Undo, List<HistoryEntry> Redo, HistoryEntry? Baseline, int Version, int SavedVersion, bool Dirty);

    private object SaveHistory() => new SavedHistory(_undo.ToList(), _redo.ToList(), _baseline, _version, _savedVersion, IsDirty);

    private void RestoreHistory(object saved)
    {
        if (saved is not SavedHistory h)
            return;
        _undo.Clear();
        _undo.AddRange(h.Undo);
        _redo.Clear();
        _redo.AddRange(h.Redo);
        _version = h.Version;
        _savedVersion = h.SavedVersion;
        _baseline = CaptureEntry();
        IsDirty = h.Dirty;
        RaiseHistoryChanged();
    }
}
