#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Result;

public class Collision
{
    public ModelLine Edge;

    public Vector3 Point;

    public QuadRunner Platform;

    public Model Model;
}
