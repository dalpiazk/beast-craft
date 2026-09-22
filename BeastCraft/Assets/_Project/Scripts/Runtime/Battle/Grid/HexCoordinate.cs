using System;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A tile address on the hexagonal battle grid, in axial coordinates.
    /// <para>
    /// Axial storage keeps two components (<see cref="Q"/>, <see cref="R"/>) rather than three; the
    /// implied cube coordinate is (Q, R, -Q-R), which is what the distance formula reconstructs.
    /// Deliberately a plain value type with no Unity dependency: this is pure board math, and
    /// keeping it free of <c>UnityEngine</c> means it stays testable and cheap to copy.
    /// </para>
    /// </summary>
    public readonly struct HexCoordinate : IEquatable<HexCoordinate>
    {
        /// <summary>The origin tile, at the centre of a hexagon-shaped board.</summary>
        public static readonly HexCoordinate Zero = new HexCoordinate(0, 0);

        public HexCoordinate(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>First axial axis (conventionally the "column" axis).</summary>
        public int Q { get; }

        /// <summary>Second axial axis (conventionally the "row" axis).</summary>
        public int R { get; }

        /// <summary>The third, implied cube coordinate. Always equal to <c>-Q - R</c>.</summary>
        public int S
        {
            get { return -Q - R; }
        }

        /// <summary>
        /// Step distance to another tile, counted in hex steps. This is the cube distance
        /// (half the sum of the absolute component deltas) expressed in axial terms.
        /// </summary>
        public int Distance(HexCoordinate other)
        {
            int deltaQ = Q - other.Q;
            int deltaR = R - other.R;
            return (Math.Abs(deltaQ) + Math.Abs(deltaQ + deltaR) + Math.Abs(deltaR)) / 2;
        }

        /// <summary>Component-wise sum, for offsetting a tile by a direction vector.</summary>
        public static HexCoordinate operator +(HexCoordinate a, HexCoordinate b)
        {
            return new HexCoordinate(a.Q + b.Q, a.R + b.R);
        }

        public static bool operator ==(HexCoordinate a, HexCoordinate b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(HexCoordinate a, HexCoordinate b)
        {
            return !a.Equals(b);
        }

        public bool Equals(HexCoordinate other)
        {
            return Q == other.Q && R == other.R;
        }

        public override bool Equals(object obj)
        {
            return obj is HexCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            // Small board coordinates, so a cheap mix is plenty; keeps the struct allocation-free.
            unchecked
            {
                return (Q * 397) ^ R;
            }
        }

        public override string ToString()
        {
            return "Hex(" + Q + ", " + R + ")";
        }
    }
}
