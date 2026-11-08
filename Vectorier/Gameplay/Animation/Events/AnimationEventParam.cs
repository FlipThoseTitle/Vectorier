#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Animation.Events;

public class AnimationEventParam
{
    public List<AnimationReaction> Reaction { get; }

    public List<AnimationSound> Sound { get; }

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
