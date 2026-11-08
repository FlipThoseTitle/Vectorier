using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace Vectorier.Services.MoveVisualizer;

/// <summary>
/// Playback of a node animation bin (port of Unity's Vectorier.Model.ModelAnimation).
/// All positions are in the animation space: Unity-like coordinates (Y up), relative to the host element
/// when the animation was placed on a Spawn / Area (otherwise level coordinates with Y flipped).
/// </summary>
public sealed class MoveAnimation
{
    public const float SourceFps = 20f;
    public const float TargetFps = 60f;

    public float ContinuousFrame { get; set; }

    public List<Vector3[]> AnimationFrames { get; } = new();
    public List<Vector3> CenterOfMassPath { get; } = new();
    public Vector3[]? AnimationNodes { get; private set; }
    public Vector3[]? PreviewNodes { get; private set; }
    public Vector3[]? PreviewPose { get; private set; }

    public int CurrentFrameIndex { get; set; }
    public int CenterOfMassNodeIndex { get; private set; } = -1;
    public Vector3 StartOffset { get; private set; }
    public bool IsOffsetInitialized { get; private set; }
    public bool IsPreviewActive { get; private set; }
    private double _lastPlaybackTime;

    public MoveModel? Model { get; private set; }

    /// <summary>How many nodes the loaded move animates (the first ones of the model).</summary>
    public int AnimatedNodeCount => AnimationFrames.Count > 0 ? AnimationFrames[0].Length : 0;

    private bool _resumePending;

    /// <summary>Resuming restarts the clock: the time spent paused is not played back.</summary>
    public bool IsPlaying
    {
        get;
        set
        {
            if (value && !field)
                _resumePending = true;
            field = value;
        }
    } = true;
    public bool Reverse { get; set; }
    public int StartFrame { get; set; }
    public int EndFrame { get; set; } = int.MaxValue;

    private bool _stayInPlace;
    private int _stayInPlacePivotNodeIndex = -1;

    public void ResetAll()
    {
        ResetRuntimeStateOnly();
        IsPlaying = true;
    }

    public void ResetRuntimeStateOnly()
    {
        AnimationFrames.Clear();
        CenterOfMassPath.Clear();
        AnimationNodes = Model != null ? new Vector3[Model.NodeCount] : null;
        PreviewNodes = Model != null ? new Vector3[Model.NodeCount] : null;
        PreviewPose = null;
        CurrentFrameIndex = 0;
        CenterOfMassNodeIndex = -1;
        StartOffset = default;
        IsOffsetInitialized = false;
        IsPreviewActive = false;
        _lastPlaybackTime = 0;
        _stayInPlace = false;
        _stayInPlacePivotNodeIndex = -1;
    }

    /// <summary>Unity EnsureModelLoadedForPreview: sets the model and (re)builds the preview pose, mirrored when reversed.</summary>
    public void EnsureModelLoadedForPreview(MoveModel model)
    {
        if (!ReferenceEquals(Model, model))
        {
            Model = model;
            AnimationNodes = new Vector3[model.NodeCount];
            PreviewNodes = new Vector3[model.NodeCount];
            PreviewPose = null;
        }
        if (PreviewPose == null || PreviewPose.Length != model.PreviewPose.Length)
            PreviewPose = new Vector3[model.PreviewPose.Length];
        for (int i = 0; i < model.PreviewPose.Length; i++)
        {
            var p = model.PreviewPose[i];
            PreviewPose[i] = new Vector3(Reverse ? -p.X : p.X, p.Y, p.Z);
        }
    }

    public void UpdatePreview(Vector3 target, string pivotNodeName)
    {
        if (Model == null || PreviewPose == null || PreviewNodes == null)
            return;
        int pivotIndex = ResolvePivotNodeIndex(pivotNodeName);
        var offset = target - PreviewPose[pivotIndex];
        for (int i = 0; i < PreviewPose.Length; i++)
            PreviewNodes[i] = PreviewPose[i] + offset;
        IsPreviewActive = true;
    }

    public void ClearPreview() => IsPreviewActive = false;

    /// <summary>Loads the bin and puts the pivot node of the start frame at <paramref name="target"/>.</summary>
    public void PlaceAt(Vector3 target, MoveModel model, byte[] bin, string pivotNodeName, bool stayInPlace, double now)
    {
        Model = model;
        ResetRuntimeStateOnly();
        EnsureModelLoadedForPreview(model);

        LoadBinaryAnimation(bin, model.NodeCount);

        int maxFrame = AnimationFrames.Count - 1;
        StartFrame = Math.Clamp(StartFrame, 0, maxFrame);
        EndFrame = maxFrame;
        if (EndFrame < StartFrame)
            EndFrame = StartFrame;

        CenterOfMassNodeIndex = model.TryGetNodeIndex("COM", out int com) ? com : -1;

        int pivotIndex = ResolvePivotNodeIndex(pivotNodeName);
        SetStayInPlace(stayInPlace, pivotNodeName);

        StartOffset = target - AnimationFrames[StartFrame][pivotIndex];
        IsOffsetInitialized = true;

        PrecomputeCenterOfMassPath();

        CurrentFrameIndex = StartFrame;
        IsPlaying = true;
        ApplyFrame(StartFrame);
        _lastPlaybackTime = now;
    }

