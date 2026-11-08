#nullable disable warnings
using System.Collections.Generic;
namespace Vectorier.Gameplay.Animation;

public class AnimationSound
{
    private List<string> _names;

    public AnimationSound(string name, int type)
    {
        _names = new List<string>(name.Split('|'));
    }

    public void Play(float volume = 1f)
    {
        var num = Random.Range(0, _names.Count);
        SoundsManager.PlaySoundsOnce(_names[num], volume);
    }
}
