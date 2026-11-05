// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Functions;

namespace Vectorier.Gameplay.Trigger
{
    public class TriggerFunction : Variable
    {
        protected TriggerFunction(string p_name, TriggerRunner p_parent) : base(p_name, p_parent)
        {
            type = VariableTypeE.VT_FUNCTION;
        }

        public static TriggerFunction Create(string p_value, string p_name, TriggerRunner p_parent)
        {
            string text = p_value.Substring(0, p_value.IndexOf("["));
            if (text.IndexOf("getModel") != -1)
            {
                return new TF_Model(p_value, p_name, p_parent);
            }
            return null;
        }

        public static void InitFuncVar(TriggerRunner p_parent, ref Variable p_var, string p_name)
        {
            if (p_name.Length != 0 && p_name[0] == '_' && p_parent != null)
            {
                p_var = p_parent.GetVar(p_name);
            }
            else
            {
                p_var = createVariable(p_name, string.Empty, p_parent);
            }
            // An unknown variable (GetVar logs it) must not crash the level: it reads as an empty name.
            p_var ??= createVariable(string.Empty, p_name, p_parent);
        }
    }
}
