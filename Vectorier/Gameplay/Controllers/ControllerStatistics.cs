#nullable disable warnings
using System;
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Controllers;

public class ControllerStatistics
{
    private Dictionary<AnimationInfo, int> _animations = new Dictionary<AnimationInfo, int>();

    private Dictionary<GameEndType, int> _gameOvers = new Dictionary<GameEndType, int>();

    private float _position;

    private float _prevPositions;

    private int _glassBroken;

    private int _bonusesCollected;

    private int _trickCollected;

    private int _coinsCollected;

    public void Reset()
    {
        _animations.Clear();
        _gameOvers.Clear();
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

