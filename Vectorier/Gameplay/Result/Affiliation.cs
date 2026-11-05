// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Vectorier.Gameplay.Result
{
    public class Affiliation
    {
        public List<Cross> CrossList1 = new List<Cross>();

        public List<Cross> CrossList2 = new List<Cross>();

        public List<Cross> CrossList3 = new List<Cross>();

        public int Type;

        public bool Hits;

        public void Clear()
        {
            Type = -1;
            CrossList1.Clear();
            CrossList2.Clear();
            CrossList3.Clear();
        }

        public void SetType(bool hit1, bool hit2)
        {
            Hits = hit1 || hit2;
            if (CrossList1.Count > 0 && CrossList2.Count > 0)
            {
                Type = 1;
            }
            else if (CrossList1.Count > 0)
            {
                Type = 2;
            }
            else if (CrossList2.Count > 0)
            {
                Type = 3;
            }
            else if (CrossList3.Count > 0)
            {
                Type = 4;
            }
            else if (hit1)
            {
                Type = 5;
            }
            else if (hit2)
            {
                Type = 6;
            }
        }
    }
}
