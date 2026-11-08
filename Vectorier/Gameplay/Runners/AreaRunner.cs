#nullable disable warnings
using Vectorier.Gameplay.Animation;
using Vectorier.Gameplay.Controllers;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Models;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Runners;

public class AreaRunner : QuadRunner
{
    public enum AreaType
    {
        None,
        Trick,
        Help,
        Catch
    }

    private AreaType _type;
    protected string _TypeName;

    public bool IsTrick => _type == AreaType.Trick;

    public bool IsCatch => _type == AreaType.Catch;

    public AreaRunner(AreaType type, float x, float y, float width, float height, string typeName, string name)
        : base(x, y, width, height, false, string.IsNullOrEmpty(typeName) ? 1 : 0, name)
    {
        _type = type;
        _TypeName = typeName;

        TypeClass = RunnerType.Area;
        LoadBinaryIfTrick();
    }

    /// <summary>A Trick area's move is loaded with the level (and again after Reload Moves).</summary>
    public void LoadBinaryIfTrick()
    {
        if (Animations.Animation.ContainsKey(Name.Replace("Trigger", "")))
        {
            AnimationInfo animationInfo = Animations.Animation[Name.Replace("Trigger", "")];
            if (animationInfo.IsTrick)
            {
                AnimationTrickInfo.LoadAnimation(animationInfo as AnimationTrickInfo);
            }
        }
    }

    public virtual void Init()
    {

    }

    public override void Draw()
    {
        if (!IsEnabled)
            return;

        var vs = Sets.Current.Containers[1];
        var pos = vs.TransformPoint(rectangle.Origin);
        Raylib.DrawRectangleV(pos, rectangle.Size * WorldScale, new Color(255, 0, 0, 64));
    }

    public virtual bool OnKeyClick(KeyVariables Key)
    {
        return true;
    }

    public virtual void Activate(ModelHuman model)
    {
    }

    public virtual void Deactivate(ModelHuman model)
    {
    }
}
