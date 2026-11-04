// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay.Animation
{
    public class AnimationGroup
    {
        public static readonly Dictionary<string, AnimationGroup> Groups = new Dictionary<string, AnimationGroup>();

        public string Name
        {
            get;
            set;
        }

        public List<AnimationReaction> Reactions
        {
            get;
            set;
        }

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

        public static List<AnimationReaction> GetReactions(string name)
        {
            return GetGroup(name).Reactions;
        }

        public static void ClearGroups()
        {
            Groups.Clear();
        }
    }
}
