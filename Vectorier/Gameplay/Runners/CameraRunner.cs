#nullable disable warnings
namespace Vectorier.Gameplay.Runners;

public class CameraRunner : SpawnRunner
{
    public CameraRunner(float x, float y, string n, string animation) : base(x, y, n, animation)
    {
        TypeClass = RunnerType.Camera;
    }
}
