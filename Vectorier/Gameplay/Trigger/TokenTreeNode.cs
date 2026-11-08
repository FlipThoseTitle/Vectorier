#nullable disable warnings
namespace Vectorier.Gameplay.Trigger;

public class TokenTreeNode
{
    public TokenTreeNode Left;

    public TokenTreeNode Right;

    public ExprToken Data;

    public int Level;

    public TokenTreeNode(ExprToken data, int level)
    {
        Left = Right = null;
        Data = data;
        Level = level;
    }

    public void DeleteTree()
    {
        if (Left != null)
        {
            Left.DeleteTree();
            Left = null;
        }
        if (Right != null)
        {
            Right.DeleteTree();
            Right = null;
        }
        Data = null;
    }
}
