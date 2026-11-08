using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorier.Models.Scene;

/// <summary>&lt;Particle&gt;: a particle effect sprite.</summary>
public sealed partial class ParticleElement : SceneElement
{
    public const string DefaultClassName = "p_glass1_mini";

    public override ElementType Type => ElementType.Particle;
    public override bool NameEditable => false;

    [ObservableProperty] private int _particleType = 1;
    [ObservableProperty] private int _frame;

    public string ClassName
    {
        get => Name;
        set => Name = value;
    }

    protected override SceneElement CreateEmpty() => new ParticleElement();

    protected override void CopyTo(SceneElement target)
    {
        base.CopyTo(target);
        var t = (ParticleElement)target;
        t.ParticleType = ParticleType;
        t.Frame = Frame;
    }
}
