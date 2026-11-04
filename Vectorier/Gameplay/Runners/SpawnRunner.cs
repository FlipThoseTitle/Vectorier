// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Animation;

namespace Vectorier.Gameplay.Runners
{
    public class SpawnRunner : Runner
    {
        protected string _animations;

        public AnimationReaction Reaction;

        public SpawnRunner()
            : base(0f, 0f)
        {
            TypeClass = RunnerType.Spawn;
        }

        public SpawnRunner(float x, float y, string n, string animation)
            : base(x, y)
        {
            _animations = animation;
            TypeClass = RunnerType.Spawn;
            Name = n;
            Hash = n.GetHashCode();
            if (string.IsNullOrEmpty(_animations))
            {
                return;
            }
            Reaction = AnimationLoader.ParseReaction(_animations.Split('|'));
        }

        public override bool Render()
        {
            return true;
        }
    }
}
