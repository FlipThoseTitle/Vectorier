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
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers
{
    public class ControllerStatistics
    {
        private Dictionary<AnimationInfo, int> _animations = new Dictionary<AnimationInfo, int>();

        private Dictionary<GameEndType, int> _gameOvers = new Dictionary<GameEndType, int>();

        //private Dictionary<GadgetType, int> _gadgets = new Dictionary<GadgetType, int>();

        private float _position;

        private float _prevPositions;

        private int _glassBroken;

        private int _bonusesCollected;

        private int _trickCollected;

        private int _coinsCollected;

        public float distance => _position / 100;

        public int Jumps => GetAnimationCountBySubtype(1);

        public int Slides => GetAnimationCountBySubtype(2);

        public int bonusCollected => _bonusesCollected;

        public int coinsCollected => _coinsCollected;

        public int trickCollected => _trickCollected;

        public int brokenGlass => _glassBroken;

        //public int killedHunter
        //{
        //    get
        //    {
        //        _gadgets.TryGetValue(GadgetType.KillBot, out var num);
        //        return num;
        //    }
        //}

        public int death
        {
            get
            {
                _gameOvers.TryGetValue(GameEndType.GE_DEATH, out var num);
                return num;

            }
        }

        public int deathFromHunter
        {
            get
            {
                _gameOvers.TryGetValue(GameEndType.GE_MURDER, out var num);
                return num;
            }
        }

        public int deathBoneBreak => _gameOvers[GameEndType.GE_DEATH];

        private int GetAnimationCountBySubtype(int subType)
        {
            var num = 0;
            foreach (var animation in _animations)
            {
                if (animation.Key.SubType == subType)
                {
                    num += animation.Value;
                }
            }
            return num;
        }

        public void Reset()
        {
            _animations.Clear();
            _gameOvers.Clear();
            //_gadgets.Clear();
            _position = 0;
            _prevPositions = 0;
            _glassBroken = 0;
            _bonusesCollected = 0;
            _trickCollected = 0;
            _coinsCollected = 0;
        }

        public void SetAnimation(AnimationInfo info)
        {
            if (info == null)
                return;
            if (!_animations.ContainsKey(info))
            {
                _animations.Add(info, 1);
                return;
            }
            _animations[info]++;
        }

        public void SetTrigger(TriggerRunner triggerRunner)
        {
            if (triggerRunner.statistic == "Glass")
            {
                _glassBroken++;
            }
        }

        public void SetBonus(ItemScoreRunner bonus)
        {
            _bonusesCollected++;
        }

        public void SetTrick(TrickAreaRunner trick)
        {
            _trickCollected++;
        }

        public void SetCoin(CoinRunner coin)
        {
            _coinsCollected++;
        }

        public void SetPosition(Vector3 start, int sign)
        {
            _position += MathF.Abs((float)(start.X - _prevPositions));
            _prevPositions = (float)start.X;
        }

        //public void SetGadget(GadgetType gadgetType)
        //{
        //    if (!_gadgets.ContainsKey(gadgetType))
        //    {
        //        _gadgets.Add(gadgetType, 1);
        //        return;
        //    }
        //    _gadgets[gadgetType]++;
        //}

        public void SetGameOver(GameEndType gameEndType)
        {
            if (!_gameOvers.ContainsKey(gameEndType))
            {
                _gameOvers.Add(gameEndType, 1);
                return;
            }
            _gameOvers[gameEndType]++;
        }
    }
}

