#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay.Animation;

public class AnimationDelta
{
    /// <summary>The corner as written in moves.xml (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left; -1 none).</summary>
    public int Corner { get; }

    /// <summary>The element name in moves.xml (Width, DeltaY, VelosityX...).</summary>
    public string XmlName { get; init; } = string.Empty;

    public Vector2 Value { get; }

    public AnimationDeltaName Name { get; }

    public AnimationDeltaType Type { get; }

    public AnimationDelta(AnimationDeltaName name, AnimationDeltaType type, Vector2 value, int corner)
    {
        Name = name;
        Corner = corner;
        Value = value;
        Type = type;
    }

    public int GetCorner(int sign)
    {
        if (sign == -1)
        {
            switch (Corner)
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
        return Corner;
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
