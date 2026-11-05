// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace Vectorier.Gameplay.Animation
{
    public class AnimationTrickInfo : AnimationInfo
    {
        public static readonly List<AnimationTrickInfo> TricksLoaded = new List<AnimationTrickInfo>();

        private readonly List<AnimationInfo> _animationParts;

        public AnimationTrickInfo(XmlNode node)
            : base(node)
        {
            IsTrick = true;
            if (node.Attributes["Parts"] == null)
            {
                return;
            }
            _animationParts = new List<AnimationInfo>();
            foreach (string anim in node.Attributes["Parts"].Value.Split('|'))
            {
                _animationParts.Add(Animations.Animation[anim]);
            }
        }

        public override void LoadBinary(bool useCache)
        {
            base.LoadBinary(useCache);
            if (_animationParts == null)
            {
                return;
            }
            for (int i = 0; i < _animationParts.Count; i++)
            {
                _animationParts[i].LoadBinary(useCache);
            }
        }

        public override void UnloadBinary()
        {
            base.UnloadBinary();
            if (_animationParts == null)
            {
                return;
            }
            for (int i = 0; i < _animationParts.Count; i++)
            {
                _animationParts[i].UnloadBinary();
            }
        }

        public static void LoadAnimation(AnimationTrickInfo info)
        {
            if (info != null)
            {
                info.LoadBinary(true);
                TricksLoaded.Add(info);
            }
        }

        public static void UnloadTricks()
        {
            foreach (AnimationTrickInfo info in TricksLoaded)
            {
                info.UnloadBinary();
            }
            TricksLoaded.Clear();
        }
    }
}
