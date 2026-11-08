#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Level;

public class RunnerRender
{
    private List<Runner> renderRunners = new List<Runner>();

    public static RunnerRender Current { get; } = new RunnerRender();

    public static void Reset()
    {
        Current.renderRunners.Clear();
    }

    public static void AddRunner(Runner runner)
    {
        if (!Current.renderRunners.Contains(runner))
        {
            Current.renderRunners.Add(runner);
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
