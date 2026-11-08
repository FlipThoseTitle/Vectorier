#nullable disable warnings
using System.Numerics;
using Vectorier.Gameplay.Camera;

namespace Vectorier.Gameplay.Runners;

public class VisualContainer : IParent, IUpdatable
{
    public IParent Parent { get; set; }

    public Vector2 LocalPosition { get; set; }
    public Vector2 LocalScale { get; set; } = Vector2.One;

    public Vector2 WorldPosition
    {
        get
        {
            if (Parent == null)
                return LocalPosition;

            return Parent.TransformPoint(LocalPosition);
        }
    }

    public Vector2 WorldScale
    {
        get
        {
            if (Parent == null)
                return LocalScale;

            return LocalScale * Parent.WorldScale;
        }
    }


    public float Z;

    public float Factor;

    private float Scale = 4;

    private bool IsZoom;

    private int Frame;

    private float Time = 30;

    private float ZoomValue = 1;

    public float FrameScale
    {
        get
        {
            float num = 2 * Frame / Time;
            num *= num;
            float num2 = (ZoomValue - Scale) / 2f;
            if (Frame < Time / 2f)
            {
                return Scale + num2 * num;
            }
            return Scale + num2 * (8 * Frame / Time - 2f - num);
        }
    }

    public VisualContainer(float factor, float z)
    {
        Factor = factor;
        Z = z;

        GlobalObjects.AddUpdatable(this);
    }

    public void Move(Vector2 point)
    {
        var frameScale = FrameScale;

        var vector = new Vector2();
        vector.X = -(float)(point.X * Factor * frameScale);
        vector.Y = -(float)(point.Y * Factor * frameScale);
        LocalPosition = vector;
    }

    public void Update()
    {
        if (LocationCamera.Current == null)
        {
            return;
        }
        Play();
        Move(new Vector2(LocationCamera.Current.X, LocationCamera.Current.Y));
    }

    public void Zooming(float value, bool isStart)
    {
        Frame = 0;
        Scale = ZoomValue;
        ZoomValue = Utilities.Math.Round(value / (value + Factor * (1 - value)), 10);
        if (!isStart)
        {
            IsZoom = true;
            return;
        }
        Scale = ZoomValue;
        var frameScale = FrameScale;
        LocalScale = new Vector2(frameScale, frameScale);
    }

    public void Play()
    {
        if (!IsZoom)
        {
            return;
        }
        Frame++;
        var frameScale = FrameScale;
        LocalScale = new Vector2(frameScale, frameScale);
        if (Frame < Time)
        {
            return;
        }
        IsZoom = false;
    }

    public Vector2 TransformPoint(Vector2 point)
    {
        return LocalPosition + (point * FrameScale);
    }

    public Vector2 TransformPoint(Vector3 point)
    {
        return LocalPosition + (new Vector2(point.X, point.Y) * FrameScale);
    }
}
