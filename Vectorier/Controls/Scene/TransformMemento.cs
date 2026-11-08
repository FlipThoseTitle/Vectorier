using System.Collections.Generic;
using Vectorier.Models.Scene;

namespace Vectorier.Controls.Scene;

/// <summary>Transform values of a set of elements and their descendants, restored before each step of a drag.</summary>
internal sealed class TransformMemento
{
    private readonly record struct State(SceneElement Element, double X, double Y, double W, double H,
        double Rotation, double Skew, bool FlipX, bool FlipY, double BaseHeight, bool AutoSize);

    private readonly List<State> _states = new();

    public TransformMemento(IEnumerable<SceneElement> roots)
    {
        foreach (var root in roots)
        {
            foreach (var e in SceneDocument.Walk(root))
            {
                var image = e as ImageElement;
                _states.Add(new State(e, e.X, e.Y, e.Width, e.Height,
                    image?.Rotation ?? 0, image?.Skew ?? 0, image?.FlipX ?? false, image?.FlipY ?? false,
                    (e as TrapezoidElement)?.BaseHeight ?? 0, image?.AutoSize ?? false));
            }
        }
    }

    public void Restore()
    {
        foreach (var s in _states)
        {
            var e = s.Element;
            e.X = s.X;
            e.Y = s.Y;
            e.Width = s.W;
            e.Height = s.H;
            if (e is ImageElement image)
            {
                image.Rotation = s.Rotation;
                image.Skew = s.Skew;
                image.FlipX = s.FlipX;
                image.FlipY = s.FlipY;
                image.AutoSize = s.AutoSize;
            }
            else if (e is TrapezoidElement trapezoid)
            {
                trapezoid.BaseHeight = s.BaseHeight;
            }
        }
    }

    public (double X, double Y) StartPosition(SceneElement element)
    {
        foreach (var s in _states)
        {
            if (ReferenceEquals(s.Element, element))
                return (s.X, s.Y);
        }
        return (element.X, element.Y);
    }
}
