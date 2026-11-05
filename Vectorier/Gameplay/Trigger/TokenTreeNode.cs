// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Vectorier.Gameplay.Trigger
{
    public class TokenTreeNode
    {
        public TokenTreeNode Left;

        public TokenTreeNode Right;

        public ExprToken Data;

        public int Level;

        public TokenTreeNode(ExprToken p_data, int p_level)
        {
            Left = Right = null;
            Data = p_data;
            Level = p_level;
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
}
