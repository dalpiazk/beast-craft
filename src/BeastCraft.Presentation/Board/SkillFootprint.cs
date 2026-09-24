using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Presentation.Board
{
    /// <summary>
    /// A skill's range and area as hex cells relative to its caster (anchored on tile (0, 0)), for
    /// a diagram on a skill card: a pure function of the Core skill data
    /// (<see cref="SkillSO.TargetShape"/>, <see cref="SkillSO.Range"/>,
    /// <see cref="SkillSO.TargetSide"/>) and the caster's <see cref="UnitFootprint"/>, following
    /// the battle's own targeting rules (<see cref="SkillTargetResolver"/>): every shape's origin is
    /// the caster's own tile(s), ranges count from a large caster's nearest tile, a Cross radiates
    /// and an AreaBurst spreads from each of a large caster's tiles, and a Line runs from the
    /// caster's tile nearest the target it picks. Unlike a battle it has no board edge, so a
    /// diagram shows the whole shape.
    /// </summary>
    public sealed class SkillFootprint
    {
        /// <summary>The direction a diagram aims a picked target: up and to the right, toward the enemy's side of the board.</summary>
        public static readonly HexCoordinate DefaultAim = new HexCoordinate(1, -1);

        private SkillFootprint(SkillTargetShape shape, SkillTargetSide side, int range, List<HexCoordinate> caster, List<HexCoordinate> reach,
                               List<HexCoordinate> area, HexCoordinate? focus)
        {
            Shape = shape;
            Side = side;
            Range = range;
            Caster = caster;
            Reach = reach;
            Area = area;
            Focus = focus;
        }

        public SkillTargetShape Shape { get; }

        /// <summary>Whose units it lands on.</summary>
        public SkillTargetSide Side { get; }

        public int Range { get; }

        /// <summary>The tiles the caster stands on.</summary>
        public IReadOnlyList<HexCoordinate> Caster { get; }

        /// <summary>
        /// Where the skill may pick its target (SingleTarget and Line: every tile within Range of the
        /// caster, the caster's own excluded); for a fixed shape, its area; empty for Self and the
        /// All shapes.
        /// </summary>
        public IReadOnlyList<HexCoordinate> Reach { get; }

        /// <summary>
        /// The tiles it hits when it fires: for a picked shape, as aimed at <see cref="Focus"/>; for a
        /// Cross or AreaBurst, the whole shape; the caster for Self; empty for the All shapes
        /// (<see cref="IsGlobal"/>).
        /// </summary>
        public IReadOnlyList<HexCoordinate> Area { get; }

        /// <summary>The example target a picked shape (SingleTarget, Line) is aimed at, or null.</summary>
        public HexCoordinate? Focus { get; }

        /// <summary>AllEnemies / AllAllies: every unit of its side, wherever it stands.</summary>
        public bool IsGlobal
        {
            get { return Shape == SkillTargetShape.AllEnemies || Shape == SkillTargetShape.AllAllies; }
        }

        /// <summary>The hex radius around the caster's anchor a diagram needs to show everything (at least 2).</summary>
        public int Extent
        {
            get
            {
                int extent = 2;
                foreach (IReadOnlyList<HexCoordinate> tiles in new[] { Caster, Reach, Area })
                {
                    foreach (HexCoordinate tile in tiles)
                    {
                        extent = Math.Max(extent, tile.Distance(HexCoordinate.Zero) + 1);
                    }
                }

                return extent;
            }
        }

        /// <summary>
        /// The footprint of <paramref name="skill"/> cast by a unit of
        /// <paramref name="casterFootprint"/> anchored on (0, 0), a picked target aimed along
        /// <paramref name="aim"/> (an axial direction; default <see cref="DefaultAim"/>).
        /// </summary>
        public static SkillFootprint Of(SkillSO skill, UnitFootprint casterFootprint = UnitFootprint.Single, HexCoordinate? aim = null)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            return Of(skill.TargetShape, skill.Range, skill.TargetSide, casterFootprint, aim);
        }

        /// <summary>The footprint of a shape, range and side (see <see cref="Of(SkillSO, UnitFootprint, HexCoordinate?)"/>).</summary>
        public static SkillFootprint Of(SkillTargetShape shape, int range, SkillTargetSide side, UnitFootprint casterFootprint = UnitFootprint.Single,
                                        HexCoordinate? aim = null)
        {
            int r = Math.Max(0, range);
            HexCoordinate direction = aim ?? DefaultAim;
            List<HexCoordinate> caster = Footprints.Tiles(HexCoordinate.Zero, casterFootprint);
            HashSet<HexCoordinate> own = new HashSet<HexCoordinate>(caster);
            List<HexCoordinate> reach = new List<HexCoordinate>();
            List<HexCoordinate> area = new List<HexCoordinate>();
            HexCoordinate? focus = null;

            switch (shape)
            {
                case SkillTargetShape.Self:
                    area.AddRange(caster);
                    break;

                case SkillTargetShape.AllEnemies:
                case SkillTargetShape.AllAllies:
                    break;

                case SkillTargetShape.SingleTarget:
                case SkillTargetShape.Line:
                    {
                        // A target may stand on any tile within Range of the caster's nearest tile.
                        foreach (HexCoordinate tile in Disc(HexCoordinate.Zero, r + Grow(casterFootprint)))
                        {
                            if (!own.Contains(tile) && FootprintMath.DistanceTo(tile, HexCoordinate.Zero, casterFootprint) <= r)
                            {
                                reach.Add(tile);
                            }
                        }

                        // The example: the farthest reachable tile straight along the aim.
                        HexCoordinate target = HexCoordinate.Zero;
                        for (int step = 1; step <= r + Grow(casterFootprint); step++)
                        {
                            HexCoordinate tile = new HexCoordinate(direction.Q * step, direction.R * step);
                            if (!own.Contains(tile) && FootprintMath.DistanceTo(tile, HexCoordinate.Zero, casterFootprint) <= r)
                            {
                                target = tile;
                            }
                        }

                        if (r == 0 || target == HexCoordinate.Zero)
                        {
                            break;
                        }

                        focus = target;
                        if (shape == SkillTargetShape.SingleTarget)
                        {
                            area.Add(target);
                            break;
                        }

                        // As SkillTargetResolver's Line: from the caster's tile nearest the focus, along the
                        // axis that best reaches it, Range tiles, the caster's own excluded.
                        HexCoordinate origin = FootprintMath.NearestTile(HexCoordinate.Zero, casterFootprint, target, UnitFootprint.Single);
                        HexCoordinate axis = SnapToAxis(origin, target, r);
                        AddArm(area, own, origin, axis, r);
                        break;
                    }

                case SkillTargetShape.Cross:
                    {
                        HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>();
                        foreach (HexCoordinate start in caster)
                        {
                            foreach (HexCoordinate axis in HexCoordinate.AxialDirections)
                            {
                                for (int step = 1; step <= r; step++)
                                {
                                    HexCoordinate tile = start + new HexCoordinate(axis.Q * step, axis.R * step);
                                    if (!own.Contains(tile) && tiles.Add(tile))
                                    {
                                        area.Add(tile);
                                    }
                                }
                            }
                        }

                        reach.AddRange(area);
                        break;
                    }

                case SkillTargetShape.AreaBurst:
                    {
                        HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>();
                        foreach (HexCoordinate start in caster)
                        {
                            foreach (HexCoordinate tile in Disc(start, r))
                            {
                                if (tiles.Add(tile))
                                {
                                    area.Add(tile);
                                }
                            }
                        }

                        reach.AddRange(area);
                        break;
                    }
            }

            // The All shapes and Self name their side by their shape (the battle ignores TargetSide for them).
            if (shape == SkillTargetShape.AllAllies || shape == SkillTargetShape.Self)
            {
                side = SkillTargetSide.Ally;
            }
            else if (shape == SkillTargetShape.AllEnemies)
            {
                side = SkillTargetSide.Enemy;
            }

            Sort(reach);
            Sort(area);
            return new SkillFootprint(shape, side, r, caster, reach, area, focus);
        }

        /// <summary>Every tile within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public static List<HexCoordinate> Disc(HexCoordinate center, int radius)
        {
            List<HexCoordinate> tiles = new List<HexCoordinate>();
            for (int q = -radius; q <= radius; q++)
            {
                for (int r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
                {
                    tiles.Add(center + new HexCoordinate(q, r));
                }
            }

            return tiles;
        }

        /// <summary>How far a footprint's tiles reach past its anchor.</summary>
        private static int Grow(UnitFootprint footprint)
        {
            int grow = 0;
            foreach (HexCoordinate offset in Footprints.Offsets(footprint))
            {
                grow = Math.Max(grow, offset.Distance(HexCoordinate.Zero));
            }

            return grow;
        }

        private static void AddArm(List<HexCoordinate> area, HashSet<HexCoordinate> own, HexCoordinate origin, HexCoordinate axis, int range)
        {
            for (int step = 1; step <= range; step++)
            {
                HexCoordinate tile = origin + new HexCoordinate(axis.Q * step, axis.R * step);
                if (!own.Contains(tile) && !area.Contains(tile))
                {
                    area.Add(tile);
                }
            }
        }

        /// <summary>The axial direction whose Range-step projection lands nearest <paramref name="focus"/> (SkillTargetResolver's rule).</summary>
        private static HexCoordinate SnapToAxis(HexCoordinate origin, HexCoordinate focus, int range)
        {
            HexCoordinate best = HexCoordinate.AxialDirections[0];
            int bestDistance = int.MaxValue;
            foreach (HexCoordinate axis in HexCoordinate.AxialDirections)
            {
                int distance = (origin + new HexCoordinate(axis.Q * range, axis.R * range)).Distance(focus);
                if (distance < bestDistance)
                {
                    best = axis;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static void Sort(List<HexCoordinate> tiles)
        {
            tiles.Sort((a, b) => a.R != b.R ? a.R.CompareTo(b.R) : a.Q.CompareTo(b.Q));
        }
    }
}
