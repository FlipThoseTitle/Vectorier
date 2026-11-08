#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay.Frame;

public class Provider
{
    public Vector3[][] Data { get; }

    public int Length => Data.Length;

    public Vector3[] this[int index] => null;

    public Provider(int count)
    {
        Data = new Vector3[count][];
    }

    public void Add(Vector3[] item, int index)
    {
        Data[index] = item;
    }
}
