#nullable disable warnings
using System.Xml;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Transformation;

public abstract class TransformPrototype
{
    protected Runner _Runner;

    protected bool _Pause;

    protected Type _Type;

    public virtual Runner Runner
    {
        get => _Runner;
        set => _Runner = value;
    }

    public bool Pause
    {
        get => _Pause;
        set => _Pause = value;
    }

    public Type Type => _Type;

    public abstract int Frames { get; }

    public abstract void Parse(XmlNode Node);

    public abstract bool Iteration();

    public abstract void CalcDelta();

    public abstract TransformPrototype Clone();

    public virtual void Reset()
    {
        _Pause = false;
    }
}
