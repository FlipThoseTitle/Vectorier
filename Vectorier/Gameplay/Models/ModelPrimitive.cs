#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Models;

public class ModelPrimitive : Model
{
    private bool _isStrike;

    private List<string> _sounds;

    private PrimitiveRunner _base;

    public float Impulse { get; set; }

    public int RenderTime { get; set; }

    public int RenderCount { get; set; }

    public virtual bool IsStrike
    {
        get => _isStrike;
        set
        {
            _isStrike = value;
            if (value)
            {
                _ControllerPhysics.Start();
                RenderTime = RenderCount;
            }
        }
    }

    public override Rectangle Rectangle
    {
        get
        {
            var rectangle = base.Rectangle;
            var node = _ModelObject.GetNode();
            rectangle.Origin = rectangle.Origin.Add(node.Start.X, node.Start.Y);
            return rectangle;
        }
    }

    public ModelPrimitive(PrimitiveRunner primitiveRunner, List<string> skins, List<string> sounds)
        : base(skins, ModelType.Primitive)
    {
        _sounds = sounds;
        _base = primitiveRunner;
        RenderCount = 150;
        Init();
    }

    public override void Strike(ModelLine edge, Vector3 point, Vector3 impulse)
    {
        IsStrike = true;
        PlaySound();
        _controllerStrike.Striking(edge, point, impulse * Impulse);
    }

    private void PlaySound()
    {
        int num = Random.Range(0, _sounds.Count);
        SoundsManager.PlaySounds(_sounds[num]);
    }
}
