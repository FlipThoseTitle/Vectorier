#nullable disable warnings
using System;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger;

public class TriggerExpression : Variable
{
    private ExprParser _parser;

    private float _result;

    private bool _isCalculated;

    public override int ValueInt
    {
        get
        {
            CalculateExpression();
            return (int)_result;
        }
    }

    public override float ValueFloat
    {
        get
        {
            CalculateExpression();
            return _result;
        }
    }

    public TriggerExpression(string value, string name, TriggerRunner parent) : base(value, name, parent)
    {
        type = VariableTypeE.VT_EXPRESSION;
        value = value.Replace("{", string.Empty);
        value = value.Replace("}", string.Empty);
        value = value.Replace(" ", string.Empty);
        _parser = ExprParser.Create(value, parent);
        if (_parser == null)
        {
            Console.WriteLine("TRIGGER EXPRESSION: expression is incorrect!");
        }
        else if (_parser.RequiredVariables.Count == 0)
        {
            _isCalculated = _parser.TryGetResult(ref _result);
        }
        else
        {
            InitVariables();
        }
    }

    private void InitVariables()
    {
        if (_parser.RequiredVariables.Count == 0)
        {
            return;
        }
        foreach (string requiredVariable in _parser.RequiredVariables)
        {
            Variable p_var;
            if (GetTypeByValue(requiredVariable) == VariableTypeE.VT_FUNCTION)
            {
                p_var = createVariable(requiredVariable, string.Empty, parent);
                _parser.AddVariable(requiredVariable, p_var);
                continue;
            }
            p_var = parent.GetVar(requiredVariable);
            if (p_var != null)
            {
                _parser.AddVariable(requiredVariable, p_var);
            }
            else
            {
                Console.WriteLine("In Expression {" + strValue + "} unknown var= " + requiredVariable);
            }
        }
        _parser.RequiredVariables.Clear();
    }

    public void CalculateExpression()
    {
        if (!_isCalculated)
        {
            InitVariables();
            _isCalculated = _parser.TryGetResult(ref _result);
        }
    }
}
