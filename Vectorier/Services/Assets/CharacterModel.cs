using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Vectorier.Gameplay.Animation;
using Vectorier.Services.IO;
using Vectorier.Services.Localization;
using Vectorier.Services.Projects;

namespace Vectorier.Services.Assets;

/// <summary>A preview problem shown to the user (conflicting files, unusable animation...).</summary>
public sealed class CharacterPreviewException : Exception
{
    public CharacterPreviewException(string message) : base(message) { }
}

/// <summary>
/// The playable character as the game loads it: models/0.xml (skeleton and required nodes) then the skin files
/// (1.xml by default), one game model (<see cref="ModelView"/>). Animations (.bin) move the nodes of 0.xml, in its
/// order; the skins' cloth nodes are moved by the game's physics.
/// </summary>
public sealed class CharacterModel
{
    public const string BaseFile = "0";
    public const string DefaultSkin = "1";

    private CharacterModel(ModelView view, int animatedNodes)
    {
        View = view;
        AnimatedNodeCount = animatedNodes;
    }

    public ModelView View { get; }

    /// <summary>The nodes of 0.xml: what each frame of a .bin animation lists.</summary>
    public int AnimatedNodeCount { get; }

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
        var files = new List<(string Name, byte[] Data)>();
        foreach (var name in new[] { BaseFile }.Concat(skins).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = VPath.Combine(project.ModelsFolder, name + ".xml");
            if (!await fs.FileExistsAsync(path))
                throw new CharacterPreviewException(Loc.F("Model file not found: models/{0}.xml", name));
            files.Add((name + ".xml", await fs.ReadAllBytesAsync(path)));
        }
        try
        {
            var view = await Task.Run(() => ModelView.Load(files, rejectConflicts: true));
            return new CharacterModel(view, view.FileNodeCounts.Count > 0 ? view.FileNodeCounts[0] : 0);
        }
        catch (ModelLoadException ex)
        {
            throw new CharacterPreviewException(ex.Message);
        }
    }

    /// <summary>
    /// Reads a .bin animation for this character with the game's reader. Frames with fewer nodes than 0.xml are an
    /// error; extra nodes are ignored.
    /// </summary>
    public List<Vector3[]> ReadAnimation(byte[] data)
    {
        Gameplay.Frame.Provider provider;
        try
        {
            provider = AnimationBinaryParser.Parse(data);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new CharacterPreviewException(Loc.T("The animation file is truncated."));
        }
        var frames = new List<Vector3[]>();
        foreach (var frame in provider.Data)
        {
            if (frame.Length < AnimatedNodeCount)
                throw new CharacterPreviewException(
                    $"The animation has {frame.Length} nodes per frame but 0.xml defines {AnimatedNodeCount}: it cannot be played on this character.");
            frames.Add(frame);
        }
        if (frames.Count == 0)
            throw new CharacterPreviewException(Loc.T("The animation has no frames."));
        return frames;
    }

    /// <summary>The animated nodes for a (possibly fractional) frame, interpolated (level units, Y down).</summary>
    public Vector3[] Pose(IReadOnlyList<Vector3[]> frames, double frame)
    {
        var pose = new Vector3[AnimatedNodeCount];
        frame = Math.Clamp(frame, 0, frames.Count - 1);
        int a = (int)Math.Floor(frame);
        int b = Math.Min(a + 1, frames.Count - 1);
        float t = (float)(frame - a);
        for (int i = 0; i < pose.Length; i++)
            pose[i] = Vector3.Lerp(frames[a][i], frames[b][i], t);
        return pose;
    }
}
