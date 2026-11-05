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
    public class AnimationSound
    {
        private List<string> _names;

        public AnimationSound(string name, int type)
        {
            _names = new List<string>(name.Split('|'));
        }

        public void Play(float p_volume = 1f)
        {
            var num = Random.Range(0, _names.Count);
            SoundsManager.PlaySoundsOnce(_names[num], p_volume);
        }
    }
}
