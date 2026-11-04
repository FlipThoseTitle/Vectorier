// Ported from Vector-Raylib-For-Vectorier (the game's player movement), see Gameplay/README.md.
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073 // the game's own unused fields and comparisons
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Vectorier.Gameplay.Detector;
using Vectorier.Gameplay.Result;

namespace Vectorier.Gameplay.Runners
{
    public class QuadRunner : Runner
    {
        public int NameHash;

        public float Width;

        public float Height;

        public float DefaultWidth;

        public float DefaultHeight;

        public int Type;

        public bool Sticky;

        public Vector3 Point1;
        public Vector3 Point2;
        public Vector3 Point3;
        public Vector3 Point4;

        protected Vector3 Velocity;

        public Rectangle rectangle = new Rectangle();

        private List<Edge> Edges = new List<Edge>();

        /// <summary>The sides: 0 top, 1 right, 2 bottom, 3 left (read-only view for the debug overlays).</summary>
        public IReadOnlyList<Edge> EdgeList => Edges;

        public bool IsRender;

        public Action<QuadRunner> OnTransformationStart;

        public Action<QuadRunner> OnTransformationEnd;

        public QuadRunner(float x, float y, float width, float height, bool sticky, int type = 0, string name = "") : base(x, y)
        {
            Width = width;
            Height = height;

            DefaultWidth = width;
            DefaultHeight = height;

            CreatePoints();

            Type = type;
            Sticky = sticky;

            Name = name;

            if (!string.IsNullOrEmpty(name))
            {
                NameHash = Name.GetHashCode();
            }
        }

        private void CreatePoints()
        {
            Point1 = new Vector3(LocalPosition.X, LocalPosition.Y, 0);
            Point2 = new Vector3(LocalPosition.X + Width, LocalPosition.Y, 0);
            Point3 = new Vector3(LocalPosition.X + Width, LocalPosition.Y + Height, 0);
            Point4 = new Vector3(LocalPosition.X, LocalPosition.Y + Height, 0);
        }

        public void SetTempPoint(int frames)
        {
            Velocity = new Vector3(TweenPosition, 0);
        }

        public override void Move(Vector2 point)
        {
            base.Move(point);
            SetProperties();
        }

        public override void Reset()
        {
            LevelMainController.current.Location.Grid.RemoveQuad(this);

            Width = DefaultWidth;
            Height = DefaultHeight;

            LocalPosition = DefaultPosition;

            base.Reset();
            SetProperties();

            LevelMainController.current.Location.Grid.AddQuad(this);
        }

        public Vector3 GetCornerByIndex(int value)
        {
            switch (value)
            {
                case 0:
                    return Point1;
                case 1:
                    return Point2;
                case 2:
                    return Point3;
                case 3:
                    return Point4;
                default:
                    return Vector3.Zero;
            }
        }

        public virtual Vector2 GetSize(int sign)
        {
            return new Vector2(Width, Height);
        }

        public Vector2 Subtract(Vector2 Point)
        {
            return new Vector2(Point.X - LocalPosition.X, Point.Y - LocalPosition.Y);
        }

        public Vector3Line Friction(Vector3 p_end, Vector3 p_start, List<Cross> crossList)
        {
            if (!Hit(p_start.X, p_start.Y))
            {
                return null;
            }
            Vector3 vector3f = p_start;
            Vector3 vector3f2 = p_end;
            vector3f.Z = 0f;
            vector3f2.Z = 0f;
            crossList.Clear();
            CrossByEdge(p_end, p_start, crossList);
            int p_side = NearestEdge(vector3f2);
            Vector3 vector3f3 = crossList.Count != 0 ? crossList[0].Point : Closest(vector3f2, p_side);
            Vector3 vector3f4 = Closest(vector3f, p_side);
            double num = Vector3.Distance(vector3f4, vector3f3);
            double num2 = Vector3.Distance(vector3f4, vector3f);
            double num3 = num2 * 0.20000000298023224;
            var v = (vector3f3 - vector3f4);
            var normalized = v.Normalize();
            return !(num3 < num) ? new Vector3Line(vector3f3, vector3f3) : new Vector3Line(vector3f4 + normalized.Multiply(num3), vector3f3);
        }

