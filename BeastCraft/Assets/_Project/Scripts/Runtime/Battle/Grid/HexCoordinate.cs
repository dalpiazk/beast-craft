using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace BeastCraft.Battle.Grid
{
    /// <summary>
    /// A tile address on the hexagonal battle grid, in axial coordinates.
    /// <para>
    /// Axial storage keeps two components (<see cref="Q"/>, <see cref="R"/>) rather than three; the
    /// implied cube coordinate is (Q, R, -Q-R), which is what the distance formula reconstructs.
    /// Deliberately a plain value type with no engine dependency: this is pure board math, and
    /// keeping it free of engine types means it stays testable and cheap to copy.
    /// </para>
    /// </summary>
    public readonly struct HexCoordinate : IEquatable<HexCoordinate>
    {
        /// <summary>The origin tile, at the centre of a hexagon-shaped board.</summary>
        public static readonly HexCoordinate Zero = new HexCoordinate(0, 0);

        /// <summary>
        /// The six axial direction vectors, in the fixed order described below. Offsetting a tile by
        /// each in turn gives that tile's neighbours, which is exactly what
        /// <see cref="Neighbors"/> does.
        /// <para>
        /// The same instance every time, so walking it allocates nothing. That matters on the two
        /// paths that walk it constantly — one A* node expansion per open-set pop, and one sweep per
        /// <c>Line</c> or <c>Cross</c> skill cast — which is why those prefer it over
        /// <see cref="Neighbors"/>. Read-only in fact and not merely in signature: the underlying
        /// table is shared, so it is wrapped rather than exposed.
        /// </para>
        /// </summary>
        public static IReadOnlyList<HexCoordinate> AxialDirections
        {
            get { return DirectionView; }
        }

        // The six axial direction vectors, in a fixed order. Order is arbitrary geometrically but
        // deliberately stable: it is what makes neighbour iteration -- and therefore any search
        // that walks neighbours -- produce the same result run to run.
        private static readonly HexCoordinate[] Directions =
        {
            new HexCoordinate(1, 0),
            new HexCoordinate(1, -1),
            new HexCoordinate(0, -1),
            new HexCoordinate(-1, 0),
            new HexCoordinate(-1, 1),
            new HexCoordinate(0, 1)
        };

        // Handed out by AxialDirections. A genuinely immutable wrapper rather than the raw array:
        // the array is shared process-wide, so exposing it as IReadOnlyList<HexCoordinate> alone
        // would leave a cast back to HexCoordinate[] able to rewrite the board's geometry for
        // everyone. Built once, so reading it allocates nothing.
        private static readonly ReadOnlyCollection<HexCoordinate> DirectionView =
            Array.AsReadOnly(Directions);

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

        /// <summary>
        /// The six tiles one step away, in a fixed order. Pure coordinate math: it knows nothing
        /// about board bounds, terrain or occupancy, so callers must filter the results themselves.
        /// <para>
        /// This necessarily allocates: the tiles are <c>this</c> offset by each direction, so unlike
        /// <see cref="AxialDirections"/> there is no fixed table to hand back. A caller in a tight
        /// loop should walk <see cref="AxialDirections"/> and add <c>this</c> itself, which yields
        /// the same tiles in the same order for nothing.
        /// </para>
        /// </summary>
        public IReadOnlyList<HexCoordinate> Neighbors()
        {
            HexCoordinate[] neighbors = new HexCoordinate[Directions.Length];
            for (int i = 0; i < Directions.Length; i++)
            {
                neighbors[i] = this + Directions[i];
            }

            return neighbors;
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
