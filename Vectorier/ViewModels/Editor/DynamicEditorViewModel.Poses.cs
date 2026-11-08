using System;
using Avalonia.Media;
using Vectorier.Models.Scene;

namespace Vectorier.ViewModels.Editor;

public partial class DynamicEditorViewModel
{
    private static DynamicKey Capture(SceneElement e, int frame)
    {
        var key = new DynamicKey
        {
            Frame = frame,
            X = e.X,
            Y = e.Y,
            Width = e.Width,
            Height = e.Height,
            Rotation = e is ImageElement image ? image.Rotation : 0,
            Color = e is ImageElement img ? img.Color : Colors.White,
            SupportX = 0.5,
            SupportY = 0.5,
            Ease = EasePreset.Linear,
        };
        return key;
    }

    private int _posing;

    /// <summary>
    /// True while the editor is writing a previewed pose (playback, scrubbing, the rest pose for saving...). Those
    /// changes are local to this computer: Live Collaboration does not send them.
    /// </summary>
    public bool IsPosing => _posing > 0;

    /// <summary>The element is bound to a timeline here, so its pose may be a preview.</summary>
    public bool IsTracked(SceneElement element)
    {
        foreach (var track in Tracks)
            if (ReferenceEquals(track.Element, element))
                return true;
        return false;
    }

    /// <summary>
    /// The element's shared data changed (keys that may move its rest pose, or its transformations): Live
    /// Collaboration compares it again, at its rest pose. Previewed poses are never sent.
    /// </summary>
    private void Share(DynamicTrack track) => _editor.NotifyElementChanged(track.Element, nameof(SceneElement.Dynamics));

    private void Apply(SceneElement e, DynamicKey pose)
    {
        _posing++;
        try
        {
            using (_editor.SuppressTracking())
            {
                e.X = pose.X;
                e.Y = pose.Y;
                if (e.HasSize)
                {
                    if (e is ImageElement { AutoSize: true } auto && Math.Abs(auto.Width - pose.Width) < 1e-9 && Math.Abs(auto.Height - pose.Height) < 1e-9)
                    {
                        // unchanged: keep it auto-sized
                    }
                    else if (Math.Abs(e.Width - pose.Width) > 1e-9 || Math.Abs(e.Height - pose.Height) > 1e-9)
                    {
                        e.Width = pose.Width;
                        e.Height = pose.Height;
                    }
                }
                if (e is ImageElement image)
                {
                    image.Rotation = pose.Rotation;
                    image.Color = pose.Color;
                }
            }
        }
        finally
        {
            _posing--;
        }
    }

    private static double Bez(double t, double p0, double p1, double p2)
    {
        double u = 1 - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }

    private static double LerpAngle(double a, double b, double t)
    {
        double d = ((b - a) % 360 + 540) % 360 - 180;
        return a + d * t;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Pose of a track at a frame (quadratic Bezier on position, linear size/color, shortest-angle rotation).</summary>
    public DynamicKey Evaluate(DynamicTrack track, int frame)
    {
        var k = track.Keys;
        if (k.Count == 0) return track.Original.Clone();
        if (k.Count == 1 || frame <= k[0].Frame) return k[0].Clone();
        if (frame >= k[^1].Frame) return k[^1].Clone();

        int i = 0;
        for (; i < k.Count - 1; i++)
        {
            if (frame >= k[i].Frame && frame <= k[i + 1].Frame)
                break;
        }
        var a = k[i];
        var b = k[i + 1];
        double t = (frame - a.Frame) / (double)Math.Max(1, b.Frame - a.Frame);
        var (cx, cy) = ControlPoint(a, b);

        var pose = b.Clone();
        pose.Frame = frame;
        pose.X = Bez(t, a.X, cx, b.X);
        pose.Y = Bez(t, a.Y, cy, b.Y);
        pose.Width = a.Width + (b.Width - a.Width) * t;
        pose.Height = a.Height + (b.Height - a.Height) * t;
        pose.Rotation = LerpAngle(a.Rotation, b.Rotation, t);
        pose.Color = Lerp(a.Color, b.Color, t);
        return pose;
    }

    /// <summary>Absolute Bezier control point between two keys.</summary>
    public (double X, double Y) ControlPoint(DynamicKey a, DynamicKey b)
    {
        if (CustomEase || b.Ease == EasePreset.Custom)
            return (a.X + b.SupportX, a.Y + b.SupportY);
        double s = DynamicKey.PresetValue(b.Ease);
        return (a.X + (b.X - a.X) * s, a.Y + (b.Y - a.Y) * s);
    }

    private void ApplyFrameToAll()
    {
        foreach (var track in Tracks)
        {
            if (track.Keys.Count > 0)
                Apply(track.Element, Evaluate(track, TrackFrame(track)));
        }
        _editor.InvalidateScene();
    }
}
