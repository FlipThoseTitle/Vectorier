#nullable disable warnings
using System.Drawing;
using System.Numerics;

namespace Vectorier.Gameplay;

// == compares positions and sizes; Equals/GetHashCode stay reference-based (rectangles are mutable).
#pragma warning disable CS0660, CS0661
public class Rectangle
{
    public Vector2 Size;

    public Vector2 Origin;

    public float MinX => Origin.X;

    public int MinXInt => (int)Origin.X;

    public float MaxX => Origin.X + Size.X;

    public int MaxXInt => (int)(Origin.X + Size.X);

    public float MinY => Origin.Y;

    public int MinYInt => (int)Origin.Y;

    public float MaxY => Origin.Y + Size.Y;

    public int MaxYInt => (int)(Origin.Y + Size.Y);

    public Rectangle(float x = 0f, float y = 0f, float width = 0f, float height = 0f)
    {
        Origin = new Vector2(x, y);
        Size = new Vector2(width, height);
    }

    public Rectangle(Rectangle rectangle)
    {
        Origin = new Vector2(rectangle.Origin.X, rectangle.Origin.Y);
        Size = new Vector2(rectangle.Size.X, rectangle.Size.Y);
    }

    public static implicit operator Vectorier.Gameplay.Rl.Rectangle(Rectangle rect)
    {
        return new Vectorier.Gameplay.Rl.Rectangle(rect.MinX, rect.MinY, rect.Size.X, rect.Size.Y);
    }

    public static implicit operator Rectangle(Vectorier.Gameplay.Rl.Rectangle rect)
    {
        return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
    }

    public static bool operator ==(Rectangle rectangle1, Rectangle rectangle2)
    {
        if (ReferenceEquals(rectangle1, rectangle2))
        {
            return true;
        }
        if (ReferenceEquals(rectangle1, null) || ReferenceEquals(rectangle2, null))
        {
            return false;
        }
        return rectangle1.Origin == rectangle2.Origin && rectangle1.Size == rectangle2.Size;
    }

    public static bool operator !=(Rectangle rectangle1, Rectangle rectangle2)
    {
        return !(rectangle1 == rectangle2);
    }

    public void Set(float x, float y, float width, float height)
    {
        Origin.X = x;
        Origin.Y = y;
        Size.X = width;
        Size.Y = height;
    }

    public void Set(Rectangle rectangle)
    {
        Origin.X = rectangle.Origin.X;
        Origin.Y = rectangle.Origin.Y;
        Size.X = rectangle.Size.X;
        Size.Y = rectangle.Size.Y;
    }

    public bool Contains(Point point)
    {
        return point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
    }
    public bool Contains(double x, double y)
    {
        return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
    }
    public bool Contains(double x, double y, float epsilon)
    {
        return x >= MinX - epsilon && x <= MaxX + epsilon && y >= MinY - epsilon && y <= MaxY + epsilon;
    }
    public bool Contains(Vector3 point)
    {
        return point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
    }
    public bool Contains(Vector3 point, float epsilon)
    {
        return point.X >= MinX - epsilon && point.X <= MaxX + epsilon && point.Y >= MinY - epsilon && point.Y <= MaxY + epsilon;
    }

    public bool Intersect(Rectangle rect)
    {
        return !(MaxX < rect.MinX) && !(rect.MaxX < MinX) && !(MaxY < rect.MinY) && !(rect.MaxY < MinY);
    }
}
