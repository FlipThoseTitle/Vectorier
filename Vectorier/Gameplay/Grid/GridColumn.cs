#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid;

public class GridColumn
{
    private List<GridCell> _cells = new List<GridCell>();

    private int _startYIndex;

    public void Add(QuadRunner quad)
    {
        Rectangle rectangle = quad.rectangle;
        int num = rectangle.MinYInt < 0 ? rectangle.MinYInt / Grid.CellHeight - 1 : rectangle.MinYInt / Grid.CellHeight;
        int num2 = rectangle.MaxYInt < 0 ? rectangle.MaxYInt / Grid.CellHeight - 1 : rectangle.MaxYInt / Grid.CellHeight;
        if (_cells.Count == 0)
        {
            _startYIndex = num;
        }
        if (num < _startYIndex)
        {
            int num3 = _startYIndex - num;
            _startYIndex = num;
            for (int i = 0; i < num3; i++)
            {
                _cells.Insert(0, new GridCell());
            }
        }
        int num4 = num - _startYIndex;
        int num5 = num2 - _startYIndex;
        if (num5 >= _cells.Count)
        {
            int num6 = num5 - _cells.Count + 1;
            for (int j = 0; j < num6; j++)
            {
                _cells.Add(new GridCell());
            }
        }
        for (int k = num4; k <= num5; k++)
        {
            _cells[k].Add(quad);
        }
    }

    public void Remove(QuadRunner quad)
    {
        Rectangle rectangle = quad.rectangle;
        int num = rectangle.MinYInt < 0 ? rectangle.MinYInt / Grid.CellHeight - 1 : rectangle.MinYInt / Grid.CellHeight;
        int num2 = rectangle.MaxYInt < 0 ? rectangle.MaxYInt / Grid.CellHeight - 1 : rectangle.MaxYInt / Grid.CellHeight;
        int num3 = num - _startYIndex;
        int num4 = num2 - _startYIndex;
        int num5 = _cells.Count - 1;
        if (num3 < 0)
        {
            num3 = 0;
        }
        if (num3 > num5)
        {
            num3 = num5;
        }
        if (num4 < 0)
        {
            num4 = 0;
        }
        if (num4 > num5)
        {
            num4 = num5;
        }
        for (int i = num3; i <= num4; i++)
        {
            _cells[i].Remove(quad);
        }
    }

    public void Collect(Rectangle rect, List<QuadRunner> quads)
    {
        if (_cells.Count != 0)
        {
            int num = rect.MinYInt < 0 ? rect.MinYInt / Grid.CellHeight - 1 : rect.MinYInt / Grid.CellHeight;
            int num2 = rect.MaxYInt < 0 ? rect.MaxYInt / Grid.CellHeight - 1 : rect.MaxYInt / Grid.CellHeight;
            int num3 = num - _startYIndex;
            int num4 = num2 - _startYIndex;
            int num5 = _cells.Count - 1;
            if (num3 < 0)
            {
                num3 = 0;
            }
            if (num3 > num5)
            {
                num3 = num5;
            }
            if (num4 < 0)
            {
                num4 = 0;
            }
            if (num4 > num5)
            {
                num4 = num5;
            }
            for (int i = num3; i <= num4; i++)
            {
                _cells[i].Collect(quads);
            }
        }
    }
}
