// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerModelEffect
    {
        private ModelHuman _modelHuman;

        private AnimationSprite _antibotEffect;

        private AnimationSprite _taserExplosionEffect;

        private Dictionary<ModelNode, AnimationSprite> _paralyzeEffect = new Dictionary<ModelNode, AnimationSprite>();

        private bool _taserEffectActive;

        private List<(ModelNode, AnimationSprite)> _taserEffect = new List<(ModelNode, AnimationSprite)>();

        public IParent Parent;

        public ControllerModelEffect(ModelHuman modelHuman)
        {
            Parent = Sets.Current.Containers[1];
            _modelHuman = modelHuman;
            if (_modelHuman.IsSelf)
            {
                CreateAntibotEffect();
            }
            else
            {
                CreateTaserEffect();
            }
            CreateTaserExplosionEffect();
        }

        private void CreateAntibotEffect()
        {
            _antibotEffect = new AnimationSprite("antibot")
            {
                Parent = Parent,
                Z = -20 + (Parent as VisualContainer).Z,
                FPS = 100,
                Iterations = 1,
                Pivot = new System.Numerics.Vector2(0.5f, 0.5f)
            };
        }

        public void RunAntibotEffect()
        {
            if (_antibotEffect == null)
            {
                Console.WriteLine("Run antibot but effect null");
                return;
            }
            var node = _modelHuman.GetNode("COM");
            _antibotEffect.LocalPosition = new System.Numerics.Vector2(node.Start.X, node.Start.Y);
            _antibotEffect.Play();
        }

        private void CreateTaserExplosionEffect()
        {
            _taserExplosionEffect = new AnimationSprite("lightning_expl_v2")
            {
                Parent = Parent,
                Z = -20 + (Parent as VisualContainer).Z,
                FPS = 100,
                Iterations = 1,
                Pivot = new System.Numerics.Vector2(0.5f, 0.5f),
                LocalScale = new System.Numerics.Vector2(2, 2)
            };

        }

        public void RunTaserExplosion()
        {
            if (_taserExplosionEffect == null)
            {
                Console.WriteLine("Run TaserExplosion but effect null");
                return;
            }
            var node = _modelHuman.GetNode("COM");

            _taserExplosionEffect.LocalPosition = new System.Numerics.Vector2(node.Start.X, node.Start.Y);
            _taserExplosionEffect.Play();
        }

        private void CreateTaserEffect()
        {

            var taser1 = new AnimationSprite("lightning_hands")
            {
                Parent = Parent,
                Z = -20 + (Parent as VisualContainer).Z,
                Pivot = new Vector2(0.5f, 0.5f)
            };
            var taser2 = new AnimationSprite("lightning_hands")
            {
                Parent = Parent,
                Z = -20 + (Parent as VisualContainer).Z,
                Pivot = new Vector2(0.5f, 0.5f)
            };

            _taserEffect.Add((_modelHuman.GetNode("NKnuckles_1"), taser1));
            _taserEffect.Add((_modelHuman.GetNode("NKnuckles_2"), taser2));
        }

        public void RunTaserEffect()
        {
            _taserEffectActive = true;
            foreach (var item in _taserEffect)
            {
                item.Item2.Play();
            }
        }

        public void StopTaserEffect()
        {
            _taserEffectActive = false;
            foreach (var item in _taserEffect)
            {
                item.Item2.Finish();
            }
        }

        private void RenderTasetEffect()
        {
            if (!_taserEffectActive)
            {
                return;
            }
            foreach (var item in _taserEffect)
            {
                item.Item2.LocalPosition = new Vector2((float)item.Item1.Start.X, (float)item.Item1.Start.Y);
            }
        }

        public void RunParalyzeEffect()
        {
            string[] nodes = {
               "NNeck",
               "NPivot",
                "NKnee_1",
                "NKnee_2",
                "NElbow_1",
                "NElbow_2"
            };
            _paralyzeEffect.Clear();
            foreach (var nodeName in nodes)
            {
                var node = _modelHuman.GetNode(nodeName);
                var animation = new AnimationSprite("lightning_paraliz_v2")
                {
                    Parent = Parent,
                    Z = -20 + (Parent as VisualContainer).Z,
                    Pivot = new Vector2(0.5f, 0.5f)
                };

                animation.Play();

                _paralyzeEffect.Add(node, animation);
            }
        }

        public void ParalyzeRender()
        {
            foreach (var node in _paralyzeEffect.Keys)
            {
                _paralyzeEffect[node].LocalPosition = new Vector2((float)node.Start.X, (float)node.Start.Y);
            }
        }

        public void StopParalyzeEffect()
        {
            foreach (var effect in _paralyzeEffect.Values)
            {
                effect.Finish();
            }
            _paralyzeEffect.Clear();
        }

        public void Render()
        {
            ParalyzeRender();
            RenderTasetEffect();
        }

        public void Reset()
        {
            if (_antibotEffect != null)
                _antibotEffect.Finish();
            _taserExplosionEffect.Finish();
            StopParalyzeEffect();
            StopTaserEffect();
        }
    }
}
