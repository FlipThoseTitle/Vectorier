using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Threading;
using Vectorier.Models.Scene;
using Vectorier.Services.Assets;
using Vectorier.ViewModels.Editor;

namespace Vectorier.Services.Collab;

/// <summary>
/// Keeps one editor's level in step with everyone else's. Local edits are spotted from the editor's change events and
/// turned into <see cref="LevelOp"/>s about single elements (identified by their SyncId); others' edits are applied
/// the same way. Platform textures are never sent: every computer rebuilds them from the platform's settings.
/// Applied edits do not become local undo steps; undo / redo re-applies the others' edits made since (see the
/// editor's history), so it only takes back your own changes.
/// </summary>
public sealed class CollabLevelSync : IDisposable
{
    private sealed record Known(string Xml, long Parent, int Index, SceneElement Instance);

    private readonly EditorViewModel _editor;
    private readonly Action<List<LevelOp>> _send;
    private readonly int _userId;
    private readonly Dictionary<long, Known> _known = new();
    private readonly HashSet<SceneElement> _dirty = new();
    private readonly List<LevelOp> _remoteLog = new();
    private readonly DispatcherTimer _timer;
    private bool _structureDirty;
    private bool _anyDirty;
    private bool _applying;
    private long _counter;
    private string _knownSettings = string.Empty;
    private DateTime _lastSettingsCheck = DateTime.MinValue;
    private Dictionary<long, SceneElement>? _index;

    /// <summary>Most edits from others kept for undo (older ones are dropped with the undo history).</summary>
    private const int MaxRemoteLog = 50000;

