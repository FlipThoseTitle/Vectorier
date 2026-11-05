// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Conditions
{
    public class TC_Less : TC_Compare
    {
        public TC_Less(TriggerLoop p_parent, XmlNode p_node)
            : base(p_parent, p_node)
        {
        }

        public override bool Check()
        {
            if (_IsNot)
            {
                return !_Value1.isLess(_Value2);
            }
            return _Value1.isLess(_Value2);
        }

        public override string ToString()
        {
            string text = "Less: " + (!_IsNot ? "(" : "!(");
            string text2 = text;
            return text2 + _Value1 + "<" + _Value2 + ")";
        }
    }
}
