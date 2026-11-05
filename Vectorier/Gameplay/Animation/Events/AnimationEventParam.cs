// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