    public CollabLevelSync(EditorViewModel editor, int userId, Action<List<LevelOp>> send)
    {
        _editor = editor;
        _userId = userId;
        _send = send;
        editor.ElementChanged += OnElementChanged;
        editor.StructureChanged += OnStructureChanged;
        editor.RemoteEditCount = () => _remoteLog.Count;
        editor.ReapplyRemoteEditsSince = ReapplySince;
        // 20 times a second: drags are seen live by the others.
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Flush());
        _timer.Start();
    }

    public EditorViewModel Editor => _editor;

    public void Dispose()
    {
        _timer.Stop();
        _editor.ElementChanged -= OnElementChanged;
        _editor.StructureChanged -= OnStructureChanged;
        _editor.RemoteEditCount = null;
        _editor.ReapplyRemoteEditsSince = null;
    }

    // ================= IDS ================= //

    /// <summary>Gives every element of the level a SyncId (the host does this for the whole level when it starts).</summary>
    public void AssignIds()
    {
        foreach (var element in _editor.Document.AllElements())
        {
            if (element.SyncId == 0 && !IsTile(element))
                element.SyncId = NewId();
        }
    }

    private long NewId() => ((long)(_userId + 1) << 40) | ++_counter;

    private static bool IsTile(SceneElement element) => element is ImageElement { SurfaceOwner: not 0 };

    // ================= LOCAL CHANGES ================= //

    private void OnElementChanged(SceneElement element, string? property)
    {
        // Dynamic Editor playback / scrubbing poses stay on this computer (sending them flooded the connection).
        if (_applying || _editor.Dynamics.IsPosing)
            return;
        _dirty.Add(element);
        _anyDirty = true;
    }

    private void OnStructureChanged()
    {
        if (_applying)
            return;
        AssignIds();
        _structureDirty = true;
        _anyDirty = true;
    }

    /// <summary>Sends what changed since the last call (also called before applying others' edits).</summary>
    public void Flush()
    {
        bool settingsDue = DateTime.UtcNow - _lastSettingsCheck > TimeSpan.FromSeconds(1);
        if (!_anyDirty && !settingsDue)
            return;
        List<LevelOp> ops;
        // Elements bound in the Dynamic Editor are shared at their rest pose, never at a previewed one.
        using (NeedsRestPose() ? _editor.Dynamics.SuspendPreview() : null)
            ops = Diff(settingsDue || _anyDirty);
        _anyDirty = false;
        _structureDirty = false;
        _dirty.Clear();
        if (ops.Count > 0)
            _send(ops);
    }

    /// <summary>The next pass serializes an element the Dynamic Editor is previewing (a full pass looks at every element).</summary>
    private bool NeedsRestPose()
    {
        var dynamics = _editor.Dynamics;
        if (!dynamics.IsBound)
            return false;
        if (_structureDirty || _known.Count == 0)
            return true;
        foreach (var element in _dirty)
            if (dynamics.IsTracked(element))
                return true;
        return false;
    }

    private List<LevelOp> Diff(bool checkSettings)
    {
        var ops = new List<LevelOp>();
        var seen = new HashSet<long>();
        var writer = _editor.CollabWriter();

        void Walk(IList<SceneElement> list, long parent)
        {
            int index = 0;
            foreach (var element in list)
            {
                if (IsTile(element))
                    continue;
                if (element.SyncId == 0)
                    element.SyncId = NewId();
                long id = element.SyncId;
                seen.Add(id);
                _known.TryGetValue(id, out var known);
                bool needXml = known == null || !ReferenceEquals(known.Instance, element) || _dirty.Contains(element);
                string xml = needXml ? Serialize(writer, element) : known!.Xml;
                if (known == null || known.Xml != xml || known.Parent != parent || known.Index != index)
                {
                    ops.Add(new LevelOp("set", id, parent, index, xml));
                }
                _known[id] = new Known(xml, parent, index, element);
                if (element is ObjectElement obj)
                    Walk(obj.Children, id);
                index++;
            }
        }

        // Only a structure change can add, remove or move elements; otherwise just the changed ones are looked at.
        if (_structureDirty || _known.Count == 0)
        {
            Walk(_editor.Document.Roots, 0);
            foreach (var removed in _known.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _known.Remove(removed);
                ops.Add(new LevelOp("remove", removed, 0, 0, null));
            }
        }
        else
        {
            foreach (var element in _dirty)
            {
                if (IsTile(element) || element.SyncId == 0 || !_known.TryGetValue(element.SyncId, out var known) || !ReferenceEquals(known.Instance, element))
                    continue;
                string xml = Serialize(writer, element);
                if (xml != known.Xml)
                {
                    ops.Add(new LevelOp("set", element.SyncId, known.Parent, known.Index, xml));
                    _known[element.SyncId] = known with { Xml = xml };
                }
            }
        }

        if (checkSettings)
        {
            _lastSettingsCheck = DateTime.UtcNow;
            string settings = SerializeSettings(writer, _editor.Document.Settings);
            if (settings != _knownSettings)
            {
                _knownSettings = settings;
                ops.Add(new LevelOp("settings", 0, 0, 0, settings));
            }
        }
        _index = null;
        return ops;
    }

    // ================= SERIALIZING ================= //

    /// <summary>
    /// Collaboration-only marker sent after an element's XML: its Layer and Order in Layer. Neither is part of the
    /// element's own XML (in a level file the layer is the &lt;Track&gt; Object holding it and the order is the write
    /// order), so without it both were lost on the way. Never written to files.
    /// </summary>
    private const string SyncTag = "VectorierSync";

    /// <summary>
    /// One element without its children (an Object's children are elements of their own); a platform's surface
    /// settings, the layer and the order in layer ride along.
    /// </summary>
    private static string Serialize(Xml.SceneXmlWriter writer, SceneElement element)
    {
        var node = writer.WriteElement(element);
        string xml;
        if (node is XElement x)
        {
            if (element is ObjectElement)
                x.Element("Content")?.Remove();
            if (element is PlatformElement { Surface: { } surface })
                x.SetAttributeValue("vectorierSurface", $"{surface.Type},{surface.Floor},{surface.LeftWall},{surface.RightWall},{surface.Seed}");
            xml = x.ToString(SaveOptions.DisableFormatting);
        }
        else
        {
            xml = node?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
        }

        // A separate marker (not an attribute) so comments and unknown elements carry them too.
        bool defaultLayer = string.IsNullOrEmpty(element.Layer) || element.Layer == Layers.Default;
        if (!defaultLayer || element.Order != 0)
        {
            var sync = new XElement(SyncTag);
            if (!defaultLayer)
                sync.SetAttributeValue("Layer", element.Layer);
            if (element.Order != 0)
                sync.SetAttributeValue("Order", element.Order.ToString(System.Globalization.CultureInfo.InvariantCulture));
            xml += sync.ToString(SaveOptions.DisableFormatting);
        }
        return xml;
    }

    private static string SerializeSettings(Xml.SceneXmlWriter writer, LevelSettings settings)
    {
        var doc = new SceneDocument { Settings = settings };
        var root = writer.WriteLevel(doc).Root!;
        root.Element("Track")?.Remove();
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private SceneElement? Parse(LevelOp op)
    {
        if (string.IsNullOrEmpty(op.Xml))
            return null;
        try
        {
            var wrapper = XElement.Parse("<Content>" + op.Xml + "</Content>");
            var sync = wrapper.Element(SyncTag);
            sync?.Remove();
            var element = _editor.CollabReader.ReadContent(wrapper).FirstOrDefault();
            if (element == null)
                return null;
            // No marker: the default layer and order (the reader already gave the element those).
            element.Layer = sync?.Attribute("Layer")?.Value is { Length: > 0 } layer ? layer : Layers.Default;
            element.Order = Num.ParseInt(sync?.Attribute("Order")?.Value);
            if (element is PlatformElement platform && wrapper.Elements().FirstOrDefault()?.Attribute("vectorierSurface")?.Value is { } surface)
            {
                var parts = surface.Split(',');
                if (parts.Length == 5 && Enum.TryParse<PlatformSurfaceType>(parts[0], out var type))
                {
                    platform.Surface = new PlatformSurfaceSettings(type, bool.Parse(parts[1]), bool.Parse(parts[2]), bool.Parse(parts[3]), int.Parse(parts[4]));
                    platform.SurfaceBuiltKey = null;
                }
                platform.ExtraAttributes.Remove("vectorierSurface");
            }
            return element;
        }
        catch
        {
            return null;
        }
    }

    // ================= SNAPSHOT ================= //

    /// <summary>Host: the whole level as "set" operations in tree order, plus the settings.</summary>
    public List<LevelOp> Snapshot()
    {
        Flush();
        // A joining participant gets the elements the Dynamic Editor is previewing at their rest pose.
        using var rest = _editor.Dynamics.SuspendPreview();
        var writer = _editor.CollabWriter();
        var ops = new List<LevelOp> { new("settings", 0, 0, 0, SerializeSettings(writer, _editor.Document.Settings)) };
        void Walk(IList<SceneElement> list, long parent)
        {
            int index = 0;
            foreach (var element in list)
            {
                if (IsTile(element))
                    continue;
                ops.Add(new LevelOp("set", element.SyncId, parent, index, _known.TryGetValue(element.SyncId, out var k) ? k.Xml : Serialize(writer, element)));
                if (element is ObjectElement obj)
                    Walk(obj.Children, element.SyncId);
                index++;
            }
        }
        Walk(_editor.Document.Roots, 0);
        return ops;
    }

    /// <summary>Participant: replaces the level with the host's.</summary>
    public void LoadSnapshot(List<LevelOp> ops)
    {
        _applying = true;
        try
        {
            using (_editor.SuppressTracking())
            {
                _editor.ClearSelection();
                _editor.Document.Roots.Clear();
                _known.Clear();
                _index = null;
                foreach (var op in ops)
                    ApplyOne(op, remember: false);
            }
        }
        finally
        {
            _applying = false;
        }
        _editor.RaiseStructureChanged();
        _editor.ResetHistoryAfterCollabLoad();
        _dirty.Clear();
        _anyDirty = false;
        _structureDirty = false;
    }

    // ================= APPLYING OTHERS' EDITS ================= //

    /// <summary>Applies edits made by someone else (already in their final order).</summary>
    public void ApplyRemote(IReadOnlyList<LevelOp> ops)
    {
        if (ops.Count == 0)
            return;
        _applying = true;
        try
        {
            using (_editor.SuppressTracking())
            {
                foreach (var op in ops)
                    ApplyOne(op, remember: true);
            }
        }
        finally
        {
            _applying = false;
        }
        AfterApply();
    }

    /// <summary>Re-applies own edits not yet confirmed by the host after others' edits (own edits win until then).</summary>
    public void Reapply(IEnumerable<LevelOp> ops)
    {
        _applying = true;
        try
        {
            using (_editor.SuppressTracking())
            {
                foreach (var op in ops)
                    ApplyOne(op, remember: false);
            }
        }
        finally
        {
            _applying = false;
        }
        AfterApply();
    }

    private void ReapplySince(int mark)
    {
        if (mark < 0 || mark >= _remoteLog.Count)
            return;
        _applying = true;
        try
        {
            _index = null;
            // What is known to be shared stays as it was: the restored elements are compared with it on the next
            // pass, so the undo reaches the others (replaying an op must not count as "already sent").
            foreach (var op in _remoteLog.Skip(mark).ToList())
                ApplyOne(op, remember: false, updateKnown: false);
        }
        finally
        {
            _applying = false;
        }
        // The restored elements are new instances: compare them all on the next pass.
        _structureDirty = true;
        _anyDirty = true;
    }

    private void AfterApply()
    {
        _index = null;
        _editor.RaiseStructureChanged();
        _editor.SceneChangedByCollab();
    }

    private SceneElement? Find(long id)
    {
        if (id == 0)
            return null;
        if (_index == null)
        {
            _index = new Dictionary<long, SceneElement>();
            foreach (var e in _editor.Document.AllElements())
                if (e.SyncId != 0)
                    _index[e.SyncId] = e;
        }
        return _index.TryGetValue(id, out var found) ? found : null;
    }

    private void ApplyOne(LevelOp op, bool remember, bool updateKnown = true)
    {
        if (remember)
        {
            _remoteLog.Add(op);
            if (_remoteLog.Count > MaxRemoteLog)
                _remoteLog.RemoveRange(0, _remoteLog.Count - MaxRemoteLog);
        }
        switch (op.Kind)
        {
            case "settings":
                ApplySettings(op.Xml);
                break;
            case "remove":
            {
                var element = Find(op.Id);
                if (element == null)
                    return;
                var siblings = element.Parent?.Children ?? _editor.Document.Roots;
                if (element is PlatformElement platform)
                {
                    foreach (var tile in siblings.OfType<ImageElement>().Where(t => t.SurfaceOwner == platform.Id).ToList())
                        Detach(tile);
                }
                Detach(element);
                if (updateKnown)
                    _known.Remove(op.Id);
                _index = null;
                break;
            }
            case "set":
            {
                var proto = Parse(op);
                if (proto == null)
                    return;
                IList<SceneElement> siblings;
                ObjectElement? parent = null;
                if (op.Parent == 0)
                {
                    siblings = _editor.Document.Roots;
                }
                else if (Find(op.Parent) is ObjectElement p)
                {
                    parent = p;
                    siblings = p.Children;
                }
                else
                {
                    return; // its parent is gone
                }

                var element = Find(op.Id);
                if (element == null)
                {
                    element = proto;
                    element.SyncId = op.Id;
                    Insert(element, parent, siblings, op.Index);
                    _index = null;
                }
                else
                {
                    bool expanded = element.IsExpanded;
                    element.CopyPropertiesFrom(proto);
                    element.IsExpanded = expanded;
                    var current = element.Parent?.Children ?? _editor.Document.Roots;
                    if (!ReferenceEquals(current, siblings) || SyncedIndex(current, element) != op.Index)
                    {
                        Detach(element);
                        Insert(element, parent, siblings, op.Index);
                    }
                }
                if (element is PlatformElement { Surface: not null } surfaced)
                    _editor.RequestSurfaceRebuild(surfaced);
                if (updateKnown)
                    _known[op.Id] = new Known(op.Xml ?? string.Empty, op.Parent, op.Index, element);
                break;
            }
        }
    }

    private void Detach(SceneElement element)
    {
        if (element.Parent is { } parent)
            parent.RemoveChild(element);
        else
            _editor.Document.Roots.Remove(element);
    }

    /// <summary>Position among the synced siblings (platform textures are not counted).</summary>
    private static int SyncedIndex(IList<SceneElement> siblings, SceneElement element)
    {
        int index = 0;
        foreach (var e in siblings)
        {
            if (ReferenceEquals(e, element))
                return index;
            if (!IsTile(e))
                index++;
        }
        return -1;
    }

    private void Insert(SceneElement element, ObjectElement? parent, IList<SceneElement> siblings, int syncedIndex)
    {
        // The real list index of the synced sibling now at that position (textures in between are skipped).
        int real = siblings.Count, count = 0;
        for (int i = 0; i < siblings.Count; i++)
        {
            if (IsTile(siblings[i]))
                continue;
            if (count == syncedIndex)
            {
                real = i;
                break;
            }
            count++;
        }
        if (parent != null)
            parent.AddChild(element, real);
        else
        {
            element.Parent = null;
            _editor.Document.Roots.Insert(Math.Min(real, _editor.Document.Roots.Count), element);
        }
    }

    private void ApplySettings(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
            return;
        try
        {
            var root = XElement.Parse(xml);
            root.Add(new XElement("Track"));
            var settings = _editor.CollabReader.ReadLevel(new XDocument(root), _editor.Document.LevelName).Settings;
            _knownSettings = xml;
            _editor.ApplyCollabSettings(settings);
        }
        catch
        {
            // Unreadable settings are ignored; the next change resends them.
        }
    }

    // ================= PRESENCE HELPERS ================= //

    /// <summary>The element with this SyncId, if it is in the level.</summary>
    public SceneElement? FindElement(long id) => Find(id);
}
