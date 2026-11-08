using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Services.Assets;

namespace Vectorier.Services.MoveVisualizer;

/// <summary>
/// The Move Visualizer's character: Editor/MoveVisualizer/runner.xml loaded as the game loads a model (see
/// <see cref="ModelView"/>), used by both Spawn Modes. The animation bins store one position per node for the first
/// nodes, in file order; the others (macro nodes, cloth) follow as in the game. <see cref="PreviewPose"/> is in the
/// animation space (Y up).
/// </summary>
public sealed class MoveModel
{
    private MoveModel(byte[] data, ModelView view)
    {
        Data = data;
        Placed = view;
        Ghost = ModelView.Load(new[] { (FileName, data) });
        foreach (var node in view.Object.NodesAll)
        {
            NodeIndexByName[node.Name] = NodeNamesOrdered.Count;
            NodeNamesOrdered.Add(node.Name);
        }
        PreviewPose = view.RestPose.Select(ToSpace).ToArray();
        MaxRadius = view.MaxRadius;
    }

    public const string FileName = "runner.xml";

    /// <summary>runner.xml as read (the Gameplay character is made from it too).</summary>
    public byte[] Data { get; }

    /// <summary>The placed character (Animation mode): posed by the move, its cloth by physics.</summary>
    public ModelView Placed { get; }

    /// <summary>The placement ghost: a still pose.</summary>
    public ModelView Ghost { get; }

    public List<string> NodeNamesOrdered { get; } = new();
    public Dictionary<string, int> NodeIndexByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The pose shown while placing (animation space): runner.xml's rest pose, or RunForward's (see UsePose).</summary>
    public Vector3[] PreviewPose { get; private set; }

    /// <summary>The same pose in the game's space (level units, Y down), for the Gameplay mode.</summary>
    public Vector3[] GamePose => PreviewPose.Select(ToSpace).ToArray();

    /// <summary>
    /// The preview pose from a frame of a move bin (animation space, the model's first nodes); the nodes the bin
    /// does not move (macro, cloth) keep their rest place relative to the COM.
    /// </summary>
    public void UsePose(Vector3[] frame)
    {
        var rest = Placed.RestPose.Select(ToSpace).ToArray();
        var pose = rest.ToArray();
        int count = Math.Min(frame.Length, pose.Length);
        for (int i = 0; i < count; i++)
            pose[i] = frame[i];
        if (count < pose.Length)
        {
            var shift = TryGetNodeIndex("COM", out int com) && com < count ? frame[com] - rest[com] : count > 0 ? frame[0] - rest[0] : Vector3.Zero;
            for (int i = count; i < pose.Length; i++)
                pose[i] = rest[i] + shift;
        }
        PreviewPose = pose;
    }

    /// <summary>One frame of a move bin, in the animation space (as MoveAnimation reads it).</summary>
    public static Vector3[] ReadFrame(byte[] bin, int frameIndex)
    {
        using var reader = new BinaryReader(new MemoryStream(bin));
        int frameCount = reader.ReadInt32();
        if (frameCount <= 0)
            throw new InvalidDataException("Animation file contains zero frames.");
        frameIndex = Math.Clamp(frameIndex, 0, frameCount - 1);
        for (int f = 0; ; f++)
        {
            reader.ReadByte();
            int nodeCount = reader.ReadInt32();
            if (f < frameIndex)
            {
                reader.BaseStream.Seek(nodeCount * 12L, SeekOrigin.Current);
                continue;
            }
            var frame = new Vector3[nodeCount];
            for (int n = 0; n < nodeCount; n++)
                frame[n] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), -reader.ReadSingle());
            return frame;
        }
    }
    public int NodeCount => NodeNamesOrdered.Count;

    /// <summary>Largest capsule radius (for bounds).</summary>
    public float MaxRadius { get; }

    public bool TryGetNodeIndex(string name, out int index) => NodeIndexByName.TryGetValue(name, out index);

    /// <summary>The game's node (level units, Y down) ↔ the animation space (Y up, Z toward the viewer).</summary>
    public static Vector3 ToSpace(Vector3 game) => new(game.X, -game.Y, -game.Z);

    public static MoveModel Parse(byte[] data)
    {
        ModelView view;
        try
        {
            view = ModelView.Load(new[] { (FileName, data) });
        }
        catch (ModelLoadException ex)
        {
            throw new FormatException(FileName + ": " + ex.Message);
        }
        if (view.NodeCount <= 0)
            throw new FormatException("XML <Nodes> is empty.");
        return new MoveModel(data, view);
    }
}
