#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Controllers;

public class ControllerCatching
{
    private ModelHuman _model;

    private List<string> _mureders;

    private bool _isPlay;

    private Dictionary<ModelHuman, int> _targets = new Dictionary<ModelHuman, int>();

    public static float DistanceFactor = 250;

    public static float Timeout = 0.35f;

    public static float HeightFactor = 2;

    public ControllerCatching(ModelHuman model)
    {
        _model = model;
        _mureders = _model.Murders;
    }

    public void Play()
    {
        SoundsManager.PlaySounds(SoundType.enemy_charge);
        _model.controllerModelEffect.RunTaserEffect();
        _isPlay = true;
    }

    public void Stop()
    {
        _model.controllerModelEffect.StopTaserEffect();
        _isPlay = false;
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
        modelHuman.controllerModelEffect.RunParalyzeEffect();
        _model.controllerModelEffect.RunTaserExplosion();
    }

    public void Render(ModelHuman modelHuman)
    {
        if (modelHuman == null)
        {
            return;
        }
        if (_mureders.Contains(modelHuman.Name))
        {
            if (IsActive(modelHuman))
            {
                if (_targets.ContainsKey(modelHuman))
                {
                    if (_targets[modelHuman] != -1)
                    {
                        _targets[modelHuman] -= 1;
                        if (_targets[modelHuman] == 0)
                        {
                            Catch(modelHuman);
                        }
                        return;
                    }
                }
                _targets[modelHuman] = (int)(Timeout * 60);
                if (_isPlay)
                {
                    return;
                }
                Play();
            }
            else
            {
                if (!_targets.ContainsKey(modelHuman))
                {
                    return;
                }
                _targets.Remove(modelHuman);
                Stop();
            }
        }
    }

    public bool IsActive(ModelHuman modelHuman)
    {
        if (!_model.IsEnabled || !modelHuman.IsEnabled || _model.IsPhysics || modelHuman.IsPhysics || _model.LockInterval)
        {
            return false;
        }
        return Distance(_model.ModelObject.CenterOfMassNode, modelHuman.ModelObject.CenterOfMassNode);
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
        _model.controllerModelEffect.StopTaserEffect();
        _isPlay = false;
    }
}
