// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Numerics;

namespace Vectorier.Gameplay
{
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

        public static Vector3 CrossLine(Vector3Line line1, Vector3Line line2)
        {
            return Cross(line1.Start, line1.End, line2.Start, line2.End);
        }

        public static Vector3 Cross(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
        {
            var num = v2.Y - v1.Y;
            var num2 = v2.X - v1.X;
            var num3 = v3.X - v4.X;
            var num4 = v3.Y - v4.Y;

            var num5 = num * num3 - num4 * num2;
            if (num5 == 0)
            {
                return new Vector3(float.NaN, float.NaN, float.NaN);
            }
            var num6 = v3.X - v1.X;
            var num7 = v3.Y - v1.Y;
            var num8 = (num * num6 - num7 * num2) / num5;

            if (num8 > 0 && num8 < 1)
            {
                var num9 = (num3 * num7 - num4 * num6) / num5;
                if (num9 > 0 && num9 < 1)
                {
                    return new Vector3(v1.X + num2 * num9, v1.Y + num * num9, 0);
                }
            }
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        public bool CroosLine(Vector2 p_point1, Vector2 p_point2)
        {
            double num = (p_point2.Y - p_point1.Y) * (Start.X - End.X) - (Start.Y - End.Y) * (p_point2.X - p_point1.X);
            double num2 = (p_point2.Y - p_point1.Y) * (Start.X - p_point1.X) - (Start.Y - p_point1.Y) * (p_point2.X - p_point1.X);
            double num3 = (Start.Y - p_point1.Y) * (Start.X - End.X) - (Start.Y - End.Y) * (Start.X - p_point1.X);
            if (num == 0.0 && num2 == 0.0 && num3 == 0.0)
            {
                return false;
            }
            if (num == 0.0)
            {
                return false;
            }
            double num4 = num2 / num;
            double num5 = num3 / num;
            if (0f < num4 && num4 < 1f && 0f < num5 && num5 < 1f)
            {
                return true;
            }
            return false;
        }
    }
}
