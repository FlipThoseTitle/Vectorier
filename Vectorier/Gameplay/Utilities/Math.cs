#nullable disable warnings
using System;
namespace Vectorier.Gameplay.Utilities;

public static class Math
{

    public static float Round(float Value, float Pow)
    {
        return MathF.Floor(Value * Pow + 0.5f) / Pow;
    }
}
