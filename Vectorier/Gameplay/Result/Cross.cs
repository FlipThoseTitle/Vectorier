#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay.Result;

public struct Cross
{
    private Vector3 _point;

    public int Index { get; }

    public Vector3 Point => _point;

    public Cross(Vector3 point, int index)
    {
        Index = index;
        _point = point;
    }
}
