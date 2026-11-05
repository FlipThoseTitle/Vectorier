// Ported from Vector-Raylib-For-Vectorier (the game).
#nullable disable warnings
#pragma warning disable CS0169, CS0414, CS0649, CS0660, CS0661, CS8073, CS0162, CS0168, CS0219, CS0108, CS0114, CS0067 // the game's own code
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace Vectorier.Gameplay
{
    public struct Matrix
    {
        public float A;
        public float B;
        public float C;
        public float D;
        public float Tx;
        public float Ty;

        public static Matrix Identity => new Matrix
        {
            A = 1,
            B = 0,
            C = 0,
            D = 1,
            Tx = 0,
            Ty = 0
        };

        public Matrix()
        {
        }

        public bool IsIdentity()
        {
            var identity = Identity;
            return A == identity.A && B == identity.B && C == identity.C && D == identity.D;
        }

        public Vector2 TransformPoint(Vector2 point)
        {
            return new Vector2(
                A * point.X + C * point.Y + Tx,
                B * point.X + D * point.Y + Ty
            );
        }

        public Vector2 TransformPoint(float x, float y)
        {
            return new Vector2(
                A * x + C * y + Tx,
                B * x + D * y + Ty
            );
        }

        public static Matrix Translation(float x, float y)
        {
            return new Matrix
            {
                A = 1,
                D = 1,
                Tx = x,
                Ty = y
            };
        }

        public static Matrix Scale(float x, float y)
        {
            return new Matrix
            {
                A = x,
                D = y,
                Tx = 0,
                Ty = 0
            };
        }

        public static Matrix Rotation(float radians)
        {
            float cos = MathF.Cos(radians);
            float sin = MathF.Sin(radians);

            return new Matrix
            {
                A = cos,
                B = sin,
                C = -sin,
                D = cos,
                Tx = 0,
                Ty = 0
            };
        }

        public static Matrix operator *(Matrix left, Matrix right)
        {
            return new Matrix
            {
                A = left.A * right.A + left.C * right.B,
                B = left.B * right.A + left.D * right.B,

                C = left.A * right.C + left.C * right.D,
                D = left.B * right.C + left.D * right.D,

                Tx = left.A * right.Tx + left.C * right.Ty + left.Tx,
                Ty = left.B * right.Tx + left.D * right.Ty + left.Ty
            };
        }
    }
}
