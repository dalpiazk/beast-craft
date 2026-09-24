using System;
using System.Globalization;

namespace BeastCraft.Presentation.Board
{
    /// <summary>A 2D point or offset in virtual-screen pixels (engine-neutral; the host converts it).</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public static readonly Vec2 Zero = new Vec2(0f, 0f);

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X + b.X, a.Y + b.Y);
        }

        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.X - b.X, a.Y - b.Y);
        }

        public static Vec2 operator *(Vec2 a, float k)
        {
            return new Vec2(a.X * k, a.Y * k);
        }

        /// <summary>The point <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (t in 0-1, unclamped).</summary>
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public bool Equals(Vec2 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y);
        }

        public override bool Equals(object obj)
        {
            return obj is Vec2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (X.GetHashCode() * 397) ^ Y.GetHashCode();
        }

        public override string ToString()
        {
            return "(" + X.ToString("0.##", CultureInfo.InvariantCulture) + ", " + Y.ToString("0.##", CultureInfo.InvariantCulture) + ")";
        }
    }
}
