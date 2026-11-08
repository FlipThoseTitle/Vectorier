#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerAreas
{
    private ModelHuman _model;

    private ModelNode _comNode;

    public List<AreaRunner> ActiveAreas { get; } = new List<AreaRunner>();

    public ControllerAreas(ModelHuman model)
    {
        _model = model;
        _comNode = model.GetNode("COM");
    }

    private bool IsInside(AreaRunner value)
    {
        return value.Hit(_comNode.Start);
    }

    private bool IsActive(AreaRunner value)
    {
        return ActiveAreas.Contains(value);
    }

    private void Activate(AreaRunner value)
    {
        ActiveAreas.Add(value);
        value.Activate(_model);
        _model.OnActiveArea(value);
    }

    private void Deactivate(AreaRunner value)
    {
        _model.CheckDelayAction(value);
        value.Deactivate(_model);
        ActiveAreas.Remove(value);
    }

    public void Check(AreaRunner value)
    {
        bool flag = IsInside(value);
        if (IsActive(value))
        {
            if (!flag)
            {
                Deactivate(value);
            }
        }
        else if (flag)
        {
            Activate(value);
        }
    }

    public void Reset()
    {
        ActiveAreas.Clear();
    }
}
