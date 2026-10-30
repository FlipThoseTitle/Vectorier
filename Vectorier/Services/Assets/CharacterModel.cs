using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vectorier.Services.IO;
using Vectorier.Services.Projects;
using Vectorier.Services.Localization;

namespace Vectorier.Services.Assets;

/// <summary>A preview problem shown to the user (conflicting files, unusable animation...).</summary>
public sealed class CharacterPreviewException : Exception
{
    public CharacterPreviewException(string message) : base(message) { }
}

/// <summary>
/// The playable character: models/0.xml (skeleton and required nodes) combined with skin files (1.xml by default).
/// Animations (.bin) move the nodes of 0.xml, in its order.
/// </summary>
public sealed class CharacterModel
{
    public const string BaseFile = "0";
    public const string DefaultSkin = "1";

    private CharacterModel(ModelData model, string[] baseNodes)
    {
        Model = model;
        BaseNodeNames = baseNodes;
        BaseToModel = baseNodes.Select(n => model.NodeIndex.TryGetValue(n, out int i) ? i : -1).ToArray();
    }

    public ModelData Model { get; }

    /// <summary>Every node of 0.xml in file order (what each frame of a .bin animation lists).</summary>
    public string[] BaseNodeNames { get; }

    private int[] BaseToModel { get; }

    public int AnimatedNodeCount => BaseNodeNames.Length;

    /// <summary>The skins of a Model's Skins attribute ("0|cap"): empty means the default 1.xml; 0 is the base itself.</summary>
    public static List<string> SkinsFrom(string? skins)
    {
        var list = (skins ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s != BaseFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Count == 0 ? new List<string> { DefaultSkin } : list;
    }

    /// <summary>Loads 0.xml and the skins. Two files defining the same node, edge or figure is an error.</summary>
    public static async Task<CharacterModel> LoadAsync(ProjectInfo project, IEnumerable<string> skins)
    {
        var fs = AppServices.FileSystem;
        var files = new List<(string Name, XDocument Doc)>();
        foreach (var name in new[] { BaseFile }.Concat(skins))
        {
            string path = VPath.Combine(project.ModelsFolder, name + ".xml");
            if (!await fs.FileExistsAsync(path))
                throw new CharacterPreviewException(Loc.F("Model file not found: models/{0}.xml", name));
            try
            {
                files.Add((name + ".xml", XDocument.Parse(ModelData.DecodeText(await fs.ReadAllBytesAsync(path)))));
            }
            catch (Exception ex)
            {
                throw new CharacterPreviewException(Loc.F("models/{0}.xml could not be read: {1}", name, ex.Message));
            }
        }

        var merged = new XElement("Scene");
        var baseNodes = new List<string>();
        foreach (string section in new[] { "Nodes", "Edges", "Figures" })
        {
            var target = new XElement(section);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (file, doc) in files)
            {
                foreach (var element in doc.Root?.Element(section)?.Elements() ?? Enumerable.Empty<XElement>())
                {
                    string key = element.Name.LocalName;
                    if (owners.TryGetValue(key, out var other) && other != file)
                        throw new CharacterPreviewException(
                            $"Conflicting entries: '{key}' in <{section}> is defined in both {other} and {file}.\nRemove one of them to preview this character.");
                    owners[key] = file;
                    target.Add(new XElement(element));
                    if (section == "Nodes" && file == BaseFile + ".xml")
                        baseNodes.Add(key);
                }
            }
            merged.Add(target);
        }
        var model = ModelData.Parse(string.Join("+", files.Select(f => f.Name)), new XDocument(merged));
        return new CharacterModel(model, baseNodes.ToArray());
    }

    /// <summary>
    /// Reads a .bin animation for this character. Frames with fewer nodes than 0.xml are an error; extra nodes are
    /// ignored. Each frame is returned with 0.xml's node count.
    /// </summary>
    public List<Vector3[]> ReadAnimation(byte[] data)
    {
        var frames = new List<Vector3[]>();
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data));
            int frameCount = reader.ReadInt32();
            for (int f = 0; f < frameCount; f++)
            {
                reader.ReadByte();
                int nodeCount = reader.ReadInt32();
                if (nodeCount < AnimatedNodeCount)
                    throw new CharacterPreviewException(
                        $"The animation has {nodeCount} nodes per frame but 0.xml defines {AnimatedNodeCount}: it cannot be played on this character.");
                var frame = new Vector3[AnimatedNodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
                    if (n < AnimatedNodeCount)
                        frame[n] = new Vector3(x, y, z);
                }
                frames.Add(frame);
            }
        }
        catch (EndOfStreamException)
        {
            throw new CharacterPreviewException(Loc.T("The animation file is truncated."));
        }
        if (frames.Count == 0)
            throw new CharacterPreviewException(Loc.T("The animation has no frames."));
        return frames;
    }

    /// <summary>Pose of the whole model for a (possibly fractional) frame: animated nodes interpolated, macros computed.</summary>
    public Vector3[] Pose(IReadOnlyList<Vector3[]> frames, double frame)
    {
        var pose = (Vector3[])Model.BasePose.Clone();
        if (frames.Count > 0)
        {
            frame = Math.Clamp(frame, 0, frames.Count - 1);
            int a = (int)Math.Floor(frame);
            int b = Math.Min(a + 1, frames.Count - 1);
            float t = (float)(frame - a);
            for (int i = 0; i < BaseToModel.Length; i++)
            {
                int index = BaseToModel[i];
                if (index >= 0)
                    pose[index] = Vector3.Lerp(frames[a][i], frames[b][i], t);
            }
        }
        Model.ComputeMacros(pose);
        return pose;
    }

    public Vector3? NodePosition(Vector3[] pose, string name) =>
        Model.NodeIndex.TryGetValue(name, out int i) ? pose[i] : null;
}
