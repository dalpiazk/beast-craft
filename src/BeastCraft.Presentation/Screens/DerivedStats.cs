using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Encounters;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One stat's line on the beast detail screen: the species base at its level, what gear adds, the total.</summary>
    public sealed class StatLine
    {
        public StatType Stat;
        public string Label;

        /// <summary>The species' stat at the beast's level (<see cref="StatCalculator.GetBaseStatsAtLevel"/>).</summary>
        public int Base;

        /// <summary>What the worn gear adds (<see cref="Total"/> minus <see cref="Base"/>).</summary>
        public int Gear;

        /// <summary>The stat the beast fights with (<see cref="StatCalculator.ComputeStats(CreatureSpeciesSO, int, IEnumerable{GearSO})"/>).</summary>
        public int Total;
    }

    /// <summary>One unit's action rate under the ATB gauge: its fill per tick and its turns per 100 ticks.</summary>
    public sealed class TurnRateView
    {
        public string Name;
        public int Speed;

        /// <summary>Gauge points a tick (<see cref="TurnManager.FillRateForSpeed"/>: <c>round(100 * sqrt(Speed))</c>).</summary>
        public int FillRate;

        /// <summary>Turns per 100 gauge ticks (<see cref="DerivedStats.TurnsPer100Ticks"/>).</summary>
        public double TurnsPer100Ticks;

        /// <summary>Turns relative to the beast the screen is about (1 = as often).</summary>
        public double Relative;

        /// <summary>Whether this is the beast the screen is about.</summary>
        public bool IsSelf;

        /// <summary>Whether this is the level-matched average enemy rather than a beast.</summary>
        public bool IsEnemy;
    }

    /// <summary>One element against the beast: its hits' multiplier on that element (dealt) and that element's hits on it (taken).</summary>
    public sealed class ElementMatchView
    {
        public Element Element;

        /// <summary>The beast's attacking element against a defender of <see cref="Element"/> (<see cref="ElementChart.GetMultiplier(Element, Element)"/>).</summary>
        public float Dealt;

        /// <summary>A hit of <see cref="Element"/> against the beast's elements (<see cref="ElementChart.GetMultiplier(Element, IReadOnlyList{Element})"/>).</summary>
        public float Taken;
    }

    /// <summary>The level-gap modifier at one gap: what the beast deals and takes against an enemy that many levels above it.</summary>
    public sealed class LevelGapPoint
    {
        /// <summary>The enemy's level minus the beast's (negative: the enemy is lower).</summary>
        public int Gap;

        public int EnemyLevel;

        /// <summary>The multiplier on the beast's hits (<see cref="DamageFormula.GetLevelMultiplier"/>(beast, enemy)).</summary>
        public double Dealt;

        /// <summary>The multiplier on the enemy's hits on the beast (<see cref="DamageFormula.GetLevelMultiplier"/>(enemy, beast)).</summary>
        public double Taken;
    }

    /// <summary>
    /// The derived numbers the Theorycrafter asked for, each read straight off the Core rule it
    /// describes (never re-derived): the ATB fill (<see cref="TurnManager"/>), the element chart
    /// (<see cref="ElementChart"/>), crits and the level-gap modifier (<see cref="DamageFormula"/>).
    /// Presentation only; nothing here changes a battle.
    /// </summary>
    public static class DerivedStats
    {
        /// <summary>The level gaps the curve shows either side of the beast's level.</summary>
        public const int LevelGapSpan = 5;

        /// <summary>
        /// Turns a unit of <paramref name="speed"/> takes per 100 gauge ticks: each tick adds
        /// <see cref="TurnManager.FillRateForSpeed"/> and a turn costs
        /// <see cref="TurnManager.ActionThreshold"/>, so 100 ticks give <c>100 * fill / threshold</c>
        /// turns (1.0 at Speed 100: 100 ticks are one time unit).
        /// </summary>
        public static double TurnsPer100Ticks(int speed)
        {
            return TurnManager.FillRateForSpeed(speed) * 100.0 / TurnManager.ActionThreshold;
        }

        /// <summary>
        /// The level-matched average enemy's turns per 100 ticks: the mean of
        /// <see cref="TurnsPer100Ticks"/> over every enemy type in the library at
        /// <paramref name="level"/> (Speed is on the growth curve and never scaled by the difficulty
        /// multiplier: <see cref="EnemyScaling"/>). 0 when there are no enemies.
        /// </summary>
        public static double AverageEnemyTurns(EnemyCatalog enemies, int level, out int averageSpeed)
        {
            averageSpeed = 0;
            if (enemies == null || enemies.Enemies == null || enemies.Enemies.Count == 0)
            {
                return 0.0;
            }

            double turns = 0.0;
            long speed = 0;
            int count = 0;
            foreach (EnemyData enemy in enemies.Enemies)
            {
                CreatureSpeciesSO species = enemy == null ? null : enemies.Species(enemy.EnemyId, Element.None);
                if (species == null)
                {
                    continue;
                }

                int s = species.GetStatAtLevel(StatType.Speed, level);
                speed += s;
                turns += TurnsPer100Ticks(s);
                count++;
            }

            if (count == 0)
            {
                return 0.0;
            }

            averageSpeed = (int)Math.Round((double)speed / count, MidpointRounding.AwayFromZero);
            return turns / count;
        }

        /// <summary>
        /// How <paramref name="attack"/> fares against each of the ten elements, and each of them
        /// against <paramref name="defenders"/> (the beast's own elements).
        /// </summary>
        public static List<ElementMatchView> ElementMatchups(Element attack, IReadOnlyList<Element> defenders)
        {
            List<ElementMatchView> rows = new List<ElementMatchView>();
            foreach (Element element in ElementChartViewModel.Elements)
            {
                rows.Add(new ElementMatchView
                {
                    Element = element,
                    Dealt = ElementChart.GetMultiplier(attack, element),
                    Taken = ElementChart.GetMultiplier(element, defenders)
                });
            }

            return rows;
        }

        /// <summary>The level-gap multipliers for enemies from <see cref="LevelGapSpan"/> below to as many above <paramref name="level"/> (levels kept to 1-100).</summary>
        public static List<LevelGapPoint> LevelGapCurve(int level, int span = LevelGapSpan)
        {
            List<LevelGapPoint> points = new List<LevelGapPoint>();
            for (int gap = -span; gap <= span; gap++)
            {
                int enemy = level + gap;
                if (enemy < 1 || enemy > 100)
                {
                    continue;
                }

                points.Add(new LevelGapPoint { Gap = gap, EnemyLevel = enemy, Dealt = DamageFormula.GetLevelMultiplier(level, enemy), Taken = DamageFormula.GetLevelMultiplier(enemy, level) });
            }

            return points;
        }

        /// <summary>The crit chance actually rolled against (<see cref="DamageFormula.ClampCritChance"/>).</summary>
        public static int CritChance(int statCritChance)
        {
            return DamageFormula.ClampCritChance(statCritChance);
        }

        /// <summary>What a crit multiplies a hit by (<see cref="DamageFormula.GetCritMultiplier"/>, no reduction exists yet).</summary>
        public static float CritMultiplier
        {
            get { return DamageFormula.GetCritMultiplier(0f); }
        }

        /// <summary>A hit's expected crit factor: <c>1 + chance / 100 * (multiplier - 1)</c>.</summary>
        public static double ExpectedCritFactor(int statCritChance)
        {
            return 1.0 + (CritChance(statCritChance) / 100.0 * (CritMultiplier - 1.0));
        }

        /// <summary>A multiplier as the screens print it: "x2", "x1.25", "x0.5", "x1.07".</summary>
        public static string Times(double multiplier)
        {
            return "x" + multiplier.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>A signed percent change: "+12%", "-8%", "0%".</summary>
        public static string Percent(double multiplier)
        {
            double percent = Math.Round((multiplier - 1.0) * 100.0, 1, MidpointRounding.AwayFromZero);
            return (percent > 0 ? "+" : string.Empty) + percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A stat's short label: HP, Atk, Def, SpA, SpD, Speed, Move, Crit.</summary>
        public static string ShortName(StatType stat)
        {
            switch (stat)
            {
                case StatType.Attack:
                    return "Atk";
                case StatType.Defense:
                    return "Def";
                case StatType.SpecialAttack:
                    return "SpA";
                case StatType.SpecialDefense:
                    return "SpD";
                case StatType.MoveRange:
                    return "Move";
                case StatType.CritChance:
                    return "Crit";
                default:
                    return stat.ToString();
            }
        }
    }
}
