#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Trigger.Events;

namespace Vectorier.Gameplay.Trigger;

public class TriggerLine
{
    private TriggerLineType _type;

    private Variable _line;

    private int _eventID;

    private TriggerRunner _parent;

    private Dictionary<ModelHuman, bool> _statusModel = new Dictionary<ModelHuman, bool>();

    public TriggerLine(TriggerRunner parent, int eventID)
    {
        _parent = parent;
        _type = TriggerLineType.NONE;
        _eventID = eventID;
        _line = null;
    }

    public void Check(ModelHuman model)
    {
        ModelNode modelNode = model.GetNode(_parent.NodeName);
        int num = 0;
        bool flag = false;
        int num2 = 0;
        switch (_type)
        {
            case TriggerLineType.VERTICAL:
                num2 = (int)(_parent.LocalPosition.X + (float)_line.ValueInt);
                num = (int)modelNode.Start.X;
                flag = (modelNode.Start - modelNode.End).X > 0f;
                break;
            case TriggerLineType.HORIZONTAL:
                num2 = (int)(_parent.LocalPosition.Y + (float)_line.ValueInt);
                num = (int)modelNode.Start.Y;
                flag = true;
                break;
        }
        bool flag2 = (flag && num2 <= num) || (!flag && num < num2);
        if (!_statusModel.ContainsKey(model))
        {
            _statusModel[model] = flag2;
            return;
        }
        bool flag3 = _statusModel[model] != flag2;
        _statusModel[model] = flag2;
        if (flag3)
        {
            TE_Line p_event = new TE_Line(null, null, _eventID);
            _parent.CheckEvent(p_event, model);
        }
    }

    public void SetLine(string type, Variable line)
    {
        _type = GetLineType(type);
        _line = line;
    }

    public void Reset()
    {
        _statusModel.Clear();
    }

    public TriggerLineType GetLineType(string type)
    {
        if (type == "Vertical")
        {
            return TriggerLineType.VERTICAL;
        }
        if (type == "Horizontal")
        {
            return TriggerLineType.HORIZONTAL;
        }
        return TriggerLineType.NONE;
    }
}
