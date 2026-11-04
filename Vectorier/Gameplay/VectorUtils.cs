// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Xml;

namespace Vectorier.Gameplay
{
    public static class VectorUtils
    {
        public static Vector3 Add(this ref Vector3 v1, Vector3 v2)
        {
            v1.X += v2.X;
            v1.Y += v2.Y;
            v1.Z += v2.Z;
            return v1;
        }

        public static Vector3 Add(this ref Vector3 v1, Vector2 v2)
        {
            v1.X += v2.X;
            v1.Y += v2.Y;
            return v1;
        }

        public static Vector2 Add(this ref Vector2 v1, Vector2 v2)
        {
            v1.X += v2.X;
            v1.Y += v2.Y;
            return v1;
        }

        public static Vector2 Add(this ref Vector2 v1, Vector3 v2)
        {
            v1.X += v2.X;
            v1.Y += v2.Y;
            return v1;
        }

        public static Vector3 Subtract(this ref Vector3 v1, Vector3 v2)
        {
            v1.X -= v2.X;
            v1.Y -= v2.Y;
            v1.Z -= v2.Z;
            return v1;
        }

        public static Vector2 Subtract(this ref Vector2 v1, Vector2 v2)
        {
            v1.X -= v2.X;
            v1.Y -= v2.Y;
            return v1;
        }

        public static Vector3 Add(this ref Vector3 v1, float num)
        {
            v1.X += num;
            v1.Y += num;
            v1.Z += num;
            return v1;
        }

        public static Vector2 Add(this ref Vector2 v1, float num)
        {
            v1.X += num;
            v1.Y += num;
            return v1;
        }

        public static Vector3 Subtract(this ref Vector3 v1, float num)
        {
            v1.X -= num;
            v1.Y -= num;
            v1.Z -= num;
            return v1;
        }

        public static Vector2 Subtract(this ref Vector2 v1, float num)
        {
            v1.X -= num;
            v1.Y -= num;
            return v1;
        }

        public static Vector3 Add(this ref Vector3 v1, Vector3 v2, float multy)
        {
            v1.X += v2.X * multy;
            v1.Y += v2.Y * multy;
            v1.Z += v2.Z * multy;
            return v1;
        }

        public static Vector2 Add(this ref Vector2 v1, Vector2 v2, float multy)
        {
            v1.X += v2.X * multy;
            v1.Y += v2.Y * multy;
            return v1;
        }

        public static Vector2 Add(this ref Vector2 v1, float x, float y)
        {
            v1.X += x;
            v1.Y += y;
            return v1;
        }

        public static Vector3 Multiply(this ref Vector3 v1, Vector3 v2)
        {
            return Vector3.Multiply(v1, v2);
        }

        public static Vector2 Multiply(this ref Vector2 v1, Vector2 v2)
        {
            return Vector2.Multiply(v1, v2);
        }

