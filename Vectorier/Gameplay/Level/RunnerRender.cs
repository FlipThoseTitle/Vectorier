// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Level
{
    public class RunnerRender
    {
        private static RunnerRender _current = new RunnerRender();

        private List<Runner> renderRunners = new List<Runner>();

        public static RunnerRender Current => _current;

        public List<Runner> Runners => renderRunners;

        public static void FreeMemory()
        {
            _current = null;
        }

        public static void Reset()
        {
            Current.renderRunners.Clear();
        }

        public static void AddRunner(Runner p_runner)
        {
            if (!Current.renderRunners.Contains(p_runner))
            {
                Current.renderRunners.Add(p_runner);
            }
        }

        public static void RenderRunners()
        {
            Current.RenderLocal();
        }

        private void RenderLocal()
        {
            int count = renderRunners.Count;
            for (int num = count - 1; num >= 0; num--)
            {
                if (renderRunners[num].Render())
                {
                    renderRunners.RemoveAt(num);
                }
            }
        }
    }
}
