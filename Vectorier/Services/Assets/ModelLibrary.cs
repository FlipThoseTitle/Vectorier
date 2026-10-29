using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using Vectorier.Models.Scene;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Assets;

/// <summary>
/// Nekki's node-based model (Nodes, Edges, Figures). Only Node, MacroNode, CenterOfMass, Edge, Muscle,
/// Capsule and Triangle are used. Model space has Y pointing up; the 2D silhouette is the orthographic
/// front view (X, -Y).
/// </summary>
public sealed class ModelData
{
    public sealed record MacroInfo(int Node, int[] Children, float[] Weights);
    public sealed record CapsuleInfo(string Name, int A, int B, float Radius1, float Radius2, float Margin1, float Margin2);
    public sealed record TriangleInfo(int A, int B, int C);

    public string Name { get; init; } = string.Empty;
    public List<string> NodeNames { get; } = new();
    public Dictionary<string, int> NodeIndex { get; } = new(StringComparer.Ordinal);
    public List<string> NodeTypes { get; } = new();
    public Vector3[] BasePose { get; set; } = Array.Empty<Vector3>();
    public List<MacroInfo> Macros { get; } = new();
    public Dictionary<string, (int A, int B)> Edges { get; } = new(StringComparer.Ordinal);
    public List<CapsuleInfo> Capsules { get; } = new();
    public List<TriangleInfo> Triangles { get; } = new();

    /// <summary>Unresolved references found while loading (the model needs another file, e.g. a skeleton).</summary>
    public List<string> MissingReferences { get; } = new();

    public bool IsSelfContained => MissingReferences.Count == 0;

    public int PivotIndex => NodeIndex.TryGetValue("NPivot", out int index) ? index : -1;

    /// <summary>Applies MacroNode / CenterOfMass weighted sums to a pose (game: MacroNodeCompute).</summary>
    public void ComputeMacros(Vector3[] pose)
    {
        foreach (var macro in Macros)
        {
            if (macro.Weights.Length == 0)
                continue;
            var sum = Vector3.Zero;
            for (int i = 0; i < macro.Children.Length; i++)
                sum += pose[macro.Children[i]] * macro.Weights[i];
            pose[macro.Node] = sum;
        }
    }

    /// <summary>
    /// Builds the black silhouette for a pose, in 2D units relative to the pivot (Y down).
    /// When <paramref name="pose"/> is null the base pose from the XML is used.
    /// </summary>
    public Geometry BuildSilhouette(Vector3[]? pose, out Rect bounds)
    {
        pose ??= BasePose;
        return BuildSilhouette(pose, PivotIndex >= 0 ? pose[PivotIndex] : Vector3.Zero, out bounds);
    }

    /// <summary>Silhouette relative to an explicit origin (a fixed or moving camera point).</summary>
    public Geometry BuildSilhouette(Vector3[] pose, Vector3 origin, out Rect bounds)
    {
        Point P(int i) => new(pose[i].X - origin.X, -(pose[i].Y - origin.Y));

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Grow(Point p, double r)
        {
            minX = Math.Min(minX, p.X - r); minY = Math.Min(minY, p.Y - r);
            maxX = Math.Max(maxX, p.X + r); maxY = Math.Max(maxY, p.Y + r);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.SetFillRule(FillRule.NonZero);
            foreach (var c in Capsules)
            {
                var a = P(c.A);
                var b = P(c.B);
                var d = b - a;
                // Margins trim the capsule from each end (fraction of the edge length).
                var start = a + d * c.Margin1;
                var end = b - d * c.Margin2;
                AddCapsule(ctx, start, end, c.Radius1, c.Radius2);
                Grow(start, c.Radius1);
                Grow(end, c.Radius2);
            }
            foreach (var t in Triangles)
            {
                var a = P(t.A); var b = P(t.B); var cc = P(t.C);
                FillPolygon.Add(ctx, a, b, cc);
                Grow(a, 0); Grow(b, 0); Grow(cc, 0);
            }
        }

        bounds = minX <= maxX ? new Rect(minX, minY, maxX - minX, maxY - minY) : new Rect(-40, -40, 80, 80);
        return geometry;
    }

