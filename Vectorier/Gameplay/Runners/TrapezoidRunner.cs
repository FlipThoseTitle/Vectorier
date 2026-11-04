// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Location;

namespace Vectorier.Gameplay.Runners
{
    public class TrapezoidRunner : QuadRunner
    {

        private int _type;

        private float _height1;

        private float _Height_L;

        private float _Height_R;

        public TrapezoidRunner(string name, int type, float x, float y, float width, float height, float height1, bool stikly)
            : base(x, y, width, height, stikly, type, name)
        {
            _type = type;
            _height1 = height1;

            _Height_L = height;
            _Height_R = height1;
            Height = Math.Max(_Height_L, _Height_R);
        }

        protected override void CalcPoints()
        {
            Point1.Set(LocalPosition.X, LocalPosition.Y, 0f);
            Point2.Set(LocalPosition.X + Width, LocalPosition.Y + (_Height_L - _Height_R), 0f);
            Point3.Set(LocalPosition.X + Width, LocalPosition.Y + _Height_L, 0f);
            Point4.Set(LocalPosition.X, LocalPosition.Y + _Height_L, 0f);
        }

        protected override void SetRectangle()
        {
            rectangle.Set((float)LocalPosition.X, (float)Math.Min(Point1.Y, Point2.Y), Width, Math.Max(_Height_L, _Height_R));
        }

        public override Vector2 GetSize(int p_sign)
        {
            return p_sign <= 0 ? new Vector2(Width, _Height_R) : new Vector2(Width, _Height_L);
        }

        public override bool Hit(float x, float y, bool equality)
        {
            Vector2 Vector2 = new Vector2(x, y);
            Vector2 Vector22 = new Vector2(Vector2.X - Point4.X, Vector2.Y - Point4.Y);
            Vector2 Vector23 = new Vector2(Point4.Y - Point3.Y, Point3.X - Point4.X);
            Vector2 Vector24 = new Vector2(Vector2.X - Point1.X, Vector2.Y - Point1.Y);
            Vector2 Vector25 = new Vector2(Point1.Y - Point2.Y, Point2.X - Point1.X);
            Vector2 Vector26 = new Vector2(Vector2.X - Point3.X, Vector2.Y - Point3.Y);
            Vector2 Vector27 = new Vector2(Point3.Y - Point2.Y, Point2.X - Point3.X);
            Vector2 Vector28 = new Vector2(Vector2.X - Point4.X, Vector2.Y - Point4.Y);
            Vector2 Vector29 = new Vector2(Point4.Y - Point1.Y, Point1.X - Point4.X);
            float num = VectorUtils.MultiplyNum(ref Vector22, Vector23);
            float num2 = VectorUtils.MultiplyNum(ref Vector24, Vector25);
            float num3 = VectorUtils.MultiplyNum(ref Vector26, Vector27);
            float num4 = VectorUtils.MultiplyNum(ref Vector28, Vector29);
            float num5 = num * num2;
            float num6 = num3 * num4;
            return equality ? num5 <= 0.0 && num6 <= 0.0 : num5 < 0.0 && num6 < 0.0;
        }

        //public override bool Hit(double x, double y, bool equality)
        //{
        //    var p1X = Point1.X;
        //    var p1Y = Point1.Y;
        //    var p2X = Point2.X;
        //    var p2Y = Point2.Y;
        //    var p3X = Point3.X;
        //    var p3Y = Point3.Y;
        //    var p4X = Point4.X;
        //    var p4Y = Point4.Y;
        //    var num = ((x - p4X) * (p4Y - p3Y) + (y - p4Y) * (p3X - p4X)) * ((x - p1X) * (p1Y - p2Y) + (y - p1Y) * (p2X - p1X));
        //    var num2 = ((y - p4Y) * (p1X - p4X) + (x - p4X) * (p4Y - p1Y)) * ((x - p3X) * (p3Y - p2Y) + (y - p4Y) * (p2X - p3X));
        //    bool flag1 = equality ? num <= 0 : num < 0;
        //    bool flag2 = equality ? num2 <= 0 : num2 < 0;
        //    return flag1 && flag2;
        //}
        public override void Draw()
        {
            // Drawing is done by the editor (Controls/Scene/SceneView.Gameplay.cs).
        }

        public override void TransformResize(Vector2 size)
        {

        }
    }
}
