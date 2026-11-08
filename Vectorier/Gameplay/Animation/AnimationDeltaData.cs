#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Animation;

public class AnimationDeltaData
{
    public QuadRunner Platform { get; }

    public Vector3 Position { get; }

    public int Sign { get; }

    public AnimationDeltaData(QuadRunner platform, Vector3 position, int sign)
    {
        Platform = platform;
        Position = position;
        Sign = sign;
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
}
