#nullable disable warnings
using System.Collections.Generic;
using System.Numerics;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Runners;

public class PrimitiveRunner : Runner
{
    private Vector3 _position = new Vector3();

    private Vector3 _deltaPosition = new Vector3();

    private Color _color;
    private bool _isUpdate;

    private List<string> _sounds;
    private float _impulse;

    protected int _type;

    public Vector3 Position
    {
        get => _position;
        set => _position = DeltaPosition + value;
    }

    public Vector3 DeltaPosition => _deltaPosition;

    public Color Color
    {
        get => _color;
        set => _color = value;
    }

    public ModelType Type { get; set; } = ModelType.Primitive;

    public ModelPrimitive Model { get; private set; }

    public virtual bool IsStrike
    {
        get => Model.IsStrike;
        set => Model.IsStrike = value;
    }
    public Rectangle Rectangle => Model.Rectangle;

    public PrimitiveRunner(int type, string name, Color color, Vector3 deltaPosition, float impulse, List<string> sounds)
        : base(0f, 0f)
    {
        _color = color;
        _deltaPosition = deltaPosition;
        Name = name;
        _sounds = sounds;
        _impulse = impulse;
        TypeClass = RunnerType.Primitive;
        _type = type;
    }

    public override void InitRunner(Vector2 point)
    {
        base.InitRunner(point);
        DefaultPosition = DefaultPosition.Set(new Vector2(_position.X, _position.Y));
    }

    public virtual void Load()
    {
        List<string> skins = new List<string> { Name + ".xml" };
        Model = new ModelPrimitive(this, skins, _sounds);
        Model.Type = Type;
        Model.Color = _color;
        Model.Name = Name;
        Model.IsEnabled = true;
        Model.IsVisible = true;
        Model.Impulse = _impulse;
        Model.Position(new Vector3(Position.X, Position.Y, 0));

    }

    public virtual void Render(List<QuadRunner> Platforms)
    {
        if (!IsEnabled || !Model.IsStrike)
        {
            return;
        }
        CollisitionPlatform(Platforms);
        Update();
        Model.ModelObject.RenderMacroNode();
        Model.ControllerPhysics.Render();
        if (Model.IsStrike)
        {
            if (Model.RenderTime > 0)
            {
                Model.RenderTime--;
                return;
            }
            Model.IsVisible = false;
            Model.ControllerPhysics.Stop();
        }
    }

    public override void Move(Vector2 point)
    {
        Position = new Vector3(point, 0);
        if (Model != null)
            Model.Position(new Vector3(Position.X, Position.Y, 0));
    }

    public void CollisitionPlatform(List<QuadRunner> quads)
    {
        foreach (var quad in quads)
        {
            Model.ControllerCollisions.UpdatePlatform(quad);
        }
    }

    public void Update()
    {
        if (Model.IsEnabled && _isUpdate)
        {
            Position += _deltaPosition;
        }
    }

    public override void Reset()
    {
        _isUpdate = false;
        Model.IsStrike = false;
        Model.IsEnabled = true;
        Model.ControllerPhysics.Stop();
        Model.Reset();
        Move(new Vector2(Position.X - _deltaPosition.X, Position.Y - _deltaPosition.Y));
        Model.IsVisible = true;
        Model.ModelObject.RenderMacroNode();
        Model.ControllerPhysics.Render();
    }
}
