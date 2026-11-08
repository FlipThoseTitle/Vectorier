#nullable disable warnings
namespace Vectorier.Gameplay.Runners.Animation;

//FIX
public class AnimationRendering : Runner
{
    private int _currentFrame;

    private bool _replay;

    protected bool _IsPlay;

    private int _countHalf;

    private int _totalHalf;

    public string Path = "";

    protected float _Width;

    protected float _Height;

    protected int _TotalFrames;

    public AnimationSprite Animator;

    public AnimationRendering(string name, float X = 0f, float Y = 0f, float width = 100f, float height = 100f, bool replay = false, int speed = 1)
        : base(X, Y)
    {
        Name = name;
        _Width = width;
        _Height = height;
        _replay = replay;
        _totalHalf = speed;

    }

    public virtual void Init(float pivotX = 0, float pivotY = 0)
    {
        Animator = new AnimationSprite(Name)
        {
            Parent = this,
            Pivot = new System.Numerics.Vector2(pivotX, pivotY),
            Z = Z
        };
        _TotalFrames = Animator.TotalFrames;
        IsEnabled = false;
    }

    public override bool Render()
    {
        if (_IsPlay)
        {
            if (_countHalf < _totalHalf)
            {
                _countHalf++;
                return false;
            }
            _countHalf = 0;
            _currentFrame++;
            if (_currentFrame < _TotalFrames)
            {
                return false;
            }
            if (_replay)
            {
                _currentFrame = 0;
                return false;
            }
            Stop();
        }
        return true;
    }

    public override void Reset()
    {
        base.Reset();
        _currentFrame = 0;
        _countHalf = 0;
        _IsPlay = false;
    }

    public virtual void Stop(bool value = false)
    {
        _IsPlay = false;
        GlobalObjects.RemoveDrawable(this);
        if (value)
        {
            IsEnabled = false;
        }
    }

    public override void Draw()
    {
        if (!_IsPlay || _currentFrame >= _TotalFrames)
        {
            return;
        }
        Animator.Draw(_currentFrame);
    }

    public void PlayFrom(int value)
    {
        _currentFrame = value;
        _countHalf = _totalHalf;
        GlobalObjects.AddDrawable(this);
        Render();
        _IsPlay = true;

    }
}
