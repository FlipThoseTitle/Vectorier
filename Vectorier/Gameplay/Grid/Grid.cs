// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Vectorier.Gameplay.Runners;

namespace Vectorier.Gameplay.Grid
{
    public class Grid
    {
        public static int CellWidth = 200;

        public static int CellHeight = 200;

        private List<GridColumn> _Columns = new List<GridColumn>();

        private List<QuadRunner> _Transformation = new List<QuadRunner>();

        private int _StartXIndex;

        public List<QuadRunner> Transformation => _Transformation;

        public void InitGrid(List<QuadRunner> p_quads)
        {
            for (int i = 0; i < p_quads.Count; i++)
            {
                AddQuad(p_quads[i]);
                p_quads[i].OnTransformationStart += OnTransformationStart;
                p_quads[i].OnTransformationEnd += OnTransformationEnd;
            }
        }

        public void AddQuadByList(List<QuadRunner> p_quads)
        {
            for (int i = 0; i < p_quads.Count; i++)
            {
                AddQuad(p_quads[i]);
            }
        }

        public void AddQuad(QuadRunner p_quad)
        {
            Rectangle rectangle = p_quad.rectangle;
            int num = rectangle.MinXInt < 0 ? rectangle.MinXInt / CellWidth - 1 : rectangle.MinXInt / CellWidth;
            int num2 = rectangle.MaxXInt < 0 ? rectangle.MaxXInt / CellWidth - 1 : rectangle.MaxXInt / CellWidth;
            if (_Columns.Count == 0)
            {
                _StartXIndex = num;
            }
            if (num < _StartXIndex)
            {
                int num3 = _StartXIndex - num;
                _StartXIndex = num;
                for (int i = 0; i < num3; i++)
                {
                    _Columns.Insert(0, new GridColumn());
                }
            }
            int num4 = num - _StartXIndex;
            int num5 = num2 - _StartXIndex;
            if (num5 >= _Columns.Count)
            {
                int num6 = num5 - _Columns.Count + 1;
                for (int j = 0; j < num6; j++)
                {
                    _Columns.Add(new GridColumn());
                }
            }
            for (int k = num4; k <= num5; k++)
            {
                _Columns[k].Add(p_quad);
            }
        }

        public void RemoveQuad(QuadRunner p_quad)
        {
            Rectangle rectangle = p_quad.rectangle;
            int num = rectangle.MinXInt < 0 ? rectangle.MinXInt / CellWidth - 1 : rectangle.MinXInt / CellWidth;
            int num2 = rectangle.MaxXInt < 0 ? rectangle.MaxXInt / CellWidth - 1 : rectangle.MaxXInt / CellWidth;
            int num3 = num - _StartXIndex;
            int num4 = num2 - _StartXIndex;
            for (int i = num3; i <= num4; i++)
            {
                _Columns[i].Remove(p_quad);
            }
        }

        public void RemoveQuadByList(List<QuadRunner> p_quads)
        {
            for (int i = 0; i < p_quads.Count; i++)
            {
                RemoveQuad(p_quads[i]);
            }
        }



        public void OnTransformationStart(QuadRunner p_quad)
        {
            RemoveQuad(p_quad);
            _Transformation.Add(p_quad);
        }

        public void OnTransformationEnd(QuadRunner p_quad)
        {
            _Transformation.Remove(p_quad);
            AddQuad(p_quad);
        }

        public void Collect(Rectangle p_rect, List<QuadRunner> p_quads)
        {
            p_quads.AddRange(_Transformation);
            int num = p_rect.MinXInt < 0 ? p_rect.MinXInt / CellWidth - 1 : p_rect.MinXInt / CellWidth;
            int num2 = p_rect.MaxXInt < 0 ? p_rect.MaxXInt / CellWidth - 1 : p_rect.MaxXInt / CellWidth;
            int num3 = num - _StartXIndex;
            int num4 = num2 - _StartXIndex;
            int num5 = _Columns.Count - 1;
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
                _Columns[i].Collect(p_rect, p_quads);
            }
        }

        public void Reset()
        {
            foreach (var quad in Transformation)
            {
                AddQuad(quad);
            }
            _Transformation.Clear();
        }
    }
}
