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

namespace Vectorier.Gameplay
{
    public static class InputManager
    {
        public static bool WasKeyPressedThisFrame(KeyboardKey key)
        {
            return Raylib.IsKeyPressed(key);
        }

        public static bool IsKeyDown(KeyboardKey key)
        {
            return Raylib.IsKeyDown(key);
        }

        public static bool IsKeyUp(KeyboardKey key)
        {
            return Raylib.IsKeyUp(key);
        }

        public static KeyboardKey[] GetPressedKeys()
        {
            List<KeyboardKey> pressedKeys = new();

            foreach (KeyboardKey key in Enum.GetValues<KeyboardKey>())
            {
                if (Raylib.IsKeyDown(key))
                {
                    pressedKeys.Add(key);
                }
            }

            if (pressedKeys.Count == 0)
            {
                pressedKeys.Add(KeyboardKey.Null);
            }

            return pressedKeys.ToArray();
        }
    }
}
