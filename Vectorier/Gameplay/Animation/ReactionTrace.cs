// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Animation.Events;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Animation
{
    /// <summary>
    /// The last reaction the game chose for a model, with what made it fire: the interval and the event it came from,
    /// and the platforms its conditions were measured on. Recorded by ControllerAnimations when a reaction is played;
    /// shown by Moves Information (I key) while that reaction's move is playing.
    /// </summary>
    public class ReactionTrace
    {
        public required AnimationReaction Reaction { get; init; }

        /// <summary>The move that was playing when the event fired, and its frame.</summary>
        public required string FromMove { get; init; }

        public int FromFrame { get; init; }

        /// <summary>The interval and the event the reaction belongs to (null when it could not be found).</summary>
        public AnimationInterval? Interval { get; init; }

        public AnimationEvent? Event { get; init; }

        public AnimationEventType EventType { get; init; }

        /// <summary>The platforms the H, V and C (collision) conditions were measured on.</summary>
        public QuadRunner? PlatformH { get; init; }

        public QuadRunner? PlatformV { get; init; }

        public QuadRunner? PlatformC { get; init; }

        /// <summary>The measuring points: the detectors and the collision point.</summary>
        public Vector3 PositionH { get; init; }

        public Vector3 PositionV { get; init; }

        public Vector3 PositionC { get; init; }

        public int Sign { get; init; }

        public Vector3 Velocity { get; init; }
    }
}
