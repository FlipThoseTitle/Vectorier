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
    public class TC_Compare : TriggerCondition
    {
        protected Variable _Value1;

        protected Variable _Value2;

        public TC_Compare(TriggerLoop p_parent, XmlNode p_node)
            : base(p_parent, p_node)
        {
            _Type = ConditionsType.TCT_None;
            Parse(p_node);
        }

        protected void Parse(XmlNode p_node)
        {
            _Value1 = GetOrCreateVar(p_node.Attributes["Value1"] == null ? p_node.Attributes["Value"].Value : p_node.Attributes["Value1"].Value);
            _Value2 = GetOrCreateVar(p_node.Attributes["Value2"] == null ? p_node.Attributes["Than"].Value : p_node.Attributes["Value2"].Value);
        }

        public override bool Check()
        {
            return false;
        }
    }
}
