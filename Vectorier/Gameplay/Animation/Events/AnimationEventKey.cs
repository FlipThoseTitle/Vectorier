#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Animation.Events;

public class AnimationEventKey : AnimationEvent
{
    public KeyboardKey Key { get; }

    public AnimationEventKey(AnimationEventParam param, XmlNode node)
        : base(param)
    {
        // A <Keys> event without a Key attribute (e.g. a typo like Key1="Up") never fires instead of crashing the load.
        string? key = node.Attributes?["Key"]?.Value;
        if (string.IsNullOrEmpty(key))
        {
            Key = KeyboardKey.Null;
            System.Console.WriteLine($"moves.xml: <Keys> without a Key attribute in {node.ParentNode?.ParentNode?.Name ?? "?"}: ignored.");
            return;
        }
        Key = KeyVariables.Parse(key);
    }

    public bool IsKey(KeyVariables keysVariables, int sign)
    {
        if (sign == -1 && keysVariables.Key == KeyboardKey.Right)
        {
            return Key == KeyboardKey.Left;
        }
        if (sign == -1 && keysVariables.Key == KeyboardKey.Left)
        {
            return Key == KeyboardKey.Right;
        }
        return Key == keysVariables.Key;
    }
}
