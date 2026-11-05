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

namespace Vectorier.Gameplay.Trigger
{
    public class TriggerExpression : Variable
    {
        private ExprParser _Parser;

        private float _Result;

        private bool _IsCalculated;

        public override int ValueInt
        {
            get
            {
                CalculateExpression();
                return (int)_Result;
            }
        }

        public override float ValueFloat
        {
            get
            {
                CalculateExpression();
                return _Result;
            }
        }

        public TriggerExpression(string p_value, string p_name, TriggerRunner p_parent) : base(p_value, p_name, p_parent)
        {
            type = VariableTypeE.VT_EXPRESSION;
            p_value = p_value.Replace("{", string.Empty);
            p_value = p_value.Replace("}", string.Empty);
            p_value = p_value.Replace(" ", string.Empty);
            _Parser = ExprParser.Create(p_value, p_parent);
            if (_Parser == null)
            {
                Console.WriteLine("TRIGGER EXPRESSION: expression is incorrect!");
            }
            else if (_Parser.RequiredVariables.Count == 0)
            {
                _IsCalculated = _Parser.TryGetResult(ref _Result);
            }
            else
            {
                InitVariables();
            }
        }

        private void InitVariables()
        {
            if (_Parser.RequiredVariables.Count == 0)
            {
                return;
            }
            foreach (string requiredVariable in _Parser.RequiredVariables)
            {
                Variable p_var;
                if (GetTypeByValue(requiredVariable) == VariableTypeE.VT_FUNCTION)
                {
                    p_var = createVariable(requiredVariable, string.Empty, parent);
                    _Parser.AddVariable(requiredVariable, p_var);
                    continue;
                }
                p_var = parent.GetVar(requiredVariable);
                if (p_var != null)
                {
                    _Parser.AddVariable(requiredVariable, p_var);
                }
                else
                {
                    Console.WriteLine("In Expression {" + strValue + "} unknown var= " + requiredVariable);
                }
            }
            _Parser.RequiredVariables.Clear();
        }

        public void CalculateExpression()
        {
            if (!_IsCalculated)
            {
                InitVariables();
                _IsCalculated = _Parser.TryGetResult(ref _Result);
            }
        }
    }
}
