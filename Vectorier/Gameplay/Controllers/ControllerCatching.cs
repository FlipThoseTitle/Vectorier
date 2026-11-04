// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerCatching
    {
        private ModelHuman _Model;

        private List<string> _Mureders;

        private bool _IsPlay;

        private Dictionary<ModelHuman, int> _Targets = new Dictionary<ModelHuman, int>();

        public static float DistanceFactor = 250;

        public static float Timeout = 0.35f;

        public static float HeightFactor = 2;

        public ControllerCatching(ModelHuman model)
        {
            _Model = model;
            _Mureders = _Model.Murders;
        }

        public void Play()
        {
            SoundsManager.PlaySounds(SoundType.enemy_charge);
            _IsPlay = true;
        }

        public void Stop()
        {
            _IsPlay = false;
        }

        public void Catch(ModelHuman modelHuman)
        {
            Stop();
            modelHuman.StartPhysics();
            if (modelHuman.IsSelf)
            {
                LevelMainController.current.Murder(modelHuman);
            }
            else
            {
                if (LevelMainController.IsHunterMode)
                {
                    LevelMainController.current.Win(modelHuman, 1);
                }
            }
            SoundsManager.PlaySounds(SoundType.enemy_discharge);
            //GamepadController.Instance.Rumble(0.7f, 0.7f,0.5f);
        }

        public void Render(ModelHuman modelHuman)
        {
            if (modelHuman == null)
            {
                return;
            }
            if (_Mureders.Contains(modelHuman.Name))
            {
                if (IsActive(modelHuman))
                {
                    if (_Targets.ContainsKey(modelHuman))
                    {
                        if (_Targets[modelHuman] != -1)
                        {
                            _Targets[modelHuman] -= 1;
                            if (_Targets[modelHuman] == 0)
                            {
                                Catch(modelHuman);
                            }
                            return;
                        }
                    }
                    _Targets[modelHuman] = (int)(Timeout * 60);
                    if (_IsPlay)
                    {
                        return;
                    }
                    Play();
                }
                else
                {
                    if (!_Targets.ContainsKey(modelHuman))
                    {
                        return;
                    }
                    _Targets.Remove(modelHuman);
                    Stop();
                }
            }
        }

        public bool IsActive(ModelHuman modelHuman)
        {
            if (!_Model.IsEnabled || !modelHuman.IsEnabled || _Model.IsPhysics || modelHuman.IsPhysics || _Model.LockInterval)
            {
                return false;
            }
            return Distance(_Model.ModelObject.CenterOfMassNode, modelHuman.ModelObject.CenterOfMassNode);
        }

        public bool Distance(ModelNode node1, ModelNode node2)
        {
            var vector = new Vector3();
            vector.X = MathF.Abs((float)node2.Start.X - (float)node1.Start.X);
            vector.Y = MathF.Abs((float)node2.Start.Y - (float)node1.Start.Y) * HeightFactor;

            return Vector3.Distance(vector, Vector3.Zero) < DistanceFactor;
        }

        public void Reset()
        {
            _IsPlay = false;
        }
    }
}
