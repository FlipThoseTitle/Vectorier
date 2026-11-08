#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Animation;

public class AnimationGroup
{
    public static readonly Dictionary<string, AnimationGroup> Groups = new Dictionary<string, AnimationGroup>();

    public string Name { get; set; }

    public List<AnimationReaction> Reactions { get; set; }

    public static void Add(AnimationGroup group)
    {
        if (group != null)
        {
            Groups[group.Name] = group;
        }
    }

    public static AnimationGroup GetGroup(string name)
    {
        Groups.TryGetValue(name, out AnimationGroup group);
        return group;
    }

    public static void ClearGroups()
    {
        Groups.Clear();
    }
}
