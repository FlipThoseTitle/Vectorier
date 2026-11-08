#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Trigger;

public class ExprToken
{
    public enum TypeToken
    {
        NOTHING = -1,
        NUMBER,
        VARIABLE,
        OPERATOR,
        UNKNOWN
    }

    public enum OperatorID
    {
        ADD,
        SUB,
        MULT,
        DIV,
        MOD,
        POW,
        ROOT,
        BRACK_L,
        BRACK_R,
        UNKONOWN
    }

    public static Dictionary<OperatorID, int> OPERATOR_PRIORITY = new Dictionary<OperatorID, int>();

    public Variable Var { get; private set; }

    public OperatorID Operator { get; private set; }

    public TypeToken Type { get; private set; }

    public int OperatorPriority { get => OPERATOR_PRIORITY[Operator] + field * OPERATOR_PRIORITY[OperatorID.BRACK_L]; set; }

    public static ExprToken create(Variable var, TypeToken type, OperatorID @operator = OperatorID.UNKONOWN)
    {
        ExprToken exprToken = new ExprToken();
        exprToken.Var = var;
        exprToken.Type = type;
        exprToken.Operator = @operator;
        return exprToken;
    }

    public static ExprToken create(Variable var)
    {
        ExprToken exprToken = new ExprToken();
        exprToken.Var = var;
        exprToken.Type = TypeToken.NUMBER;
        exprToken.Operator = OperatorID.UNKONOWN;
        return exprToken;
    }

    private ExprToken()
    {
        Type = TypeToken.NOTHING;
        Operator = OperatorID.UNKONOWN;
        OperatorPriority = 1;
        Var = null;
    }

    public void reset(Variable var)
    {
        Var = var;
        Type = TypeToken.NUMBER;
        Operator = OperatorID.UNKONOWN;
    }
}