    private static void AddCapsule(StreamGeometryContext ctx, Point a, Point b, double ra, double rb)
    {
        AddCircle(ctx, a, ra);
        AddCircle(ctx, b, rb);

        var d = b - a;
        double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (length < 1e-6)
            return;
        var n = new Avalonia.Vector(-d.Y / length, d.X / length);
        // Quad between the circles (tangent approximation; exact for equal radii).
        FillPolygon.Add(ctx, a + n * ra, b + n * rb, b - n * rb, a - n * ra);
    }

    private static void AddCircle(StreamGeometryContext ctx, Point c, double r)
    {
        if (r <= 0)
            return;
        ctx.BeginFigure(new Point(c.X + r, c.Y), true);
        ctx.ArcTo(new Point(c.X - r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
    }

    // ================= PARSING ================= //

    private static readonly HashSet<string> SupportedNodeTypes = new() { "Node", "MacroNode", "CenterOfMass" };

    public static ModelData Parse(string name, byte[] data) => Parse(name, XDocument.Parse(DecodeText(data)));

    public static ModelData Parse(string name, XDocument doc)
    {
        var model = new ModelData { Name = name };
        var scene = doc.Root ?? throw new FormatException("Empty model XML.");
        var nodes = scene.Element("Nodes");

        var positions = new List<Vector3>();
        var pendingMacros = new List<(int Index, XElement Node)>();
        foreach (var node in nodes?.Elements() ?? Enumerable.Empty<XElement>())
        {
            string type = (string?)node.Attribute("Type") ?? "Node";
            if (!SupportedNodeTypes.Contains(type))
                continue;
            int index = model.NodeNames.Count;
            model.NodeNames.Add(node.Name.LocalName);
            model.NodeIndex[node.Name.LocalName] = index;
            model.NodeTypes.Add(type);
            positions.Add(new Vector3(F(node, "X"), F(node, "Y"), F(node, "Z")));
            if (type is "MacroNode" or "CenterOfMass")
                pendingMacros.Add((index, node));
        }
        model.BasePose = positions.ToArray();

        foreach (var (index, node) in pendingMacros)
        {
            int count = (int)F(node, "NodesCount");
            var children = new List<int>();
            var weights = new List<float>();
            for (int i = 1; i <= count; i++)
            {
                string? child = (string?)node.Attribute("ChildNode" + i);
                if (child == null || !model.NodeIndex.TryGetValue(child, out int childIndex))
                {
                    model.MissingReferences.Add($"{node.Name.LocalName}.ChildNode{i} → {child}");
                    continue;
                }
                children.Add(childIndex);
                // CenterOfMass has no LCC weights in the game (no MacroNode compute); keep its XML position.
                weights.Add(node.Attribute("LCC" + i) != null ? F(node, "LCC" + i) : float.NaN);
            }
            if ((string?)node.Attribute("Type") == "MacroNode" && children.Count > 0 && weights.All(w => !float.IsNaN(w)))
                model.Macros.Add(new MacroInfo(index, children.ToArray(), weights.ToArray()));
        }

        foreach (var edge in scene.Element("Edges")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            string type = (string?)edge.Attribute("Type") ?? "Edge";
            if (type is not ("Edge" or "Muscle"))
                continue;
            string end1 = (string?)edge.Attribute("End1") ?? "";
            string end2 = (string?)edge.Attribute("End2") ?? "";
            bool ok1 = model.NodeIndex.TryGetValue(end1, out int a);
            bool ok2 = model.NodeIndex.TryGetValue(end2, out int b);
            if (!ok1) model.MissingReferences.Add($"{edge.Name.LocalName}.End1 → {end1}");
            if (!ok2) model.MissingReferences.Add($"{edge.Name.LocalName}.End2 → {end2}");
            if (ok1 && ok2)
                model.Edges[edge.Name.LocalName] = (a, b);
        }

        foreach (var figure in scene.Element("Figures")?.Elements() ?? Enumerable.Empty<XElement>())
        {
            switch ((string?)figure.Attribute("Type"))
            {
                case "Capsule":
                {
                    string edgeName = (string?)figure.Attribute("Edge") ?? "";
                    if (!model.Edges.TryGetValue(edgeName, out var edge))
                    {
                        model.MissingReferences.Add($"{figure.Name.LocalName}.Edge → {edgeName}");
                        continue;
                    }
                    float r1 = F(figure, "Radius1", 1);
                    float r2 = figure.Attribute("Radius2") != null ? F(figure, "Radius2", r1) : r1;
                    model.Capsules.Add(new CapsuleInfo(figure.Name.LocalName, edge.A, edge.B, r1, r2,
                        Math.Clamp(F(figure, "Margin1"), 0, 1), Math.Clamp(F(figure, "Margin2"), 0, 1)));
                    break;
                }
                case "Triangle":
                {
                    var names = new[] { "Node1", "Node2", "Node3" }.Select(a => (string?)figure.Attribute(a) ?? "").ToArray();
                    var idx = new int[3];
                    bool ok = true;
                    for (int i = 0; i < 3; i++)
                    {
                        if (!model.NodeIndex.TryGetValue(names[i], out idx[i]))
                        {
                            model.MissingReferences.Add($"{figure.Name.LocalName}.Node{i + 1} → {names[i]}");
                            ok = false;
                        }
                    }
                    if (ok)
                        model.Triangles.Add(new TriangleInfo(idx[0], idx[1], idx[2]));
                    break;
                }
            }
        }

        model.ComputeMacros(model.BasePose);
        return model;
    }

    private static float F(XElement e, string name, float fallback = 0)
        => (float)Num.Parse((string?)e.Attribute(name), fallback);

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
}

/// <summary>Loads model XML files from a project's models folder and caches them.</summary>
public sealed class ModelLibrary
{
    /// <summary>Files listed in the Project Window but never rendered (speed; they need other files anyway).</summary>
    public static readonly HashSet<string> SkipRender = new(StringComparer.OrdinalIgnoreCase)
    {
        "0.xml", "1.xml", "2.xml", "cap.xml", "cap_2.xml", "GEAR_CAP.xml", "GEAR_SCARF.xml", "GEAR_SHORTS.xml",
        "helper.xml", "hunter.xml", "revolution_girl.xml", "shirt.xml", "tail.xml"
    };

    private readonly IFileSystem _fs;
    private readonly ProjectInfo _project;
    private readonly ConcurrentDictionary<string, Task<ModelData?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ModelLibrary(IFileSystem fs, ProjectInfo project)
    {
        _fs = fs;
        _project = project;
    }

    public async Task<IReadOnlyList<string>> GetModelFilesAsync()
    {
        var files = await _fs.GetFilesAsync(_project.ModelsFolder);
        return files.Where(f => VPath.HasExtension(f, ".xml")).ToList();
    }

    public static bool IsSkipped(string fileOrName)
    {
        string file = fileOrName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? VPath.GetFileName(fileOrName) : fileOrName + ".xml";
        return SkipRender.Contains(file);
    }

    /// <summary>Loads models/NAME.xml (cached). Returns null when it cannot be read.</summary>
    public Task<ModelData?> GetAsync(string name)
    {
        return _cache.GetOrAdd(name, async n =>
        {
            try
            {
                string path = VPath.Combine(_project.ModelsFolder, n + ".xml");
                if (!await _fs.FileExistsAsync(path))
                    return null;
                byte[] data = await _fs.ReadAllBytesAsync(path);
                return await Task.Run(() => ModelData.Parse(n, data));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ModelLibrary] {n}: {ex.Message}");
                return null;
            }
        });
    }

    public async Task<ModelData?> LoadFileAsync(IFileSystem fs, string path)
    {
        try
        {
            byte[] data = await fs.ReadAllBytesAsync(path);
            return await Task.Run(() => ModelData.Parse(VPath.GetFileNameWithoutExtension(path), data));
        }
        catch
        {
            return null;
        }
    }

    public void Invalidate(string? name = null)
    {
        if (name == null) _cache.Clear();
        else _cache.TryRemove(name, out _);
    }
}
