// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Vectorier.Gameplay
{
    public static class GlobalObjects
    {
        public static List<IDrawable> Drawables = new List<IDrawable>();

        public static List<IUpdatable> Updatables = new List<IUpdatable>();

        public static bool InitAfterAdding = false;

        public static void Init()
        {
            Drawables = Drawables.OrderByDescending(x => x.Z).ToList();
        }

        public static void AddDrawable(IDrawable drawable)
        {
            if (drawable == null || Drawables.Contains(drawable))
            {
                return;
            }
            Drawables.Add(drawable);

            if (InitAfterAdding)
            {
                Init();
            }
        }

        public static void AddUpdatable(IUpdatable updatable)
        {
            if (updatable == null || Updatables.Contains(updatable))
            {
                return;
            }
            Updatables.Add(updatable);
        }

        public static void RemoveDrawable(IDrawable drawable)
        {
            if (drawable == null || !Drawables.Contains(drawable))
            {
                return;
            }
            Drawables.Remove(drawable);
        }

        public static void RemoveUpdatable(IUpdatable updatable)
        {
            if (updatable == null || !Updatables.Contains(updatable))
            {
                return;
            }
            Updatables.Remove(updatable);
        }
    }
}
