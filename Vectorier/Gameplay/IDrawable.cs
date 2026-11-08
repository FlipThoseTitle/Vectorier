#nullable disable warnings
namespace Vectorier.Gameplay;

public interface IDrawable
{
    public float Z { get; set; }

    public void Draw();
}
