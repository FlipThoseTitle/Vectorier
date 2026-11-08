#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners;

public class CoinRunner : ItemScoreRunner
{
    private bool _enabled;

    private int _groupID;

    private int _defaultScore;

    public Color color;

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
