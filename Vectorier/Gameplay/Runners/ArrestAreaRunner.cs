// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Camera;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners
{
    public class ArrestAreaRunner : AreaRunner
    {
        private bool _IsArrest;

        private ModelHuman _ActiveModel;

        private List<ModelHuman> _ArrestModels = new List<ModelHuman>();

        private float _Distance;

        public static ArrestAreaRunner ActiveArrest;

        public bool IsArrest => _IsArrest;

        public ArrestAreaRunner(float x, float y, float width, float height, string typeName, string name, float distance)
            : base(AreaType.Catch, x, y, width, height, typeName, name)
        {
            IsEnabled = true;
            _Distance = distance;
        }


        public override void Activate(ModelHuman modelHuman)
        {
            if (modelHuman.Arrests.Count == 0)
            {
                IsEnabled = false;
            }
            else if (IsEnabled)
            {
                _IsArrest = false;
                _ActiveModel = modelHuman;
                ActiveArrest = this;

                foreach (var model in LevelMainController.current.GetModelsByNames(modelHuman.Arrests))
                {
                    var distanceX = (modelHuman.ModelObject.CenterOfMassNode.Start -
                                     model.ModelObject.CenterOfMassNode.Start).X;

                    bool valid = (distanceX < 0 && modelHuman.Sign < 0) || (distanceX >= 0 && modelHuman.Sign >= 1);

                    if (valid && _Distance < MathF.Abs((float)distanceX))
                    {
                        _IsArrest = true;
                        _ArrestModels.Add(model);
                    }
                }

            }
        }

        public override void Deactivate(ModelHuman modelHuman)
        {
            _IsArrest = false;
            ActiveArrest = null;
        }

        public override void Reset()
        {
            base.Reset();
            _ArrestModels.Clear();
            IsEnabled = true;
        }

        public void Arresting()
        {
            foreach (var model in _ArrestModels)
            {
                if (model.IsSelf)
                {
                    LocationCamera.Current.Node = _ActiveModel.ModelObject.CameraNode;
                    LevelMainController.current.Arrest(model);
                }
            }
        }
    }
}
