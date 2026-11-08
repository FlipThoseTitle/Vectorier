using System;
using System.Collections.Generic;
using Avalonia;

namespace Vectorier.Controls.Scene;

/// <summary>
/// Uniform-grid spatial index over level-space bounds. Viewport queries touch only the cells that the view
/// overlaps, so the cost of a frame depends on what is visible, not on the size of the level.
/// </summary>
internal sealed class SpatialGrid
{
    private const double CellSize = 512;
    private const int LargeCellSpan = 48;

    private readonly Dictionary<long, HashSet<RenderItem>> _cells = new();
    private readonly HashSet<RenderItem> _large = new();

    private static long Key(int x, int y) => ((long)x << 32) | (uint)y;

    private static int Cell(double v) => (int)Math.Floor(v / CellSize);

    public void Clear()
    {
        _cells.Clear();
        _large.Clear();
    }

    public void Insert(RenderItem item)
    {
        var b = item.Bounds;
        item.CellX0 = Cell(b.Left);
        item.CellY0 = Cell(b.Top);
        item.CellX1 = Cell(b.Right);
        item.CellY1 = Cell(b.Bottom);
        item.Large = item.CellX1 - item.CellX0 > LargeCellSpan || item.CellY1 - item.CellY0 > LargeCellSpan;
        item.InGrid = true;
        if (item.Large)
        {
            _large.Add(item);
            return;
        }
        for (int x = item.CellX0; x <= item.CellX1; x++)
        {
            for (int y = item.CellY0; y <= item.CellY1; y++)
            {
                long key = Key(x, y);
                if (!_cells.TryGetValue(key, out var set))
                {
                    set = new HashSet<RenderItem>();
                    _cells[key] = set;
                }
                set.Add(item);
            }
        }
    }

    public void Remove(RenderItem item)
    {
        if (!item.InGrid)
            return;
        item.InGrid = false;
        if (item.Large)
        {
            _large.Remove(item);
            return;
        }
        for (int x = item.CellX0; x <= item.CellX1; x++)
        {
            for (int y = item.CellY0; y <= item.CellY1; y++)
            {
                if (_cells.TryGetValue(Key(x, y), out var set))
                {
                    set.Remove(item);
                    if (set.Count == 0)
                        _cells.Remove(Key(x, y));
                }
            }
        }
    }

    /// <summary>Adds every item whose bounds intersect <paramref name="rect"/> (each once per stamp).</summary>
    public void Query(Rect rect, int stamp, List<RenderItem> output)
    {
        foreach (var item in _large)
        {
            if (item.Stamp != stamp && item.Bounds.Intersects(rect))
            {
                item.Stamp = stamp;
                output.Add(item);
            }
        }

        int x0 = Cell(rect.Left), y0 = Cell(rect.Top), x1 = Cell(rect.Right), y1 = Cell(rect.Bottom);
        long cellCount = (long)(x1 - x0 + 1) * (y1 - y0 + 1);
        if (cellCount > _cells.Count * 2L)
        {
            // Zoomed far out: walking the occupied cells is cheaper than walking the rectangle.
            foreach (var set in _cells.Values)
                AddFrom(set, rect, stamp, output);
            return;
        }
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                if (_cells.TryGetValue(Key(x, y), out var set))
                    AddFrom(set, rect, stamp, output);
            }
        }
    }

    private static void AddFrom(HashSet<RenderItem> set, Rect rect, int stamp, List<RenderItem> output)
    {
        foreach (var item in set)
        {
            if (item.Stamp == stamp)
                continue;
            item.Stamp = stamp;
            if (item.Bounds.Intersects(rect))
                output.Add(item);
        }
    }
}
