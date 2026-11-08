#nullable disable warnings
using System.Collections.Generic;
using System.Xml;

namespace Vectorier.Gameplay.Animation;

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
}
