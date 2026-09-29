using System;
using System.Collections.Generic;
using System.Text;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The fog over a stage's region map (producer decision: it persists per region and never
    /// fogs again on a replay). Fog is kept on a fixed grid in the map's own logical coordinates, not
    /// on the nodes: a replay draws a new node map from a new seed, but the ground already seen stays
    /// seen, and the stage's points of interest (<c>BeastCraft.Discovery.PoiLayout</c>) sit on the
    /// same grid.
    /// <para>
    /// <strong>The grid</strong> (<see cref="FogGrid"/>) has a row for every map row and one between
    /// each pair (half-rows 0 to 2 × top), and a column for every lane and one between each pair and
    /// at each edge (columns 0 to 2 × lanes): a location of row <c>L</c> and lane <c>n</c> sits at
    /// half-row <c>2L</c>, column <c>2n + 1</c>; the top (the pass or the lair) at the centre column.
    /// Points of interest sit between them, at odd half-rows and even columns, so they are always
    /// near the trail and never on it.
    /// </para>
    /// <para>
    /// <strong>Reveal rules.</strong> An expedition's start lifts the trailhead band (the first row
    /// and the half-row above it) and the top location's cell (the pass or the lair is always in
    /// sight). Clearing (or visiting) a location lifts an ellipse around it
    /// (<see cref="NodeRevealRows"/> half-rows by <see cref="NodeRevealCols"/> columns: its
    /// neighbours and the points of interest near it) and the cell of every location it leads to, and
    /// records the row reached (<see cref="StageFog.DeepestLayer"/>). A Vista lifts a much larger
    /// ellipse (<see cref="VistaRevealRows"/> by <see cref="VistaRevealCols"/>). Nothing ever fogs a
    /// cell again. Tutorial regions have no fog (Hearthglen is shown fully revealed).
    /// </para>
    /// Pure: no randomness, so the pacing simulator (which runs these rules) never moves.
    /// </summary>
    public static class MapFog
    {
        /// <summary>A cleared location's reveal ellipse: half-rows (a map row is two).</summary>
        public const double NodeRevealRows = 2.6;

        /// <summary>A cleared location's reveal ellipse: columns (a lane is two).</summary>
        public const double NodeRevealCols = 3.2;

        /// <summary>A Vista's reveal ellipse: half-rows.</summary>
        public const double VistaRevealRows = 7.0;

        /// <summary>A Vista's reveal ellipse: columns.</summary>
        public const double VistaRevealCols = 9.0;

        /// <summary>The fog grid of a stage map generated under <paramref name="rules"/>.</summary>
        public static FogGrid GridFor(MapRulesData rules)
        {
            return new FogGrid(Math.Max(1, (rules == null ? 3 : Math.Max(3, rules.Layers)) - 1), Math.Max(1, rules == null ? 1 : rules.Lanes));
        }

        /// <summary>The fog grid of <paramref name="region"/>'s stage maps (its effective rules, <see cref="RegionLibrary.RulesFor(RegionData)"/>).</summary>
        public static FogGrid GridFor(RegionLibrary library, RegionData region)
        {
            return GridFor(library == null || region == null ? null : library.RulesFor(region));
        }

        /// <summary>The cell a location sits in.</summary>
        public static int CellOf(FogGrid grid, MapNode node)
        {
            if (node == null)
            {
                return -1;
            }

            bool top = node.Layer >= grid.MapRows;
            int col = top ? grid.Lanes : (2 * Clamp(node.Lane, 0, grid.Lanes - 1)) + 1;
            return grid.Index(2 * Clamp(node.Layer, 0, grid.MapRows), col);
        }

        /// <summary>Whether cell <paramref name="cell"/> of <paramref name="fog"/> has been seen (a null fog has seen nothing).</summary>
        public static bool IsRevealed(StageFog fog, int cell)
        {
            return fog != null && cell >= 0 && Get(fog.Cells, cell);
        }

        /// <summary>Whether location <paramref name="node"/> stands on seen ground.</summary>
        public static bool IsRevealed(StageFog fog, FogGrid grid, MapNode node)
        {
            return IsRevealed(fog, CellOf(grid, node));
        }

        /// <summary>How many cells of <paramref name="fog"/> are seen.</summary>
        public static int CountRevealed(StageFog fog, FogGrid grid)
        {
            int count = 0;
            for (int i = 0; i < grid.CellCount; i++)
            {
                count += IsRevealed(fog, i) ? 1 : 0;
            }

            return count;
        }

        /// <summary>Lifts cell <paramref name="cell"/>. Returns whether it was fogged.</summary>
        public static bool Reveal(StageFog fog, int cell)
        {
            if (fog == null || cell < 0 || Get(fog.Cells, cell))
            {
                return false;
            }

            fog.Cells = Set(fog.Cells, cell);
            return true;
        }

        /// <summary>
        /// Lifts every cell inside the ellipse of <paramref name="radiusRows"/> half-rows by
        /// <paramref name="radiusCols"/> columns around (<paramref name="halfRow"/>, <paramref name="col"/>).
        /// Returns how many were fogged.
        /// </summary>
        public static int RevealEllipse(StageFog fog, FogGrid grid, int halfRow, int col, double radiusRows, double radiusCols)
        {
            int lifted = 0;
            for (int r = 0; r < grid.HalfRows; r++)
            {
                for (int c = 0; c < grid.Cols; c++)
                {
                    double dr = (r - halfRow) / radiusRows;
                    double dc = (c - col) / radiusCols;
                    if ((dr * dr) + (dc * dc) <= 1.0 && Reveal(fog, grid.Index(r, c)))
                    {
                        lifted++;
                    }
                }
            }

            return lifted;
        }

        /// <summary>Lifts every cell of the stage (a stage explored before fog existed: see the schema-8 migration).</summary>
        public static void RevealAll(StageFog fog, FogGrid grid)
        {
            for (int i = 0; i < grid.CellCount; i++)
            {
                Reveal(fog, i);
            }

            fog.DeepestLayer = Math.Max(fog.DeepestLayer, grid.MapRows);
        }

        /// <summary>
        /// An expedition into stage <paramref name="stage"/> starts: the trailhead band (half-rows 0
        /// and 1, every column) and the top location's cell are lifted. Returns the stage's fog.
        /// </summary>
        public static StageFog StartStage(RegionProgress progress, FogGrid grid, int stage)
        {
            StageFog fog = progress.FogOf(stage);
            for (int r = 0; r < Math.Min(2, grid.HalfRows); r++)
            {
                for (int c = 0; c < grid.Cols; c++)
                {
                    Reveal(fog, grid.Index(r, c));
                }
            }

            Reveal(fog, grid.Index(2 * grid.MapRows, grid.Lanes));
            return fog;
        }

        /// <summary>
        /// Location <paramref name="node"/> of <paramref name="nodes"/> (stage <paramref name="stage"/>)
        /// was cleared or visited: its ellipse and the cells of the locations it leads to are lifted, and
        /// the row reached recorded. Returns the stage's fog.
        /// </summary>
        public static StageFog OnCleared(RegionProgress progress, FogGrid grid, int stage, MapNode node, IList<MapNode> nodes)
        {
            StageFog fog = progress.FogOf(stage);
            if (node == null)
            {
                return fog;
            }

            int cell = CellOf(grid, node);
            RevealEllipse(fog, grid, grid.RowOf(cell), grid.ColOf(cell), NodeRevealRows, NodeRevealCols);
            foreach (int next in node.Next ?? new int[0])
            {
                MapNode target = nodes != null && next >= 0 && next < nodes.Count ? nodes[next] : null;
                Reveal(fog, CellOf(grid, target));
            }

            fog.DeepestLayer = Math.Max(fog.DeepestLayer, Math.Min(node.Layer, grid.MapRows));
            return fog;
        }

        /// <summary>A Vista at (<paramref name="halfRow"/>, <paramref name="col"/>) was visited: its large ellipse is lifted. Returns how many cells.</summary>
        public static int RevealVista(RegionProgress progress, FogGrid grid, int stage, int halfRow, int col)
        {
            return RevealEllipse(progress.FogOf(stage), grid, halfRow, col, VistaRevealRows, VistaRevealCols);
        }

        /// <summary>Whether bit <paramref name="index"/> of hexadecimal bit set <paramref name="hex"/> is set (anything unreadable reads as unset).</summary>
        public static bool Get(string hex, int index)
        {
            int digit = index / 4;
            if (string.IsNullOrEmpty(hex) || index < 0 || digit >= hex.Length)
            {
                return false;
            }

            int value = HexValue(hex[digit]);
            return value >= 0 && (value & (1 << (index % 4))) != 0;
        }

        /// <summary><paramref name="hex"/> with bit <paramref name="index"/> set (grown with zeros as needed; unreadable digits read as 0).</summary>
        public static string Set(string hex, int index)
        {
            if (index < 0)
            {
                return hex ?? string.Empty;
            }

            int digit = index / 4;
            StringBuilder text = new StringBuilder(Math.Max(digit + 1, hex == null ? 0 : hex.Length));
            for (int i = 0; i < Math.Max(digit + 1, hex == null ? 0 : hex.Length); i++)
            {
                int value = hex != null && i < hex.Length ? Math.Max(0, HexValue(hex[i])) : 0;
                if (i == digit)
                {
                    value |= 1 << (index % 4);
                }

                text.Append("0123456789abcdef"[value]);
            }

            return text.ToString();
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9')
            {
                return c - '0';
            }

            if (c >= 'a' && c <= 'f')
            {
                return c - 'a' + 10;
            }

            return c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }

    /// <summary>
    /// A stage map's fog grid (see <see cref="MapFog"/>): <see cref="HalfRows"/> = 2 × the top row + 1,
    /// <see cref="Cols"/> = 2 × lanes + 1; cell index = half-row × cols + column.
    /// </summary>
    public readonly struct FogGrid : IEquatable<FogGrid>
    {
        public FogGrid(int mapRows, int lanes)
        {
            MapRows = Math.Max(1, mapRows);
            Lanes = Math.Max(1, lanes);
        }

        /// <summary>The top row's index (the map's layers − 1).</summary>
        public int MapRows { get; }

        public int Lanes { get; }

        public int HalfRows
        {
            get { return (2 * MapRows) + 1; }
        }

        public int Cols
        {
            get { return (2 * Lanes) + 1; }
        }

        public int CellCount
        {
            get { return HalfRows * Cols; }
        }

        public int Index(int halfRow, int col)
        {
            return halfRow < 0 || halfRow >= HalfRows || col < 0 || col >= Cols ? -1 : (halfRow * Cols) + col;
        }

        public int RowOf(int cell)
        {
            return cell < 0 ? -1 : cell / Cols;
        }

        public int ColOf(int cell)
        {
            return cell < 0 ? -1 : cell % Cols;
        }

        /// <summary>A cell's centre across the map, 0 (left edge column) to 1 (right edge column).</summary>
        public float XOf(int col)
        {
            return Cols <= 1 ? 0.5f : col / (float)(Cols - 1);
        }

        public bool Equals(FogGrid other)
        {
            return MapRows == other.MapRows && Lanes == other.Lanes;
        }

        public override bool Equals(object obj)
        {
            return obj is FogGrid other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (MapRows * 397) ^ Lanes;
        }
    }
}
