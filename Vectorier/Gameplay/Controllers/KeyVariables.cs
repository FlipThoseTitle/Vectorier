#nullable disable warnings
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Controllers;

public class KeyVariables
{

    public KeyboardKey Key { get; }

    public KeyVariables(string key)
    {
        Key = Parse(key);
    }

    public KeyVariables(KeyboardKey key)
    {
        Key = key;
    }

    public static KeyboardKey Parse(string key)
    {
        switch (key)
        {
            case "Up":
                return KeyboardKey.Up;
            case "Down":
                return KeyboardKey.Down;
            case "Left":
                return KeyboardKey.Left;
            case "Right":
                return KeyboardKey.Right;
        }
        return KeyboardKey.Null;
    }

    public bool IsEqual(KeyboardKey key)
    {
        return Key == key;
    }

    public bool IsEqual(KeyVariables keysVariables)
    {
        return Key == keysVariables.Key;
    }

    public override string ToString()
    {
        return Key.ToString();
    }
}
