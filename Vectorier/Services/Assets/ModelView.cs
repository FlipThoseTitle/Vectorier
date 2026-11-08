using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Xml;
using Avalonia;
using Avalonia.Media;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;
using Vectorier.Models.Scene;
using Math = System.Math;

namespace Vectorier.Services.Assets;

/// <summary>A model that cannot be shown (missing references, conflicting files, unreadable XML...).</summary>
public sealed class ModelLoadException : Exception
{
    public ModelLoadException(string message, IReadOnlyList<string>? missing = null) : base(message)
    {
        MissingReferences = missing ?? Array.Empty<string>();
    }

    /// <summary>References to nodes or edges no loaded file defines (the model needs another file).</summary>
    public IReadOnlyList<string> MissingReferences { get; }
}

/// <summary>
/// Every model the editor shows (Project Window, Scene, previews, Move Visualizer) as the game holds it: the
/// game's <see cref="ModelObject"/>, parsed from the files in load order, drawn by the game's ModelRender, with its
/// cloth nodes moved by the game's <see cref="ControllerPhysics"/>. Node positions are the game's: level units,
/// Y pointing down.
/// </summary>
public sealed class ModelView
{
    private readonly ControllerPhysics _physics;
    private readonly Vector3[] _rest;

    private ModelView(ModelObject model)
    {
        Object = model;
        Object.IsVisible = true;
        _physics = new ControllerPhysics(model);
        _rest = new Vector3[model.NodesAll.Count];
        for (int i = 0; i < _rest.Length; i++)
            _rest[i] = model.NodesAll[i].Start;
        foreach (var node in model.NodesAll)
            HasCloth |= node.IsPhysics;
    }

    /// <summary>Wraps a model the game code made (the Move Visualizer's gameplay character).</summary>
    public static ModelView For(ModelObject model) => new(model);

    public ModelObject Object { get; }

    public int NodeCount => Object.NodesAll.Count;

    /// <summary>The model has nodes with Cloth="1" (moved by physics, not by the animation).</summary>
    public bool HasCloth { get; }

    /// <summary>How many nodes each loaded file adds, in load order.</summary>
    public IReadOnlyList<int> FileNodeCounts { get; private init; } = Array.Empty<int>();

    /// <summary>Every node as the files place it, in load order.</summary>
    public IReadOnlyList<Vector3> RestPose => _rest;

    public Vector3? Node(string name) => Object.GetNode(name)?.Start;

    public Vector3 this[int index] => Object.NodesAll[index].Start;

    // ================= LOADING ================= //

    /// <summary>
    /// Parses the files in the order the game loads them (models/0.xml first, then the skins). Throws a
    /// <see cref="ModelLoadException"/> when a file is unreadable or refers to something none of the files
    /// before it defines (the game's parser would fail on it).
    /// </summary>
    public static ModelView Load(IReadOnlyList<(string Name, byte[] Data)> files, bool rejectConflicts = false)
    {
        var docs = new List<KeyValuePair<string, XmlNode>>();
        foreach (var (name, data) in files)
        {
            XmlDocument? doc;
            try
            {
                // As the game opens model files: comments are left out (its parser reads every child as an entry).
                doc = new XmlDocument();
                using var reader = XmlReader.Create(new System.IO.StringReader(DecodeText(data)),
                    new XmlReaderSettings { IgnoreComments = true, IgnoreProcessingInstructions = true, IgnoreWhitespace = true });
                doc.Load(reader);
            }
            catch (Exception ex)
            {
                throw new ModelLoadException(Localization.Loc.F("{0} could not be read: {1}", name, ex.Message));
            }
            if (doc?["Scene"] == null)
                throw new ModelLoadException(Localization.Loc.F("{0} could not be read: {1}", name, "<Scene>"));
            docs.Add(new KeyValuePair<string, XmlNode>(name, doc));
        }
        var counts = Check(docs, rejectConflicts);
        return new ModelView(new ModelObject(docs)) { FileNodeCounts = counts };
    }

