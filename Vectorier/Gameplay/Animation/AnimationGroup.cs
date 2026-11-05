// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
