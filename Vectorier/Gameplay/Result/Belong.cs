// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Result
{
    public class Belong
    {
        private QuadRunner _Platform;

        private DetectorLine _Detector;

        public QuadRunner Platform => _Platform;

        public DetectorLine Detector => _Detector;

        public Belong(QuadRunner platform, DetectorLine detector)
        {
            _Platform = platform;
            _Detector = detector;
        }
    }
}
