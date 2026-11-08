#nullable disable warnings
using System.Collections.Generic;
using System.Linq;
namespace Vectorier.Gameplay;

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
