#nullable disable warnings
using System.Collections.Generic;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid;

public class Grid
{
    public static int CellWidth = 200;

    public static int CellHeight = 200;

    private List<GridColumn> _columns = new List<GridColumn>();
    private int _startXIndex;

    public List<QuadRunner> Transformation { get; } = new List<QuadRunner>();

    public void InitGrid(List<QuadRunner> quads)
    {
        for (int i = 0; i < quads.Count; i++)
        {
            AddQuad(quads[i]);
            quads[i].OnTransformationStart += OnTransformationStart;
            quads[i].OnTransformationEnd += OnTransformationEnd;
        }
    }

    public void AddQuad(QuadRunner quad)
    {
        Rectangle rectangle = quad.rectangle;
        int num = rectangle.MinXInt < 0 ? rectangle.MinXInt / CellWidth - 1 : rectangle.MinXInt / CellWidth;
        int num2 = rectangle.MaxXInt < 0 ? rectangle.MaxXInt / CellWidth - 1 : rectangle.MaxXInt / CellWidth;
        if (_columns.Count == 0)
        {
            _startXIndex = num;
        }
        if (num < _startXIndex)
        {
            int num3 = _startXIndex - num;
            _startXIndex = num;
            for (int i = 0; i < num3; i++)
            {
                _columns.Insert(0, new GridColumn());
            }
        }
        int num4 = num - _startXIndex;
        int num5 = num2 - _startXIndex;
        if (num5 >= _columns.Count)
        {
            int num6 = num5 - _columns.Count + 1;
            for (int j = 0; j < num6; j++)
            {
                _columns.Add(new GridColumn());
            }
        }
        for (int k = num4; k <= num5; k++)
        {
            _columns[k].Add(quad);
        }
    }

    public void RemoveQuad(QuadRunner quad)
    {
        Rectangle rectangle = quad.rectangle;
        int num = rectangle.MinXInt < 0 ? rectangle.MinXInt / CellWidth - 1 : rectangle.MinXInt / CellWidth;
        int num2 = rectangle.MaxXInt < 0 ? rectangle.MaxXInt / CellWidth - 1 : rectangle.MaxXInt / CellWidth;
        int num3 = num - _startXIndex;
        int num4 = num2 - _startXIndex;
        for (int i = num3; i <= num4; i++)
        {
            _columns[i].Remove(quad);
        }
    }

    public void OnTransformationStart(QuadRunner quad)
    {
        RemoveQuad(quad);
        Transformation.Add(quad);
    }

    public void OnTransformationEnd(QuadRunner quad)
    {
        Transformation.Remove(quad);
        AddQuad(quad);
    }

    public void Collect(Rectangle rect, List<QuadRunner> quads)
    {
        quads.AddRange(Transformation);
        int num = rect.MinXInt < 0 ? rect.MinXInt / CellWidth - 1 : rect.MinXInt / CellWidth;
        int num2 = rect.MaxXInt < 0 ? rect.MaxXInt / CellWidth - 1 : rect.MaxXInt / CellWidth;
        int num3 = num - _startXIndex;
        int num4 = num2 - _startXIndex;
        int num5 = _columns.Count - 1;
        if (num3 < 0)
        {
            num3 = 0;
        }
        if (num3 > num5)
        {
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
            _columns[i].Collect(rect, quads);
        }
    }

    public void Reset()
    {
        foreach (var quad in Transformation)
        {
            AddQuad(quad);
        }
        Transformation.Clear();
    }
}
