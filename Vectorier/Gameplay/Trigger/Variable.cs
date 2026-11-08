#nullable disable warnings
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger;

public class Variable
{
    public struct TV_Value
    {
        public float asFloat;

        public int asInt;
    }

    private TV_Value value;

    private TV_Value defaultValue;

    protected VariableTypeE type;

    private string name;

    protected string strValue;

    private string defaultStringValue;

    protected TriggerRunner parent;

    public VariableTypeE Type => type;

    public bool IsStringVar => type == VariableTypeE.VT_STRING;

    public virtual int ValueInt => value.asInt;

    public virtual float ValueFloat => value.asFloat;

    public virtual string ValueString => strValue;

    public virtual string DebugStringValue
    {
        get
        {
            switch (type)
            {
                case VariableTypeE.VT_INT:
                    return ValueInt.ToString();
                case VariableTypeE.VT_DOUBLE:
                    return ValueFloat.ToString();
                case VariableTypeE.VT_STRING:
                    return ValueString;
                default:
                    return "Unknown value";
            }
        }
    }

    protected Variable()
    {
    }

    private Variable(int p_value, string p_name)
    {
        defaultValue.asInt = p_value;
        type = VariableTypeE.VT_INT;
        name = p_name;
        parent = null;
        value.asInt = p_value;
        value.asFloat = p_value;
    }

    protected Variable(float p_value, string p_name)
    {
        name = p_name;
        parent = null;
        value.asFloat = p_value;
        value.asInt = (int)p_value;
        type = VariableTypeE.VT_DOUBLE;
        defaultValue.asFloat = p_value;
        defaultValue.asInt = (int)p_value;
    }

    protected Variable(string p_name, TriggerRunner p_parent)
    {
        name = p_name;
        type = VariableTypeE.VT_STRING;
        parent = p_parent;
    }

    protected Variable(string p_value, string p_name, TriggerRunner p_parent)
    {
        name = p_name;
        strValue = p_value;
        type = VariableTypeE.VT_STRING;
        defaultStringValue = p_value;
        parent = p_parent;
        value.asInt = p_value.GetHashCode();
        defaultValue.asInt = p_value.GetHashCode();
    }

    public static Variable createVariable(string p_value, string p_name, TriggerRunner p_parent = null)
    {
        Variable variable = null;
        var type = GetTypeByValue(p_value);
        switch (type)
        {
            case VariableTypeE.VT_INT:
                if (!int.TryParse(p_value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int val))
                {
                    variable = new Variable(p_value, p_name, p_parent);
                    break;
                }
                variable = new Variable(val, p_name);
                break;
            case VariableTypeE.VT_STRING:
                variable = new Variable(p_value, p_name, p_parent);
                break;
            case VariableTypeE.VT_FUNCTION:
                variable = TriggerFunction.Create(p_value, p_name, p_parent);
                break;
            case VariableTypeE.VT_DOUBLE:
                if (!float.TryParse(p_value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float val1))
                {
                    variable = new Variable(p_value, p_name, p_parent);
                    break;
                }
                variable = new Variable(val1, p_name);
                break;
            case VariableTypeE.VT_EXPRESSION:
                variable = new TriggerExpression(p_value, p_name, p_parent);
                break;
        }
        return variable;
    }

    public virtual void setValue(int p_value)
    {
        type = VariableTypeE.VT_INT;
        value.asInt = p_value;
        value.asFloat = p_value;
    }

    public virtual void setValue(float p_value)
    {
        value.asFloat = p_value;
        type = VariableTypeE.VT_DOUBLE;
        value.asInt = (int)p_value;
    }

    public virtual void setValue(string p_value)
    {
        type = VariableTypeE.VT_STRING;
        value.asInt = p_value.GetHashCode();
        strValue = p_value;
    }

    public virtual void appendValue(int p_value)
    {
        value.asInt += p_value;
        value.asFloat += p_value;
    }

    public virtual void appendValue(float p_value)
    {
        value.asFloat += p_value;
        value.asInt += (int)p_value;
    }

    public virtual void appendValue(string p_value)
    {
        strValue += p_value;
        value.asInt = strValue.GetHashCode();
    }

    public bool isEquale(Variable p_value)
    {
        return ValueInt == p_value.ValueInt;
    }

    public bool isGreater(Variable p_value)
    {
        return p_value.ValueInt < ValueInt;
    }

    public bool isLess(Variable p_value)
    {
        return ValueInt < p_value.ValueInt;
    }

    public bool isLessEqual(Variable p_value)
    {
        return ValueInt <= p_value.ValueInt;
    }

    public bool isGreaterEqual(Variable p_value)
    {
        return p_value.ValueInt <= ValueInt;
    }

    public override string ToString()
    {
        return null;
    }

    public void resetValues()
    {
        strValue = defaultStringValue;
        value.asInt = defaultValue.asInt;
        if (type == VariableTypeE.VT_DOUBLE)
        {
            value.asFloat = defaultValue.asFloat;
        }
        else if (type == VariableTypeE.VT_INT)
        {
            value.asInt = defaultValue.asInt;
        }
    }

    public static VariableTypeE GetTypeByValue(string p_value)
    {
        if (p_value.Length == 0)
        {
            return VariableTypeE.VT_STRING;
        }
        if (p_value[0] >= '+' && p_value[0] <= '9' && p_value[0] != '/')
        {
            if (p_value.IndexOf('.') == -1)
            {
                return VariableTypeE.VT_INT;
            }
            return VariableTypeE.VT_DOUBLE;
        }
        if (p_value[0] == '?')
        {
            return VariableTypeE.VT_FUNCTION;
        }
        if (p_value[0] == '{' && p_value[p_value.Length - 1] == '}')
        {
            return VariableTypeE.VT_EXPRESSION;
        }
        return VariableTypeE.VT_STRING;
    }
}
