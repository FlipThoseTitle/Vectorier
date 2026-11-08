#nullable disable warnings
using System;
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Trigger;

public class ExprParser
{
    private TriggerRunner _parent;

    private string _expression;

    private int _position;
    private bool _needReCallc;

    private TokenTreeNode _root;

    private List<ExprToken> _tokenList;

    private List<ExprToken> _tokensForRemove;

    public HashSet<string> RequiredVariables { get; }

    public static ExprParser Create(string value, TriggerRunner parent)
    {
        ExprParser exprParser = new ExprParser();
        exprParser._expression = value;
        exprParser._parent = parent;
        exprParser._needReCallc = false;
        if (!exprParser.Parse())
        {
            exprParser = null;
            return null;
        }
        exprParser.ApplyTokenRules();
        exprParser.BuildTree(exprParser._tokenList, ref exprParser._root, 0);
        return exprParser;
    }

    private ExprParser()
    {
        if (ExprToken.OPERATOR_PRIORITY.Count == 0)
        {
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.BRACK_L] = 99;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.BRACK_R] = 99;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.ROOT] = 4;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.POW] = 4;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.MOD] = 3;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.MULT] = 3;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.DIV] = 3;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.ADD] = 1;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.SUB] = 2;
            ExprToken.OPERATOR_PRIORITY[ExprToken.OperatorID.UNKONOWN] = 100;
        }
        _root = null;
        _position = 0;
        RequiredVariables = new HashSet<string>();
        _tokenList = new List<ExprToken>();
        _tokensForRemove = new List<ExprToken>();
    }

    private bool Parse()
    {
        RequiredVariables.Clear();
        ExprToken exprToken = null;
        while (_position < _expression.Length)
        {
            exprToken = GetNextToken();
            if (exprToken == null)
            {
                break;
            }
            _tokenList.Add(exprToken);
        }
        if (_position < _expression.Length)
        {
            Clear(true);
            return false;
        }
        return true;
    }

    private void ApplyTokenRules()
    {
        if (_tokenList.Count > 1 && (_tokenList[0].Operator == ExprToken.OperatorID.ADD || _tokenList[0].Operator == ExprToken.OperatorID.SUB))
        {
            _tokenList.Insert(0, ExprToken.create(Variable.createVariable("0", "number")));
        }
        int num = _tokenList.Count - 3;
        for (int i = 0; i < num; i++)
        {
            if (_tokenList[i].Operator == ExprToken.OperatorID.BRACK_L && _tokenList[i + 1].Operator == ExprToken.OperatorID.SUB && _tokenList[i + 2].Type == ExprToken.TypeToken.NUMBER && _tokenList[i + 3].Operator == ExprToken.OperatorID.BRACK_R)
            {
                _tokenList.Insert(i + 1, ExprToken.create(Variable.createVariable("0", "number")));
                i += 3;
            }
        }
        SetOperatorsPriority();
    }

    private void SetOperatorsPriority()
    {
        int num = 1;
        foreach (ExprToken token in _tokenList)
        {
            if (token.Operator == ExprToken.OperatorID.BRACK_L || token.Operator == ExprToken.OperatorID.BRACK_R)
            {
                num += token.Operator == ExprToken.OperatorID.BRACK_L ? 1 : -1;
                _tokensForRemove.Add(token);
            }
            else
            {
                token.OperatorPriority = num;
            }
        }
        ForDeleteClear();
    }

    private void BuildTree(List<ExprToken> tokens, ref TokenTreeNode node, int level)
    {
        if (tokens.Count != 0)
        {
            int minPriorityTokenIndex = GetMinPriorityTokenIndex(tokens);
            node = new TokenTreeNode(tokens[minPriorityTokenIndex], level);
            level++;
            List<ExprToken> list = new List<ExprToken>();
            List<ExprToken> list2 = new List<ExprToken>();
            for (int i = 0; i < minPriorityTokenIndex; i++)
            {
                list.Add(tokens[i]);
            }
            for (int j = minPriorityTokenIndex + 1; j < tokens.Count; j++)
            {
                list2.Add(tokens[j]);
            }
            if (list.Count != 0)
            {
                BuildTree(list, ref node.Left, level);
            }
            if (list2.Count != 0)
            {
                BuildTree(list2, ref node.Right, level);
            }
        }
    }

    private static int GetMinPriorityTokenIndex(List<ExprToken> tokens)
    {
        int num = int.MaxValue;
        int result = 0;
        for (int num2 = tokens.Count - 1; num2 >= 0; num2--)
        {
            int operatorPriority = tokens[num2].OperatorPriority;
            if (operatorPriority < num)
            {
                num = operatorPriority;
                result = num2;
            }
        }
        return result;
    }

    public bool TryGetResult(ref float result)
    {
        result = CalcTree(_root);
        Clear();
        return !_needReCallc;
    }

    private float CalcTree(TokenTreeNode node)
    {
        if (node != null)
        {
            if (node.Data.Type == ExprToken.TypeToken.OPERATOR)
            {
                return GetOperatorResult(node.Data.Operator, node.Left, node.Right);
            }
            switch (node.Data.Var.Type)
            {
                case VariableTypeE.VT_DOUBLE:
                case VariableTypeE.VT_FUNCTION:
                case VariableTypeE.VT_EXPRESSION:
                    return node.Data.Var.ValueFloat;
                default:
                    return node.Data.Var.ValueInt;
            }
        }
        return 0f;
    }

    private float GetOperatorResult(ExprToken.OperatorID @operator, TokenTreeNode left, TokenTreeNode right)
    {
        switch (@operator)
        {
            case ExprToken.OperatorID.ADD:
                return CalcTree(left) + CalcTree(right);
            case ExprToken.OperatorID.SUB:
                return CalcTree(left) - CalcTree(right);
            case ExprToken.OperatorID.MULT:
                return CalcTree(left) * CalcTree(right);
            case ExprToken.OperatorID.DIV:
                return CalcTree(left) / CalcTree(right);
            case ExprToken.OperatorID.MOD:
                return CalcTree(left) % CalcTree(right);
            case ExprToken.OperatorID.POW:
                return (float)Math.Pow(CalcTree(left), CalcTree(right));
            default:
                return 0f;
        }
    }

    private ExprToken GetNextToken()
    {
        if (_position >= _expression.Length)
        {
            return null;
        }
        if (isDelimeter(_expression[_position]))
        {
            return GetOperatorToken();
        }
        if (isDigit(_expression[_position]))
        {
            return GetNumberToken();
        }
        if (isLetter(_expression[_position]))
        {
            return GetVariableToken();
        }
        return null;
    }

    private ExprToken GetOperatorToken()
    {
        ExprToken.OperatorID p_operator = ExprToken.OperatorID.UNKONOWN;
        switch (_expression[_position++])
        {
            case '+':
                p_operator = ExprToken.OperatorID.ADD;
                break;
            case '-':
                p_operator = ExprToken.OperatorID.SUB;
                break;
            case '*':
                p_operator = ExprToken.OperatorID.MULT;
                break;
            case '/':
                p_operator = ExprToken.OperatorID.DIV;
                break;
            case '%':
                p_operator = ExprToken.OperatorID.MOD;
                break;
            case '^':
                p_operator = ExprToken.OperatorID.POW;
                break;
            case '|':
                p_operator = ExprToken.OperatorID.ROOT;
                break;
            case '(':
                p_operator = ExprToken.OperatorID.BRACK_L;
                break;
            case ')':
                p_operator = ExprToken.OperatorID.BRACK_R;
                break;
        }
        return ExprToken.create(null, ExprToken.TypeToken.OPERATOR, p_operator);
    }

    private ExprToken GetNumberToken()
    {
        string text = string.Empty;
        while (_expression.Length > _position && isDigit(_expression[_position]))
        {
            text += _expression[_position++];
        }
        return ExprToken.create(Variable.createVariable(text, "number"));
    }

    private ExprToken GetVariableToken()
    {
        string text = string.Empty;
        while (_expression.Length > _position && (isLetter(_expression[_position]) || isDigit(_expression[_position])))
        {
            text += _expression[_position++];
        }
        RequiredVariables.Add(text);
        _needReCallc = true;
        return ExprToken.create(Variable.createVariable(text, "var", _parent), ExprToken.TypeToken.VARIABLE);
    }

    private bool isDelimeter(char c)
    {
        if (c == '\0')
        {
            return false;
        }
        return "+-/*%^!()".IndexOf(c) != -1;
    }

    private bool isDigit(char c)
    {
        if (c == '\0')
        {
            return false;
        }
        return "0123456789.".IndexOf(c) != -1;
    }

    private bool isLetter(char c)
    {
        if (c == '\0')
        {
            return false;
        }
        return !isDigit(c) && !isDelimeter(c);
    }

    public void AddVariable(string name, Variable var)
    {
        if (name.Contains("?"))
        {
            return;
        }
        foreach (ExprToken token in _tokenList)
        {
            if (token.Var != null && token.Var.IsStringVar && token.Var.ValueString == name)
            {
                token.reset(var);
            }
        }
    }

    private void ForDeleteClear(bool destroy = true)
    {
        if (_tokensForRemove.Count == 0)
        {
            return;
        }
        foreach (ExprToken item in _tokensForRemove)
        {
            _tokenList.Remove(item);
        }
        _tokensForRemove.Clear();
    }

    private void Clear(bool fromDestructor = false)
    {
        ForDeleteClear();
        if (fromDestructor)
        {
            _tokenList.Clear();
            if (_root != null)
            {
                _root.DeleteTree();
            }
        }
    }
}
