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
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners
{
    public class CoinRunner : ItemScoreRunner
    {
        private bool _enabled;

        private int _groupID;

        private int _defaultScore;

        public bool enabled => _enabled;

        public Color color;

        public int GroupID => _groupID;

        public CoinRunner(int type, string prefabName, int groupId, int score, float x, float y)
            : base(type, prefabName, score, x, y)
        {
            _groupID = groupId;
            _defaultScore = score;
        }

        public override void Collision(ModelHuman modelHuman)
        {
            if (_enabled)
            {
                base.Collision(modelHuman);
            }
        }

        public override void Init()
        {
            Idle = new AnimationSprite("credits")
            {
                Parent = this,
                FPS = 20,
                LocalPosition = new Vector2(-50, -50),
                Iterations = -1,
                Z = -10 + Z
            };

            Off = new AnimationSprite("credits_off")
            {
                Parent = this,
                FPS = 20,
                LocalPosition = new Vector2(-50, -50),
                Iterations = 1,
                Z = -10 + Z
            };
        }

        public override void InitRunner(Vector2 point)
        {
            base.InitRunner(point);
            SetEnable(false);
        }

        public override void AddBonus(ModelHuman modelHuman)
        {
            SoundsManager.PlaySounds(SoundType.bonus_pickup);
            modelHuman.AddCoin(this);
        }

        public void SetEnable(bool value)
        {
            _enabled = value;
            _score = _defaultScore;
            if (_enabled)
            {
                if (Idle != null)
                    Idle.Play();
            }
            else
            {
                if (Idle != null)
                    Idle.Finish();
            }
        }

        public override void Reset()
        {
            base.Reset();
            SetEnable(false);
        }
    }
}
