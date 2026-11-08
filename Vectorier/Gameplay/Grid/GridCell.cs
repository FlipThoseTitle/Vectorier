#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid;

public class GridCell
{
    private List<QuadRunner> _quads = new List<QuadRunner>();

    public void Add(QuadRunner quad)
    {
        _quads.Add(quad);
    }

    public void Remove(QuadRunner quad)
    {
        _quads.Remove(quad);
    }

    public void Collect(List<QuadRunner> quads)
    {
        for (int i = 0; i < _quads.Count; i++)
        {
            QuadRunner quadRunner = _quads[i];
            if (!quadRunner.IsRender)
            {
                quads.Add(quadRunner);
                quadRunner.IsRender = true;
            }
        }
    }
}
