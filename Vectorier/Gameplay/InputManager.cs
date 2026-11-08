#nullable disable warnings
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay;

public static class InputManager
{
    public static bool WasKeyPressedThisFrame(KeyboardKey key)
    {
        return Raylib.IsKeyPressed(key);
    }
}