        public static float MultiplyNum(this ref Vector3 v1, Vector3 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        public static float MultiplyNum(this ref Vector2 v1, Vector2 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y;
        }

        public static Vector3 Multiply(this ref Vector3 v1, double num)
        {
            return Vector3.Multiply((float)num, v1);
        }

        public static Vector2 Multiply(this ref Vector2 v1, double num)
        {
            return Vector2.Multiply((float)num, v1);
        }

        public static Vector3 Multiply(this ref Vector3 v1, float num)
        {
            return Vector3.Multiply(num, v1);
        }

        public static Vector3 Scale(this Vector3 v1, float num)
        {
            return Vector3.Multiply(num, v1);
        }

        public static Vector2 Multiply(this ref Vector2 v1, float num)
        {
            return Vector2.Multiply(num, v1);
        }

        public static Vector3 Normalize(this ref Vector3 v)
        {
            return Vector3.Normalize(v);
        }

        public static Vector2 Normalize(this ref Vector2 v)
        {
            return Vector2.Normalize(v);
        }

        public static Vector3 Closest(Vector3 point, Vector3 linePoint, Vector3 lineDirection)
        {
            Vector3 Vector3 = point - linePoint;
            float num = Vector3.MultiplyNum(lineDirection);
            float num2 = lineDirection.MultiplyNum(lineDirection);
            float num3 = 0f;
            if (num2 != 0f)
            {
                num3 = num / num2;
            }
            return linePoint + lineDirection.Multiply(num3);
        }

        public static Vector2 Closest(Vector2 point, Vector2 linePoint, Vector2 lineDirection)
        {
            Vector2 Vector3 = point - linePoint;
            float num = Vector3.MultiplyNum(lineDirection);
            float num2 = lineDirection.MultiplyNum(lineDirection);
            float num3 = 0f;
            if (num2 != 0f)
            {
                num3 = num / num2;
            }
            return linePoint + lineDirection.Multiply(num3);
        }

        public static Vector3? Cross(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
        {
            float num = (v2.Y - v1.Y) * (v3.X - v4.X) - (v3.Y - v4.Y) * (v2.X - v1.X);
            float num2 = (v2.Y - v1.Y) * (v3.X - v1.X) - (v3.Y - v1.Y) * (v2.X - v1.X);
            float num3 = (v3.Y - v1.Y) * (v3.X - v4.X) - (v3.Y - v4.Y) * (v3.X - v1.X);
            if ((double)num == 0.0 && (double)num2 == 0.0 && (double)num3 == 0.0)
            {
                return null;
            }
            if ((double)num == 0.0)
            {
                return null;
            }
            float num4 = num2 / num;
            float num5 = num3 / num;
            float p_x = v1.X + (v2.X - v1.X) * num5;
            float p_y = v1.Y + (v2.Y - v1.Y) * num5;
            if (0f < num4 && num4 < 1f && 0f < num5 && num5 < 1f)
            {
                return new Vector3(p_x, p_y, 0f);
            }
            return null;
        }

        public static Vector2 Cross(Vector2 v1, Vector2 v2, Vector2 v3, Vector2 v4)
        {
            var num = v2.Y - v1.Y;
            var num2 = v2.X - v1.X;
            var num3 = v3.X - v4.X;
            var num4 = v3.Y - v4.Y;

            var num5 = num * num3 - num4 * num2;
            if (num5 == 0)
            {
                return new Vector2(float.NaN, float.NaN);
            }
            var num6 = v3.X - v1.X;
            var num7 = v3.Y - v1.Y;
            var num8 = (num * num6 - num7 * num2) / num5;

            if (num8 > 0 && num8 < 1)
            {
                var num9 = (num3 * num7 - num4 * num6) / num5;
                if (num9 > 0 && num9 < 1)
                {
                    return new Vector2(v1.X + num2 * num9, v1.Y + num * num9);
                }
            }
            return new Vector2(float.NaN, float.NaN);
        }

        public static Vector3 Middle(Vector3 v1, Vector3 v2)
        {
            return v1 + (v2 - v1) * 0.5f;
        }

        public static void Middle(Vector3 v1, Vector3 v2, ref Vector3 p_result)
        {
            p_result.X = v1.X + (v2.X - v1.X) * 0.5f;
            p_result.Y = v1.Y + (v2.Y - v1.Y) * 0.5f;
            p_result.Z = v1.Z + (v2.Z - v1.Z) * 0.5f;
        }

        public static float Factor(Vector3 point, Vector3 start, Vector3 end)
        {
            var a = new Vector3(start.X, start.Y, 0);
            var b = new Vector3(end.X, end.Y, 0);
            var p = new Vector3(point.X, point.Y, 0);

            float length = Vector3.Distance(a, b);
            if (length <= 1e-6)
                return 0.5f;

            float t = Vector3.Distance(a, p) / length;
            return System.MathF.Max(0.0f, System.MathF.Min(1.0f, t));
        }

        public static Vector2 Set(this ref Vector2 v1, Vector2 v2)
        {
            v1.X = v2.X;
            v1.Y = v2.Y;
            return v1;
        }

        public static Vector2 Set(this ref Vector2 v1, float x, float y)
        {
            v1.X = x;
            v1.Y = y;
            return v1;
        }

        public static Vector3 Set(this ref Vector3 v1, Vector3 v2)
        {
            v1.X = v2.X;
            v1.Y = v2.Y;
            v1.Z = v2.Z;
            return v1;
        }

        public static Vector3 Set(this ref Vector3 v1, float x, float y, float z)
        {
            v1.X = x;
            v1.Y = y;
            v1.Z = z;
            return v1;
        }

        public static Vector3 Reset(this ref Vector3 vector)
        {
            vector.X = 0;
            vector.Y = 0;
            vector.Z = 0;
            return vector;
        }

        public static Vector2 Reset(this ref Vector2 vector)
        {
            vector.X = 0;
            vector.Y = 0;
            return vector;
        }

        public static Vector2 Round(this ref Vector2 vector, int pow)
        {
            vector.X = Utilities.Math.Round(vector.X, pow);
            vector.Y = Utilities.Math.Round(vector.Y, pow);
            return vector;
        }

        public static Vector2 Round(this ref Vector2 vector)
        {
            vector.X = (int)MathF.Round(vector.X);
            vector.Y = (int)MathF.Round(vector.Y);
            return vector;
        }

        public static Vector2? Create(XmlNode Node)
        {
            if (Node == null)
            {
                return null;
            }
            Vector2 vector3f = new Vector2(0f, 0f);
            try
            {
                vector3f.X = float.Parse(Node.Attributes["X"].Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                throw new Exception("Error : parse X eeror type");
            }
            try
            {
                vector3f.Y = float.Parse(Node.Attributes["Y"].Value, CultureInfo.InvariantCulture);
                return vector3f;
            }
            catch
            {
                throw new Exception("Error : parse Y eeror type");
            }
        }

        public static Vector2 Clone(this ref Vector2 vector)
        {
            return new Vector2(vector.X, vector.Y);
        }

        public static Vector2 Clone(this Vector2 vector)
        {
            return new Vector2(vector.X, vector.Y);
        }
    }
}
