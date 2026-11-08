#nullable disable warnings
using System;
using System.Numerics;
using System.Xml;

namespace Vectorier.Gameplay;

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

    public static Vector3 Multiply(this ref Vector3 v1, float num)
    {
        return Vector3.Multiply(num, v1);
    }

    public static Vector3 Scale(this Vector3 v1, float num)
    {
        return Vector3.Multiply(num, v1);
    }

    public static Vector3 Normalize(this ref Vector3 v)
    {
        return Vector3.Normalize(v);
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

    public static Vector3 Middle(Vector3 v1, Vector3 v2)
    {
        return v1 + (v2 - v1) * 0.5f;
    }

    public static void Middle(Vector3 v1, Vector3 v2, ref Vector3 result)
    {
        result.X = v1.X + (v2.X - v1.X) * 0.5f;
        result.Y = v1.Y + (v2.Y - v1.Y) * 0.5f;
        result.Z = v1.Z + (v2.Z - v1.Z) * 0.5f;
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

    public static Vector2? Create(XmlNode Node)
    {
        if (Node == null)
        {
            return null;
        }
        Vector2 vector3f = new Vector2(0f, 0f);
        try
        {
            vector3f.X = float.Parse(Node.Attributes["X"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            throw new Exception("Error : parse X eeror type");
        }
        try
        {
            vector3f.Y = float.Parse(Node.Attributes["Y"].Value, System.Globalization.CultureInfo.InvariantCulture);
            return vector3f;
        }
        catch
        {
            throw new Exception("Error : parse Y eeror type");
        }
    }

    public static Vector2 Clone(this Vector2 vector)
    {
        return new Vector2(vector.X, vector.Y);
    }
}
