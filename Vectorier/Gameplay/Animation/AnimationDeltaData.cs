// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationDeltaData
    {
        public QuadRunner Platform
        {
            get;
        }

        public Vector3 Position
        {
            get;
        }

        public int Sign
        {
            get;
        }

        public AnimationDeltaData(QuadRunner platform, Vector3 position, int sign)
        {
            Platform = platform;
            Position = position;
            Sign = sign;
        }

        public Rectangle GetRectangle(int index)
        {
            var vector = GetVector(index);
            if (vector != null)
            {
                return new Rectangle((float)vector.X, (float)vector.Y, Platform.Width, Platform.Height);
            }
            return null;
        }

        public Vector3 GetVector(int index)
        {
            return Position - Platform.GetCornerByIndex(index);
        }

        public double GetDeltaValue(AnimationDeltaName name, int corner, int sign, Vector3 velocity)
        {
            switch (name)
            {
                case AnimationDeltaName.Width:
                    {
                        return Platform.GetSize(sign).X;
                    }
                case AnimationDeltaName.Height:
                    {
                        return Platform.GetSize(sign).Y;
                    }
                case AnimationDeltaName.DeltaX:
                    {
                        var vector = GetVector(corner);
                        if (sign == -1)
                        {
                            return -vector.X;
                        }
                        return vector.X;
                    }
                case AnimationDeltaName.DeltaY:
                    {
                        return GetVector(corner).Y;
                    }
                case AnimationDeltaName.VelocityX:
                    {
                        return velocity.X;
                    }
                case AnimationDeltaName.VelocityY:
                    {
                        return velocity.Y;
                    }
                default:
                    return 0;
            }
        }

        public string DeltaToString(int corner)
        {
            var vector = GetVector(corner);
            return string.Format("[delta{0} X:{1}, Y:{2}]", corner, vector.X, vector.Y);
        }
    }
}