    /// <summary>What the game's parser looks up, checked in the same order (it fails on a missing node or edge).</summary>
    private static int[] Check(List<KeyValuePair<string, XmlNode>> docs, bool rejectConflicts)
    {
        var counts = new int[docs.Count];
        int fileIndex = -1;
        var missing = new List<string>();
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        var edges = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<(string Section, string Key), string>();
        foreach (var (file, doc) in docs)
        {
            fileIndex++;
            var scene = doc["Scene"]!;
            foreach (string section in new[] { "Nodes", "Edges", "Figures" })
            {
                foreach (XmlNode element in scene[section]?.ChildNodes ?? EmptyNodes)
                {
                    if (element is not XmlElement e)
                        continue;
                    string key = e.Name;
                    if (rejectConflicts && owners.TryGetValue((section, key), out var other) && other != file)
                        throw new ModelLoadException(Localization.Loc.F(
                            "Conflicting entries: '{0}' in <{1}> is defined in both {2} and {3}.\nRemove one of them to preview this character.",
                            key, section, other, file));
                    owners[(section, key)] = file;

                    string type = e.GetAttribute("Type");
                    if (!e.HasAttribute("Type"))
                        missing.Add($"{key}.Type");
                    switch (section)
                    {
                        case "Nodes":
                            if (type is "MacroNode" or "CenterOfMass")
                            {
                                int count = Num.ParseInt(e.GetAttribute("NodesCount"));
                                for (int i = 1; i <= count; i++)
                                {
                                    string child = e.GetAttribute("ChildNode" + i);
                                    if (!nodes.Contains(child))
                                        missing.Add($"{key}.ChildNode{i} → {child}");
                                }
                            }
                            nodes.Add(key);
                            counts[fileIndex]++;
                            break;
                        case "Edges":
                            foreach (string end in new[] { "End1", "End2" })
                                if (!nodes.Contains(e.GetAttribute(end)))
                                    missing.Add($"{key}.{end} → {e.GetAttribute(end)}");
                            edges.Add(key);
                            break;
                        case "Figures" when type == "Capsule":
                            if (!edges.Contains(e.GetAttribute("Edge")))
                                missing.Add($"{key}.Edge → {e.GetAttribute("Edge")}");
                            break;
                        case "Figures" when type == "Triangle":
                            for (int i = 1; i <= 3; i++)
                                if (!nodes.Contains(e.GetAttribute("Node" + i)))
                                    missing.Add($"{key}.Node{i} → {e.GetAttribute("Node" + i)}");
                            break;
                    }
                }
            }
        }
        if (missing.Count > 0)
            throw new ModelLoadException(Localization.Loc.T("Not rendered: references outside the file (") + string.Join(", ", missing.GetRange(0, Math.Min(3, missing.Count))) + ")", missing);
        return counts;
    }

    private static readonly XmlNodeList EmptyNodes = new XmlDocument().ChildNodes;

    /// <summary>Model files are Windows-1251 in Nekki's data; UTF-8 when they declare it.</summary>
    public static string DecodeText(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);

        string head = Encoding.ASCII.GetString(data, 0, Math.Min(200, data.Length));
        if (head.Contains("utf-8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetString(data);

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(data);
        }
        catch
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    // ================= POSING ================= //

    /// <summary>
    /// One game tick (60 per second), in the game's order: the cloth physics, then the animated nodes move to
    /// <paramref name="animated"/> (the first nodes, as the game applies a frame), then the macro nodes.
    /// <paramref name="count"/> limits how many entries of the pose are used.
    /// </summary>
    public void Tick(IReadOnlyList<Vector3> animated, int count = int.MaxValue)
    {
        _physics.Render();
        Apply(animated, count);
        Object.RenderMacroNode();
    }

    /// <summary>Puts the animated nodes there without moving them (no velocity), then the macro nodes.</summary>
    public void Set(IReadOnlyList<Vector3> animated, int count = int.MaxValue)
    {
        var nodes = Object.NodesAll;
        int n = Math.Min(Math.Min(count, animated.Count), nodes.Count);
        for (int i = 0; i < n; i++)
            nodes[i].Position(animated[i], animated[i]);
        Object.RenderMacroNode();
    }

    private void Apply(IReadOnlyList<Vector3> animated, int count)
    {
        var nodes = Object.NodesAll;
        int n = Math.Min(Math.Min(count, animated.Count), nodes.Count);
        for (int i = 0; i < n; i++)
        {
            nodes[i].EndAssignStart();
            nodes[i].PositionStart(animated[i]);
        }
    }

    /// <summary>
    /// Moves every node by an offset (Start and End, so nothing gains speed), like the game's Position: cloth keeps
    /// its shape when the whole model jumps somewhere else.
    /// </summary>
    public void Shift(Vector3 offset)
    {
        foreach (var node in Object.NodesAll)
            node.Position(node.End + offset, node.Start + offset);
    }

    /// <summary>Back to the rest pose of the files.</summary>
    public void ResetToRest()
    {
        var nodes = Object.NodesAll;
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].Position(_rest[i], _rest[i]);
    }

    /// <summary>A still model: its cloth hangs for a while under gravity (what a still preview shows).</summary>
    public void Settle(int ticks = SettleTicks)
    {
        if (!HasCloth)
            return;
        for (int i = 0; i < ticks; i++)
        {
            _physics.Render();
            Object.RenderMacroNode();
        }
    }

    public const int SettleTicks = 180;

    // ================= DRAWING ================= //

    /// <summary>
    /// The model as the game draws it, with <paramref name="transform"/> mapping a node to the target and
    /// <paramref name="scale"/> the target units per level unit (capsule radii).
    /// </summary>
    public Geometry Draw(Func<Vector3, Point> transform, double scale, out Rect bounds) => Draw(Object, transform, scale, out bounds);

    /// <summary>Draws relative to a point (in level units, Y down).</summary>
    public Geometry Draw(Vector3 origin, out Rect bounds) => Draw(p => new Point(p.X - origin.X, p.Y - origin.Y), 1, out bounds);

    public static Geometry Draw(ModelObject model, Func<Vector3, Point> transform, double scale, out Rect bounds)
    {
        var geometry = new StreamGeometry();
        ModelCanvas canvas;
        using (var ctx = geometry.Open())
        {
            canvas = new ModelCanvas(ctx, transform, scale);
            model.Draw(canvas);
        }
        bounds = canvas.Bounds;
        return geometry;
    }

    /// <summary>Largest capsule radius, in level units (for culling).</summary>
    public float MaxRadius
    {
        get
        {
            float r = 0;
            foreach (var capsule in Object.Capsules)
                r = Math.Max(r, capsule.Stroke);
            return r;
        }
    }
}
