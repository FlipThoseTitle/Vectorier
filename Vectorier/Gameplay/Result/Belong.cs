#nullable disable warnings
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Result;

public class Belong
{
    public QuadRunner Platform { get; }

    public DetectorLine Detector { get; }

    public Belong(QuadRunner platform, DetectorLine detector)
    {
        Platform = platform;
        Detector = detector;
    }
}
