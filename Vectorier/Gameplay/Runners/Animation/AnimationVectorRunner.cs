#nullable disable warnings
using System.Numerics;

namespace Vectorier.Gameplay.Runners.Animation;

public class AnimationVectorRunner : AnimationRunner
{
    private Vector2 _direction = new Vector2();

    private Vector2 _acceleration = new Vector2();

    private Vector2 _directionResult = new Vector2();

    private Vector2 _accelerationResult = new Vector2();

    private int _life;

    private int _count;

    public AnimationVectorRunner(float x, float y, float width, float height, string name, int type, float scaleX, float scaleY, Vector2? direction, Vector2? acceleration, int life, bool replay = false, bool iskill = false, bool isSmooth = false, int speed = 1, int thin = 1)
        : base(x, y, width, height, name, type, scaleX, scaleY, true, 2)
    {
        if (direction != null)
        {
            _direction = direction.Value;
        }
        if (acceleration != null)
        {
            _acceleration = acceleration.Value;
        }
        _accelerationResult = _acceleration;
        _life = life;
        _IsPlay = false;
    }

    public override void Reset()
    {
        _directionResult = DefaultPosition;
        _accelerationResult = _acceleration;
        _count = 0;
        IsEnabled = false;
        Stop();
        base.Reset();
    }

    public override void Move(Vector2 Point)
    {
        base.Move(Point);
        _directionResult.X = LocalPosition.X;
        _directionResult.Y = LocalPosition.Y;
    }

    public override bool Render()
    {
        if (!_IsPlay)
        {
            return true;
        }
        _accelerationResult += _acceleration;
        _directionResult += _direction;
        _directionResult += _accelerationResult;
        LocalPosition = new Vector2(_directionResult.X, _directionResult.Y);
        base.Render();
        if (LevelMainController.current.slowModeFrames * _life < _count)
        {
            Stop(true);
            return true;
        }
        _count++;
        return false;
    }
}
