// Ported from Vector-Raylib-For-Vectorier (the game's player movement and model drawing).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid
{
    public class GridColumn
    {
        private List<GridCell> _Cells = new List<GridCell>();

        private int _StartYIndex;

        public void Add(QuadRunner p_quad)
        {
            Rectangle rectangle = p_quad.rectangle;
            int num = rectangle.MinYInt < 0 ? rectangle.MinYInt / Grid.CellHeight - 1 : rectangle.MinYInt / Grid.CellHeight;
            int num2 = rectangle.MaxYInt < 0 ? rectangle.MaxYInt / Grid.CellHeight - 1 : rectangle.MaxYInt / Grid.CellHeight;
            if (_Cells.Count == 0)
            {
                _StartYIndex = num;
            }
            if (num < _StartYIndex)
            {
                int num3 = _StartYIndex - num;
                _StartYIndex = num;
                for (int i = 0; i < num3; i++)
                {
                    _Cells.Insert(0, new GridCell());
                }
            }
            int num4 = num - _StartYIndex;
            int num5 = num2 - _StartYIndex;
            if (num5 >= _Cells.Count)
            {
                int num6 = num5 - _Cells.Count + 1;
                for (int j = 0; j < num6; j++)
                {
                    _Cells.Add(new GridCell());
                }
            }
            for (int k = num4; k <= num5; k++)
            {
                _Cells[k].Add(p_quad);
            }
        }

        public void Remove(QuadRunner p_quad)
        {
            Rectangle rectangle = p_quad.rectangle;
            int num = rectangle.MinYInt < 0 ? rectangle.MinYInt / Grid.CellHeight - 1 : rectangle.MinYInt / Grid.CellHeight;
            int num2 = rectangle.MaxYInt < 0 ? rectangle.MaxYInt / Grid.CellHeight - 1 : rectangle.MaxYInt / Grid.CellHeight;
            int num3 = num - _StartYIndex;
            int num4 = num2 - _StartYIndex;
            int num5 = _Cells.Count - 1;
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
                _Cells[i].Remove(p_quad);
            }
        }

        public void Collect(Rectangle p_rect, List<QuadRunner> p_quads)
        {
            if (_Cells.Count != 0)
            {
                int num = p_rect.MinYInt < 0 ? p_rect.MinYInt / Grid.CellHeight - 1 : p_rect.MinYInt / Grid.CellHeight;
                int num2 = p_rect.MaxYInt < 0 ? p_rect.MaxYInt / Grid.CellHeight - 1 : p_rect.MaxYInt / Grid.CellHeight;
                int num3 = num - _StartYIndex;
                int num4 = num2 - _StartYIndex;
                int num5 = _Cells.Count - 1;
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
                    _Cells[i].Collect(p_quads);
                }
            }
        }
    }
}
