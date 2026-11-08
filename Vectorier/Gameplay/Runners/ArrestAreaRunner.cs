#nullable disable warnings
using System;
using System.Collections.Generic;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners;

public class ArrestAreaRunner : AreaRunner
{
    private ModelHuman _activeModel;

    private List<ModelHuman> _arrestModels = new List<ModelHuman>();

    private float _distance;

    public static ArrestAreaRunner ActiveArrest;

    public bool IsArrest { get; private set; }

    public ArrestAreaRunner(float x, float y, float width, float height, string typeName, string name, float distance)
        : base(AreaType.Catch, x, y, width, height, typeName, name)
    {
        IsEnabled = true;
        _distance = distance;
    }


    public override void Activate(ModelHuman modelHuman)
    {
        if (modelHuman.Arrests.Count == 0)
        {
            IsEnabled = false;
        }
        else if (IsEnabled)
        {
            IsArrest = false;
            _activeModel = modelHuman;
            ActiveArrest = this;

            foreach (var model in LevelMainController.current.GetModelsByNames(modelHuman.Arrests))
            {
                var distanceX = (modelHuman.ModelObject.CenterOfMassNode.Start -
                                 model.ModelObject.CenterOfMassNode.Start).X;

                bool valid = (distanceX < 0 && modelHuman.Sign < 0) || (distanceX >= 0 && modelHuman.Sign >= 1);

                if (valid && _distance < MathF.Abs((float)distanceX))
                {
                    IsArrest = true;
                    _arrestModels.Add(model);
                }
            }

        }
    }

    public override void Deactivate(ModelHuman modelHuman)
    {
        IsArrest = false;
        ActiveArrest = null;
    }

    public override void Reset()
    {
        base.Reset();
        _arrestModels.Clear();
        IsEnabled = true;
    }

    public void Arresting()
    {
        foreach (var model in _arrestModels)
        {
            if (model.IsSelf)
            {
                LocationCamera.Current.Node = _activeModel.ModelObject.CameraNode;
                LevelMainController.current.Arrest(model);
            }
        }
    }
}
