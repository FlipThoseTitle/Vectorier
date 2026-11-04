// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Node;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Detector
{
    public class DetectorEvent
    {
        public enum DetectorEventType
        {
            None,
            On,
            Off
        }

        public DetectorLine Detector
        {
            get;
        }

        public DetectorEventType Type
        {
            get;
        }

        public int Side
        {
            get;
        }

        public QuadRunner Platform => Detector.Platform;

        public ModelNode Node => Detector.Node;

        public bool IsVertical => Detector.Type == DetectorLine.DetectorType.Vertical;

        public bool IsHorizontal => Detector.Type == DetectorLine.DetectorType.Horizontal;

        public DetectorEvent(DetectorLine detector, DetectorEventType type, int side)
        {
            Detector = detector;
            Type = type;
            Side = side;
        }

        public void DeltaPosition(Vector3 velocity)
        {
            if (Type == DetectorEventType.On)
            {
                var v = Platform.DeltaEdge(Detector.Position, Side);
                Detector.DeltaPosition(v.Add(velocity));
            }
        }
    }
}
