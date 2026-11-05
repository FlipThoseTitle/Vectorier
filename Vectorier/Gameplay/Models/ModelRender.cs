// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Vectorier.Gameplay.Rl;
using Vectorier.Gameplay.Geometry;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Node;

namespace Vectorier.Gameplay.Models
{
    public class ModelRender : IDrawable
    {
        private Color _Color;

        private bool _debugMode;

        private bool _IsEnabled = true;

        private List<Capsule> _Capsules = new List<Capsule>();

        private List<Triangle> _Triangles = new List<Triangle>();

        public float Z { get; set; } = -15;

        public Color Color
        {
            get => _Color;
            set => _Color = value;
        }

        public string Name
        {
            get;
            set;
        }

        public bool DebugMode
        {
            get => _debugMode;
            set => _debugMode = value;
        }

        public bool IsEnabled
        {
            get => _IsEnabled;
            set
            {
                _IsEnabled = value;
            }
        }

        public List<Capsule> Capsules => _Capsules;

        public List<Triangle> Triangles => _Triangles;

        public ModelRender()
        {
            // In the editor, models outside a level (previews, the Move Visualizer's still poses) are not the level's.
            if (Detached)
            {
                return;
            }
            GlobalObjects.AddDrawable(this);
            Z += Sets.Current.Containers[1].Z;
        }

        public void Draw()
        {
            foreach (var capsule in _Capsules)
            {
                capsule.Draw(Color);
            }
            foreach (var triangle in _Triangles)
            {
                triangle.Draw(Color);
            }
        }

        /// <summary>Set while the editor builds a model that does not belong to the level being played.</summary>
        [ThreadStatic]
        public static bool Detached;

        /// <summary>The editor's drawing of the same capsules and triangles, into one geometry (model previews).</summary>
        public void Draw(ModelCanvas canvas)
        {
            foreach (var capsule in _Capsules)
            {
                capsule.Draw(canvas);
            }
            foreach (var triangle in _Triangles)
            {
                triangle.Draw(canvas);
            }
        }

        public void Add(List<ModelNode> Nodes)
        {
        }

        public void Add(ModelNode Node)
        {
        }

        public void Add(List<ModelLine> Lines)
        {
        }

        public void Add(ModelLine modelLine)
        {
            switch (modelLine.Type)
            {
                case "Capsule":
                    var capsule = new Capsule();
                    capsule.Init(modelLine);
                    _Capsules.Add(capsule);
                    break;
            }
        }

        public void Add(List<Triangle> Triangles)
        {
            _Triangles.AddRange(Triangles);
        }

        public void Add(Triangle Triangles)
        {
            _Triangles.Add(Triangles);
        }

        public void Add(Model Model)
        {
        }

        public Capsule GetCapsulTransform(string name)
        {
            foreach (var c in _Capsules)
            {
                if (c.Name == name)
                {
                    return c;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// What the game's Raylib calls draw into, in the editor: one filled geometry (the model is a single colour).
    /// <see cref="Transform"/> maps a node (level coordinates, Y down) to the canvas, like the game's
    /// Sets.Current.Containers[1].TransformPoint, and <see cref="Scale"/> is its FrameScale.
    /// </summary>
    public sealed class ModelCanvas
    {
        private readonly Avalonia.Media.StreamGeometryContext _ctx;
        private double _minX = double.MaxValue, _minY = double.MaxValue, _maxX = double.MinValue, _maxY = double.MinValue;

        public ModelCanvas(Avalonia.Media.StreamGeometryContext ctx, Func<Vector3, Avalonia.Point> transform, double scale = 1)
        {
            _ctx = ctx;
            Transform = transform;
            Scale = scale;
            _ctx.SetFillRule(Avalonia.Media.FillRule.NonZero);
        }

        public Func<Vector3, Avalonia.Point> Transform { get; }

        public double Scale { get; }

        /// <summary>What was drawn, in canvas units (empty when nothing was).</summary>
        public Avalonia.Rect Bounds => _minX <= _maxX ? new Avalonia.Rect(_minX, _minY, _maxX - _minX, _maxY - _minY) : default;

        public bool IsEmpty => _minX > _maxX;

        private void Grow(Avalonia.Point p, double r)
        {
            _minX = Math.Min(_minX, p.X - r); _minY = Math.Min(_minY, p.Y - r);
            _maxX = Math.Max(_maxX, p.X + r); _maxY = Math.Max(_maxY, p.Y + r);
        }

        /// <summary>Raylib.DrawCircleV.</summary>
        public void Circle(Avalonia.Point c, double r)
        {
            if (!(r > 0) || double.IsNaN(c.X) || double.IsNaN(c.Y))
                return;
            _ctx.BeginFigure(new Avalonia.Point(c.X + r, c.Y), true);
            _ctx.ArcTo(new Avalonia.Point(c.X - r, c.Y), new Avalonia.Size(r, r), 0, false, Avalonia.Media.SweepDirection.Clockwise);
            _ctx.ArcTo(new Avalonia.Point(c.X + r, c.Y), new Avalonia.Size(r, r), 0, false, Avalonia.Media.SweepDirection.Clockwise);
            _ctx.EndFigure(true);
            Grow(c, r);
        }

        /// <summary>Raylib.DrawLineEx: a line of a thickness with square-cut ends.</summary>
        public void Line(Avalonia.Point a, Avalonia.Point b, double thickness)
        {
            var d = b - a;
            double length = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            if (length < 1e-9 || !(thickness > 0))
                return;
            var n = new Avalonia.Vector(-d.Y / length, d.X / length) * (thickness / 2);
            Polygon(a + n, b + n, b - n, a - n);
        }

        /// <summary>
        /// A filled polygon (Rlgl triangles with back-face culling off). Every figure winds the same way on screen,
        /// like the circles, so overlapping shapes add up instead of cancelling out under the NonZero rule.
        /// </summary>
        public void Polygon(params Avalonia.Point[] pts)
        {
            if (pts.Length < 3)
                return;
            double area = 0;
            for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
                area += pts[j].X * pts[i].Y - pts[i].X * pts[j].Y;
            if (Math.Abs(area) < 1e-12 || double.IsNaN(area))
                return; // degenerate: nothing to fill
            if (area > 0)
            {
                _ctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++)
                    _ctx.LineTo(pts[i]);
            }
            else
            {
                _ctx.BeginFigure(pts[^1], true);
                for (int i = pts.Length - 2; i >= 0; i--)
                    _ctx.LineTo(pts[i]);
            }
            _ctx.EndFigure(true);
            foreach (var p in pts)
                Grow(p, 0);
        }
    }
}
