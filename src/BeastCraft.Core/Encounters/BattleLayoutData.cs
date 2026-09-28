using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Progression;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Encounters/battle-layouts.json</c>: the battlefield
    /// obstacles (rocks, rubble, a fallen bough) painted on each battle backdrop. One entry per
    /// (RegionId, ArtKey, Arena): a painted backdrop (its ArtKey is a sprite in the art manifest,
    /// listed in <c>battle-art.json</c>) and the hexes its obstacles stand on. Unlike the backdrop
    /// art this is battle data: an obstacle hex blocks movement and standing (nothing walks onto or
    /// through it, nothing is deployed or knocked onto it), and nothing else — skills pass over it
    /// (no line of sight anywhere). Checked by <see cref="ObstacleLayoutValidator"/>; picked per
    /// battle by <see cref="BattleLayouts.Pick"/>. A region and arena with no entry has no obstacles.
    /// Public fields, JSON keys are the field names (read with <c>FieldJson</c>).
    /// </summary>
    [Serializable]
    public class BattleLayoutData
    {
        /// <summary>The file's path relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Encounters/battle-layouts.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>Every layout, at most one per (RegionId, ArtKey, Arena).</summary>
        public BattleLayoutEntryData[] Layouts = new BattleLayoutEntryData[0];
    }

    /// <summary>One backdrop's obstacles on one arena size.</summary>
    [Serializable]
    public class BattleLayoutEntryData
    {
        /// <summary>A <c>regions.json</c> RegionId.</summary>
        public string RegionId;

        /// <summary>The painted backdrop the obstacles are painted on: a sprite's ArtKey (<c>backdrop/&lt;region&gt;/&lt;id&gt;/&lt;arena&gt;</c>).</summary>
        public string ArtKey;

        /// <summary>An <see cref="ArenaSize"/> name: <c>Small</c>, <c>Medium</c> or <c>Large</c>.</summary>
        public string Arena;

        /// <summary>The obstacle hexes, in the axial coordinates <see cref="HexGrid"/> uses (<c>R</c> &lt; 0 the enemy side).</summary>
        public HexCellData[] Cells = new HexCellData[0];

        /// <summary>The cells as coordinates.</summary>
        public List<HexCoordinate> Coordinates()
        {
            List<HexCoordinate> cells = new List<HexCoordinate>();
            foreach (HexCellData cell in Cells ?? new HexCellData[0])
            {
                if (cell != null)
                {
                    cells.Add(new HexCoordinate(cell.Q, cell.R));
                }
            }

            return cells;
        }
    }

    /// <summary>An axial hex coordinate in data.</summary>
    [Serializable]
    public class HexCellData
    {
        public int Q;

        public int R;
    }

    /// <summary>
    /// Which layout a battle fights on, and putting it on the board. The pick is a pure function of
    /// the battle's region, arena and seed, on its own stream of the seed
    /// (<see cref="LootRoller.DeriveSeed"/>, <see cref="ObstacleStream"/>): the battle, the balance
    /// simulator and the viewer (which draws the same layout's backdrop) all agree, and the pick never
    /// touches the battle's own random numbers.
    /// </summary>
    public static class BattleLayouts
    {
        /// <summary>
        /// The seed stream the layout is picked on (<c>PostBattleAward</c>'s reward streams are 0-4;
        /// the consumables' 2 is also drawn at battle start).
        /// </summary>
        public const int ObstacleStream = 5;

        /// <summary>The layouts of <paramref name="regionId"/>'s <paramref name="arena"/>, in file order (empty when none).</summary>
        public static List<BattleLayoutEntryData> Candidates(BattleLayoutData data, string regionId, ArenaSize arena)
        {
            List<BattleLayoutEntryData> found = new List<BattleLayoutEntryData>();
            if (data == null || data.Layouts == null || string.IsNullOrEmpty(regionId))
            {
                return found;
            }

            string name = arena.ToString();
            foreach (BattleLayoutEntryData entry in data.Layouts)
            {
                if (entry != null && string.Equals(entry.RegionId, regionId, StringComparison.Ordinal) && string.Equals(entry.Arena, name, StringComparison.Ordinal))
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        /// <summary>
        /// The layout a battle of <paramref name="seed"/> in <paramref name="regionId"/> on
        /// <paramref name="arena"/> fights on: candidate <c>DeriveSeed(seed, ObstacleStream) mod n</c>
        /// of <see cref="Candidates"/>; null when the region and arena have none (no obstacles).
        /// </summary>
        public static BattleLayoutEntryData Pick(BattleLayoutData data, string regionId, ArenaSize arena, int seed)
        {
            List<BattleLayoutEntryData> candidates = Candidates(data, regionId, arena);
            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[LootRoller.DeriveSeed(seed, ObstacleStream) % candidates.Count];
        }

        /// <summary>Blocks every cell of <paramref name="layout"/> on <paramref name="grid"/> (null: nothing). Returns how many cells were blocked.</summary>
        public static int Apply(HexGrid grid, BattleLayoutEntryData layout)
        {
            int blocked = 0;
            if (grid == null || layout == null)
            {
                return blocked;
            }

            foreach (HexCoordinate cell in layout.Coordinates())
            {
                if (grid.SetBlocked(cell, true))
                {
                    blocked++;
                }
            }

            return blocked;
        }
    }
}