        public int NearestEdge(Vector3 vector)
        {
            int result = 0;
            int side = 0;
            double num2 = 1.7976931348623157e+308;
            for (int i = 0; i < Edges.Count; i++)
            {
                var distance = (double)Vector3.Distance(Closest(vector, side), vector);
                int num = side;
                if (side != 0 && num2 <= distance)
                {
                    distance = num2;
                    num = result;
                }
                result = num;
                side++;
                num2 = distance;
            }
            return result;
        }

        public static bool IsCrossIndex(List<Cross> crossList1, List<Cross> crossList2)
        {
            foreach (Cross item in crossList1)
            {
                foreach (Cross item2 in crossList2)
                {
                    if (item.Index == item2.Index)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void DetectorAffiliation(DetectorLine detector, Affiliation result)
        {
            result.Clear();
            var start = detector.Start;
            var end = detector.End;
            end.Start = end.Start.Add(Velocity);
            end.End = end.End.Add(Velocity);
            CrossByEdge(start.End, end.End, result.CrossList1);
            CrossByEdge(start.Start, end.Start, result.CrossList2);
            CrossByEdge(start.Start, start.End, result.CrossList3);
            var startX = Utilities.Math.Round(start.Start.X, 1000);
            var startY = Utilities.Math.Round(start.Start.Y, 1000);
            bool hit = Hit(startX, startY, true);
            var endX = Utilities.Math.Round(start.End.X, 1000);
            var endY = Utilities.Math.Round(start.End.Y, 1000);
            bool hit2 = Hit(endX, endY, true);
            result.SetType(hit, hit2);
            if (result.Type > 0)
            {
                return;
            }
            result.Clear();

        }

        public static Cross MinCross(List<Cross> crossList, Vector3 point)
        {
            double num = double.MaxValue;
            Cross result = new Cross();
            foreach (Cross p_cross in crossList)
            {
                double num2 = Vector3.Distance(p_cross.Point, point);
                if (!(num2 > num))
                {
                    num = num2;
                    result = p_cross;
                }
            }
            return result;
        }

        public static int Side(DetectorLine detector, int sign)
        {
            switch (detector.Type)
            {
                case DetectorLine.DetectorType.Vertical:
                    return sign != 1 ? 1 : 3;
                case DetectorLine.DetectorType.Horizontal:
                    return 0;
                default:
                    return -1;
            }
        }

        public static int SideForType5Or6(DetectorLine p_detector, Rectangle p_rect, int p_sign, int p_type)
        {
            switch (p_detector.Type)
            {
                case DetectorLine.DetectorType.Vertical:
                    {
                        Vector3 vector3f = default;
                        vector3f = p_type != 5 ? p_detector.Start.End : p_detector.Start.Start;
                        if (vector3f.X >= p_rect.MinX - 0.01f && vector3f.X <= p_rect.MinX + 0.01f)
                        {
                            return 3;
                        }
                        if (vector3f.X >= p_rect.MaxX - 0.01f && vector3f.X <= p_rect.MaxX + 0.01f)
                        {
                            return 1;
                        }
                        if (vector3f.Y >= p_rect.MinY - 0.01f && vector3f.Y <= p_rect.MinY + 0.01f)
                        {
                            return 0;
                        }
                        if (vector3f.Y >= p_rect.MaxY - 0.01f && vector3f.Y <= p_rect.MaxY + 0.01f)
                        {
                            return 2;
                        }
                        return p_sign != 1 ? 1 : 3;
                    }
                case DetectorLine.DetectorType.Horizontal:
                    return 0;
                default:
                    return -1;
            }
        }

        public int Side(DetectorLine detector, Affiliation affiliationResult, int sign)
        {
            Vector3Line start = detector.Start;
            Vector3Line end = detector.End;
            Vector3Line perpendicular = detector.Perpendicular;
            List<Cross> list = new List<Cross>();
            Cross cross;
            switch (affiliationResult.Type)
            {
                case 1:
                    return !IsCrossIndex(affiliationResult.CrossList1, affiliationResult.CrossList2) ? Side(detector, sign) : MinCross(affiliationResult.CrossList1, end.Start).Index;
                case 2:
                    CrossByEdge(start.End, perpendicular.End, list);
                    cross = list.Count != 0 ? MinCross(list, end.End) : MinCross(affiliationResult.CrossList1, end.End);
                    return cross.Index;
                case 3:
                    CrossByEdge(start.Start, perpendicular.Start, list);
                    cross = list.Count != 0 ? MinCross(list, end.Start) : MinCross(affiliationResult.CrossList2, end.Start);
                    return cross.Index;
                case 4:
                    return Side(detector, sign);
                case 5:
                    return SideForType5Or6(detector, rectangle, sign, 5);
                case 6:
                    return SideForType5Or6(detector, rectangle, sign, 6);
                default:
                    return -1;
            }
        }

        public void CrossByEdge(Vector3 p1, Vector3 p2, List<Cross> result)
        {
            for (int i = 0; i < Edges.Count; i++)
            {
                Vector3? vector3f = VectorUtils.Cross(p1, p2, Edges[i].Point1, Edges[i].Point2);
                if (vector3f.HasValue)
                {
                    result.Add(new Cross(vector3f.Value, i));
                }
            }
        }

        public Vector3 DeltaEdge(Vector3 vector, int side)
        {
            if (side == -1)
            {
                return new Vector3(0, 0, 0);
            }
            return Closest(vector, side) - vector;
        }

        public Vector3 Closest(Vector3 vector, int side)
        {
            Vector3 point = Edges[side].Point1;
            Vector3 p_lineDirection = Edges[side].Point2 - point;
            return VectorUtils.Closest(vector, point, p_lineDirection);
        }

        public double Distance(Vector3 point, Vector3 v1, Vector3 v2)
        {
            Vector3 vector3f = new Vector3(v2.Y - v1.Y, v1.X - v2.X, 0f);
            vector3f = vector3f.Normalize();
            return vector3f.MultiplyNum(point - v1);
        }

        public Vector3? Corner(int sign, int p_cornernum)
        {
            if (sign > 0)
            {
                switch (p_cornernum)
                {
                    case 0:
                        return Point1;
                    case 1:
                        return Point2;
                    case 2:
                        return Point3;
                    case 3:
                        return Point4;
                }
            }
            else
            {
                switch (p_cornernum)
                {
                    case 0:
                        return Point2;
                    case 1:
                        return Point1;
                    case 2:
                        return Point4;
                    case 3:
                        return Point3;
                }
            }
            return null;
        }

        public bool Hit(Vector3 point, bool equality = false)
        {
            return Hit(point.X, point.Y, equality);
        }

        public virtual bool Hit(float x, float y, bool equality = false)
        {
            return rectangle.Contains(x, y, 0.01f);
        }

        public void SetProperties(bool IsPoint = true)
        {
            CalcPoints();
            SetEdge();
            SetRectangle();
        }

        protected virtual void SetRectangle()
        {
            rectangle.Set(LocalPosition.X, LocalPosition.Y, Width, Height);
        }
        protected virtual void CalcPoints()
        {
            Point1.Set(LocalPosition.X, LocalPosition.Y, 0);
            Point2.Set(LocalPosition.X + Width, LocalPosition.Y, 0);
            Point3.Set(LocalPosition.X + Width, LocalPosition.Y + Height, 0);
            Point4.Set(LocalPosition.X, LocalPosition.Y + Height, 0);
        }

        public void SetEdge()
        {
            Edges.Clear();
            Edges.Add(new Edge
            {
                Point1 = Point1,
                Point2 = Point2
            });
            Edges.Add(new Edge
            {
                Point1 = Point2,
                Point2 = Point3
            });
            Edges.Add(new Edge
            {
                Point1 = Point3,
                Point2 = Point4
            });
            Edges.Add(new Edge
            {
                Point1 = Point4,
                Point2 = Point1
            });
        }

        public override void TransformationStart()
        {
            if (ActiveTransformation == 0)
            {
                OnTransformationStart?.Invoke(this);
            }
            base.TransformationStart();
        }

        public override void TransformationEnd()
        {
            base.TransformationEnd();
            if (ActiveTransformation == 0)
            {
                OnTransformationEnd?.Invoke(this);
            }
        }

        public override void TransformResize(Vector2 size)
        {
            //var scale = _CachedTransform.localScale;
            //scale.x = size.X / _WidthQuad;
            //scale.y = size.Y / _HeightQuad;
            //_CachedTransform.localScale = scale;

            Width = size.X;
            Height = size.Y;
            SetProperties();
        }

        public override void TransformRotate(float angle)
        {
        }
    }
}
