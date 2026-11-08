#nullable disable warnings
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Functions;

namespace Vectorier.Gameplay.Trigger;

public class TriggerFunction : Variable
{
    protected TriggerFunction(string name, TriggerRunner parent) : base(name, parent)
    {
        type = VariableTypeE.VT_FUNCTION;
    }

    public static TriggerFunction Create(string value, string name, TriggerRunner parent)
    {
        string text = value.Substring(0, value.IndexOf("["));
        if (text.IndexOf("getModel") != -1)
        {
            return new TF_Model(value, name, parent);
        }
        return null;
    }

    public static void InitFuncVar(TriggerRunner parent, ref Variable var, string name)
    {
        if (name.Length != 0 && name[0] == '_' && parent != null)
        {
            var = parent.GetVar(name);
        }
        else
        {
            var = createVariable(name, string.Empty, parent);
        }
        // An unknown variable (GetVar logs it) must not crash the level: it reads as an empty name.
        var ??= createVariable(string.Empty, name, parent);
    }
}
