// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationDelta
    {
        private readonly int _corner;

        /// <summary>The corner as written in moves.xml (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left; -1 none).</summary>
        public int Corner => _corner;

        /// <summary>The element name in moves.xml (Width, DeltaY, VelosityX...).</summary>
        public string XmlName { get; init; } = string.Empty;

        public Vector2 Value
        {
            get;
        }

        public AnimationDeltaName Name
        {
            get;
        }

        public AnimationDeltaType Type
        {
            get;
        }

        public AnimationDelta(AnimationDeltaName name, AnimationDeltaType type, Vector2 value, int corner)
        {
            Name = name;
            _corner = corner;
            Value = value;
            Type = type;
        }

        public int GetCorner(int sign)
        {
            if (sign == -1)
            {
                switch (_corner)
                {
                    case 0:
                        return 1;
                    case 1:
                        return 0;
                    case 2:
                        return 3;
                    case 3:
                        return 2;
                }
            }
            return _corner;
        }

        public bool IsInterval(double value)
        {
            return More(value, Value.X) && Less(value, Value.Y);
        }

        public static bool More(double target, float value)
        {
            return float.IsNaN(value) || target >= value;
        }

        public static bool Less(double target, float value)
        {
            return float.IsNaN(value) || target <= value;
        }
    }
}
