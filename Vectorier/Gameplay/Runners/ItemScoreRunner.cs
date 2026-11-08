#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Runners.Animation;

namespace Vectorier.Gameplay.Runners;

public class ItemScoreRunner : ItemRunner
{
    protected int _score;

    protected string _prefabName;


    protected float _x;

    protected float _y;

    protected int _type;

    public AnimationSprite Idle;

    public AnimationSprite Off;

    public ItemScoreRunner(int type, string prefabName, int score, float x, float y)
        : base(x, y, type)
    {
        _type = type;
        _score = score;
        _x = x;
        _y = y;
        _prefabName = prefabName;
    }

    public override void Init()
    {
        Idle = new AnimationSprite("bonus_v4")
        {
            Parent = this,
            FPS = 20,
            Pivot = new Vector2(0.5f, 0.5f),
            Iterations = -1,
            Z = -10 + Z
        };

        Off = new AnimationSprite("bonus_v4_off")
        {
            Parent = this,
            FPS = 20,
            Pivot = new Vector2(0.5f, 0.5f),
            Iterations = 1,
            Z = -10 + Z
        };

        Idle.Play();
    }

    public override void AddBonus(ModelHuman modelHuman)
    {
        modelHuman.CollectBonus(this);
        SoundsManager.PlaySounds(SoundType.bonus_pickup);
    }

    public override void Play()
    {
        Idle.Finish();
        Off.Play();
    }

    public override void Move(Vector2 shift)
    {
        base.Move(shift);
    }

    public override void Reset()
    {
        base.Reset();
        _inclusion = false;
        Idle.Play();
    }
}