    public void ApplyFrame(int frameIndex)
    {
        if (AnimationFrames.Count == 0 || AnimationNodes == null)
            return;
        frameIndex = Math.Clamp(frameIndex, StartFrame, EndFrame);
        ContinuousFrame = frameIndex;

        var frame = AnimationFrames[frameIndex];
        var offset = StartOffset;
        if (_stayInPlace && _stayInPlacePivotNodeIndex >= 0 && _stayInPlacePivotNodeIndex < frame.Length)
            offset += AnimationFrames[StartFrame][_stayInPlacePivotNodeIndex] - frame[_stayInPlacePivotNodeIndex];

        for (int i = 0; i < frame.Length && i < AnimationNodes.Length; i++)
            AnimationNodes[i] = frame[i] + offset;
        CurrentFrameIndex = frameIndex;
    }

    /// <summary>Advances at the source 20 fps, interpolated, updating at most 60 times a second.</summary>
    public bool TryAdvancePlayback(double now)
    {
        if (!IsOffsetInitialized || AnimationFrames.Count == 0)
            return false;
        if (!IsPlaying || _resumePending)
        {
            _lastPlaybackTime = now;
            _resumePending = false;
            return false;
        }

        double dt = now - _lastPlaybackTime;
        if (dt < 1.0 / TargetFps)
            return false;
        _lastPlaybackTime = now;

        ContinuousFrame += (float)(dt * SourceFps);
        if (ContinuousFrame > EndFrame)
            ContinuousFrame = StartFrame + (ContinuousFrame - EndFrame);
        if (ContinuousFrame > EndFrame) // very long pause (window in the background)
            ContinuousFrame = StartFrame;

        CurrentFrameIndex = (int)Math.Floor(ContinuousFrame);
        ApplyInterpolatedFrame(ContinuousFrame);
        return true;
    }

    public void ApplyInterpolatedFrame(float frameTime)
    {
        if (AnimationFrames.Count == 0 || AnimationNodes == null)
            return;
        frameTime = Math.Clamp(frameTime, StartFrame, EndFrame);

        int frameA = (int)Math.Floor(frameTime);
        int frameB = Math.Min(frameA + 1, EndFrame);
        float t = frameTime - frameA;

        var nodesA = AnimationFrames[frameA];
        var nodesB = AnimationFrames[frameB];
        var offset = StartOffset;
        if (_stayInPlace && _stayInPlacePivotNodeIndex >= 0 && _stayInPlacePivotNodeIndex < nodesA.Length)
        {
            var startPivot = AnimationFrames[StartFrame][_stayInPlacePivotNodeIndex];
            var current = Vector3.Lerp(nodesA[_stayInPlacePivotNodeIndex], nodesB[_stayInPlacePivotNodeIndex], t);
            offset += startPivot - current;
        }

        for (int i = 0; i < nodesA.Length && i < AnimationNodes.Length; i++)
            AnimationNodes[i] = Vector3.Lerp(nodesA[i], nodesB[i], t) + offset;
    }

    public Vector3? GetAnimationNode(string nodeName)
    {
        if (AnimationNodes == null || Model == null || !Model.TryGetNodeIndex(nodeName, out int index))
            return null;
        return index >= 0 && index < AnimationNodes.Length ? AnimationNodes[index] : null;
    }

    public int ResolvePivotNodeIndex(string requestedPivot)
    {
        if (Model == null)
            throw new InvalidOperationException("Model must be loaded before resolving pivot node.");
        if (!string.IsNullOrWhiteSpace(requestedPivot) && Model.TryGetNodeIndex(requestedPivot, out int index))
            return index;
        if (Model.TryGetNodeIndex("NPivot", out int pivot))
            return pivot;
        throw new InvalidOperationException("Pivot node not found. Requested pivot missing, and 'NPivot' not found in XML <Nodes>.");
    }

    public void SetStayInPlace(bool enabled, string pivotNodeName)
    {
        _stayInPlace = enabled;
        _stayInPlacePivotNodeIndex = enabled ? ResolvePivotNodeIndex(pivotNodeName) : -1;
    }

    /// <summary>Frame count, then per frame: a byte, the node count and X, Y, Z floats per node.</summary>
    private void LoadBinaryAnimation(byte[] data, int expectedNodeCount)
    {
        AnimationFrames.Clear();
        using var reader = new BinaryReader(new MemoryStream(data));
        int frameCount = reader.ReadInt32();
        for (int f = 0; f < frameCount; f++)
        {
            reader.ReadByte();
            int nodeCount = reader.ReadInt32();
            // The game moves the first nodes of the model, one per entry: a frame cannot list more nodes than it has.
            if (expectedNodeCount > 0 && nodeCount > expectedNodeCount)
                throw new InvalidDataException(string.Format(CultureInfo.InvariantCulture,
                    "Animation node count mismatch. Model has {0} nodes, but frame {1} contains {2}.", expectedNodeCount, f, nodeCount));
            var frame = new Vector3[nodeCount];
            for (int n = 0; n < nodeCount; n++)
            {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                frame[n] = new Vector3(Reverse ? -x : x, y, -z);
            }
            AnimationFrames.Add(frame);
        }
        if (AnimationFrames.Count == 0)
            throw new InvalidDataException("Animation file contains zero frames.");
    }

    private void PrecomputeCenterOfMassPath()
    {
        CenterOfMassPath.Clear();
        if (CenterOfMassNodeIndex < 0)
            return;
        foreach (var frame in AnimationFrames)
            CenterOfMassPath.Add(frame[CenterOfMassNodeIndex] + StartOffset);
    }
}
