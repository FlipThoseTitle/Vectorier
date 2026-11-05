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
using System.Xml;
using Vectorier.Gameplay.Controllers;

namespace Vectorier.Gameplay.Animation.Events
{
    public class AnimationEventKey : AnimationEvent
    {
        private readonly KeyboardKey _key;

        public KeyboardKey Key => _key;

        public AnimationEventKey(AnimationEventParam param, XmlNode node)
            : base(param)
        {
            // A <Keys> event without a Key attribute (e.g. a typo like Key1="Up") never fires instead of crashing the load.
            string? key = node.Attributes?["Key"]?.Value;
            if (string.IsNullOrEmpty(key))
            {
                _key = KeyboardKey.Null;
                System.Console.WriteLine($"moves.xml: <Keys> without a Key attribute in {node.ParentNode?.ParentNode?.Name ?? "?"}: ignored.");
                return;
            }
            _key = KeyVariables.Parse(key);
        }

        public bool IsKey(KeyVariables keysVariables, int sign)
        {
            if (sign == -1 && keysVariables.Key == KeyboardKey.Right)
            {
                return _key == KeyboardKey.Left;
            }
            if (sign == -1 && keysVariables.Key == KeyboardKey.Left)
            {
                return _key == KeyboardKey.Right;
            }
            return _key == keysVariables.Key;
        }
    }
}
