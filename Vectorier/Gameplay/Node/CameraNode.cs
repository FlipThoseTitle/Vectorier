#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay.Node;

public class CameraNode
{
    public ModelNode Node { get; }

    public Vector3 End => Node == null ? new Vector3() : Node.End;

    public Vector3 Start => Node == null ? new Vector3() : Node.Start;

    public CameraNode(ModelNode node)
    {
        if (node == null)
        {
            return;
        }
        Node = node;
        Node.Radius = 8;
    }
}
