#nullable disable warnings
using System.Xml;

namespace Vectorier.Gameplay.Trigger.Events;

public class TE_Line : TriggerEvent
{
    private Variable _line;

    private static int _counterID;

    /// <summary>The editor's addition: a new game counts its lines from the start.</summary>
    public static void ResetCounter() => _counterID = 0;

    private string _typeStr;

    public int ID { get; set; }

    public TE_Line(TriggerLoop parent, XmlNode node, int iD = -1)
    {
        _Type = EventType.TET_LINE;
        if (parent == null)
        {
            ID = iD;
            return;
        }
        ID = _counterID++;
        string value = node.Attributes["Position"].Value;
        if (value[0] == '_')
        {
            _line = parent.ParentTrigger.GetVar(value);
        }
        else
        {
            _line = Variable.createVariable(value, string.Empty);
        }
        _typeStr = node.Attributes["Type"].Value;
        parent.SetLine(_typeStr, _line, ID);
    }

    public override bool IsEqual(TriggerEvent value)
    {
        if (!base.IsEqual(value))
        {
            return false;
        }
        return ID == (value as TE_Line).ID;
    }
}
