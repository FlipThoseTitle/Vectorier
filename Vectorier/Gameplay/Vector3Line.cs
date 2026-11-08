#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

public class Vector3Line
{
    public Vector3 Start;

    public Vector3 End;

    public double Stroke { get; set; }

    public Color Color { get; set; }

    public double Distance => Vector3.Distance(Start, End);

    public Vector3Line()
    {
    }

    public Vector3Line(Vector3 start, Vector3 end)
    {
        Stroke = 1;
        Start = start;
        End = end;
    }

    public void Set(Vector3 start, Vector3 end)
    {
        Start = start;
        End = end;
    }

    public void SetZerroOnZ()
    {
        var start = Start;
        start.Z = 0;
        Start = start;

        var end = End;
        end.Z = 0;
        End = end;
    }
}
