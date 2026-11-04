// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Vectorier.Gameplay.Models
{
    public class ModelEvent<T> where T : class
    {
        private T _Data;

        private bool _Bubbles;

        private bool _Cancelable;

        public T Data => _Data;

        public bool Bubbles => _Bubbles;

        public bool Cancelable => _Cancelable;

        public ModelEvent(T Data = null, bool Bubbles = false, bool Cancelable = false)
        {
            _Data = Data;
            _Bubbles = Bubbles;
            _Cancelable = Cancelable;
        }
    }
}
