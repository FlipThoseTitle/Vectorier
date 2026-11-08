namespace Vectorier.Models.Scene;

/// <summary>
/// Editor-only annotation. It is not game data: it is saved as an XML comment
/// (&lt;!--Vectorier.Comment ...--&gt;) inside its parent's Content, which the game ignores.
/// </summary>
public sealed class CommentElement : SceneElement
{
    public override ElementType Type => ElementType.Comment;
    public override bool LayerEditable => false;
    public override bool SupportsDynamics => false;

    protected override SceneElement CreateEmpty() => new CommentElement();
}
