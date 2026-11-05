// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Models;

namespace Vectorier.Gameplay.Runners
{
    public class PrimitiveAnimatedRunner : PrimitiveRunner
    {
        private ControllerAnimations _ControllerAnimation;

        private AnimationReaction _reaction;

        public ControllerAnimations ControllerAnimation => _ControllerAnimation;

        public override bool IsStrike
        {
            get => false;
            set
            {
            }
        }

        public PrimitiveAnimatedRunner(int type, string p_name, Color p_color, Vector3 p_deltaPosition, float p_impulse, List<string> p_sounds)
            : base(type, p_name, p_color, p_deltaPosition, p_impulse, p_sounds)
        {
            Type = ModelType.PrimitiveAnimated;
        }

        public override void Load()
        {
            base.Load();
            _ControllerAnimation = new ControllerAnimations(Model);
        }

        public override void Render(List<QuadRunner> p_platforms)
        {
            base.Render(p_platforms);
            _ControllerAnimation.Render();
            if (_reaction != null)
            {
                Play();
            }
        }

        public void Play()
        {
            _ControllerAnimation.Play(Animations.Animation[_reaction.Name], false, _reaction.FirstFrame);
            _reaction = null;
        }

        public void PlayReaction(string[] reactions)
        {
            _reaction = AnimationLoader.ParseReaction(reactions);
        }

        public override void Reset()
        {
            _ControllerAnimation.Clear();
            base.Reset();
        }
    }
}
