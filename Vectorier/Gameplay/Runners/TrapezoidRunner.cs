#nullable disable warnings
using System;
using System.Numerics;
using Vectorier.Gameplay.Level;
using Vectorier.Gameplay.Rl;

namespace Vectorier.Gameplay.Runners;

public class TrapezoidRunner : QuadRunner
{

    private int _type;

    private float _height1;

    private float _height_L;

    private float _height_R;

    public TrapezoidRunner(string name, int type, float x, float y, float width, float height, float height1, bool stikly)
        : base(x, y, width, height, stikly, type, name)
    {
        _type = type;
        _height1 = height1;

        _height_L = height;
        _height_R = height1;
        Height = Math.Max(_height_L, _height_R);
    }

    protected override void CalcPoints()
    {
        Point1.Set(LocalPosition.X, LocalPosition.Y, 0f);
        Point2.Set(LocalPosition.X + Width, LocalPosition.Y + (_height_L - _height_R), 0f);
        Point3.Set(LocalPosition.X + Width, LocalPosition.Y + _height_L, 0f);
        Point4.Set(LocalPosition.X, LocalPosition.Y + _height_L, 0f);
    }

    protected override void SetRectangle()
    {
        rectangle.Set((float)LocalPosition.X, (float)Math.Min(Point1.Y, Point2.Y), Width, Math.Max(_height_L, _height_R));
    }

    public override Vector2 GetSize(int sign)
    {
        return sign <= 0 ? new Vector2(Width, _height_R) : new Vector2(Width, _height_L);
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

    public override void Draw()
    {
        if (!IsEnabled)
            return;

        var vs = Sets.Current.Containers[1];

        Vector2 a = vs.TransformPoint(Point1);
        Vector2 b = vs.TransformPoint(Point2);
        Vector2 c = vs.TransformPoint(Point3);
        Vector2 d = vs.TransformPoint(Point4);

        Rlgl.Begin(DrawMode.Quads);

        Rlgl.Color4ub(0, 0, 255, 64);

        Rlgl.Vertex2f(a.X, a.Y);
        Rlgl.Vertex2f(b.X, b.Y);
        Rlgl.Vertex2f(c.X, c.Y);
        Rlgl.Vertex2f(d.X, d.Y);

        Rlgl.End();
    }

    public override void TransformResize(Vector2 size)
    {

    }
}
