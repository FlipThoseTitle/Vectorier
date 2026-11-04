// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay.Animation.Events
{
    public class AnimationEventParam
    {
        public List<AnimationReaction> Reaction
        {
            get;
        }

        public List<AnimationSound> Sound
        {
            get;
        }

        /// <summary>The event's element name in moves.xml (Keys, DetectorV, OnEnd...): for Moves Information.</summary>
        public string XmlName { get; set; } = string.Empty;

        /// <summary>The event's attributes as written in moves.xml, Groups excepted (e.g. Type="1").</summary>
        public string XmlAttributes { get; set; } = string.Empty;

        /// <summary>The EventGroup the event comes from (null: defined directly in the interval).</summary>
        public string? EventGroup { get; set; }

        /// <summary>The event's Groups attribute: the ReactionGroups it uses (null: none).</summary>
        public string? ReactionGroups { get; set; }

        public AnimationEventParam(List<AnimationReaction> reaction, List<AnimationSound> sound)
        {
            Reaction = reaction;
            Sound = sound;
        }
    }
}
