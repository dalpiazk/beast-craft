using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;

namespace BeastCraft.Economy
{
    /// <summary>
    /// Integrity and power-budget checks for <see cref="GearLibraryData"/>.
    /// <para>
    /// <strong>Structure:</strong> ids present, lowercase snake_case and unique across beast and
    /// avatar gear; slots name a <see cref="GearSlot"/> (beast) or <see cref="AvatarGearSlot"/>
    /// (avatar); rarity 0-2; minimum level 1-100; archetype <c>Striker</c>, <c>Bulwark</c> or
    /// <c>Swift</c>; at least one modifier, each a known <see cref="StatType"/> other than
    /// <c>MoveRange</c> (banned in v1), each stat once, flat and percent not both zero, no negative
    /// values; sources from <c>shop</c> / <c>drop</c> / <c>boss</c>, at least one, and an epic only
    /// from <c>boss</c>.
    /// </para>
    /// <para>
    /// <strong>Budget</strong> (with the roster's species): a piece's points are the sum over its
    /// modifiers of <c>Flat / ref + Pct</c>, where <c>ref</c> is the roster's mean of that stat at
    /// <c>MinimumLevel + </c><see cref="ReferenceLevelOffset"/> (the middle of its band), Speed
    /// weighted x<see cref="SpeedWeight"/> and each point of <c>CritChance</c> worth
    /// <see cref="CritPointValue"/>. Points must be within <see cref="Tolerance"/> of the rarity's
    /// budget (<see cref="Budget(int)"/>: common 5%, rare 8%, epic 12% in the first band) scaled by
    /// the band (<see cref="BandScale"/>): a level is worth less of a stat the higher it is, so
    /// the same share of a stat buys more levels late; the scale keeps a piece's worth in
    /// levels-equivalent about flat — three commons about +0.7 of a level, three rares +1.2, three
    /// epics +1.8, measured by the balance simulator's <c>--economy-probe</c>. And no piece may add more than
    /// <see cref="MaxAxisShare"/> to any species' stat at its minimum level (crit excepted: it is a
    /// percent, not a stat that grows). Avatar gear is budgeted against the same roster means. See
    /// the economy design doc, "Gear".
    /// </para>
    /// </summary>
    public static class GearLibraryValidator
    {
        /// <summary>A piece's budget is measured at its minimum level plus this (the middle of a 20-level band).</summary>
        public const int ReferenceLevelOffset = 10;

        /// <summary>Speed's weight in the points (a speed point buys turns, worth more than a point of any other stat).</summary>
        public const double SpeedWeight = 1.5;

        /// <summary>What one point of <c>CritChance</c> counts for.</summary>
        public const double CritPointValue = 0.01;

        /// <summary>How far a piece's points may stray from its rarity's budget, as a fraction of it.</summary>
        public const double Tolerance = 0.10;

        /// <summary>The most a piece may add to any species' stat at its minimum level.</summary>
        public const double MaxAxisShare = 0.25;

        /// <summary>The archetype names.</summary>
        public static readonly string[] Archetypes = { "Striker", "Bulwark", "Swift" };

        /// <summary>The exponent of <see cref="BandScale"/>, fitted to <c>--economy-probe</c> (three commons ~0.7 LE at levels 1, 50 and 100).</summary>
        public const double BandScaleExponent = 0.65;

        /// <summary>
        /// The band's budget scale: <c>(T(1 + offset) / T(MinimumLevel + offset)) ^ </c><see cref="BandScaleExponent"/>,
        /// with <c>T</c> the roster's mean HP + Attack + Defense + SpecialAttack + SpecialDefense +
        /// Speed at a level — 1 for the first band, about 0.7 / 0.56 / 0.47 / 0.41 for bands 21 / 41
        /// / 61 / 81.
        /// </summary>
        public static double BandScale(IReadOnlyList<CreatureSpeciesSO> species, int minimumLevel)
        {
            double first = MeanTotal(species, 1 + ReferenceLevelOffset);
            double here = MeanTotal(species, Math.Min(100, Math.Max(1, minimumLevel) + ReferenceLevelOffset));
            return here <= 0.0 ? 1.0 : Math.Pow(first / here, BandScaleExponent);
        }

        /// <summary>The budget of a piece of <paramref name="rarity"/> in the band of <paramref name="minimumLevel"/>: <see cref="Budget(int)"/> x <see cref="BandScale"/>.</summary>
        public static double Budget(int rarity, int minimumLevel, IReadOnlyList<CreatureSpeciesSO> species)
        {
            return Budget(rarity) * BandScale(species, minimumLevel);
        }

        private static double MeanTotal(IReadOnlyList<CreatureSpeciesSO> species, int level)
        {
            double total = 0.0;
            foreach (StatType stat in new[] { StatType.HP, StatType.Attack, StatType.Defense, StatType.SpecialAttack, StatType.SpecialDefense, StatType.Speed })
            {
                total += MeanStat(species, stat, level);
            }

            return total;
        }

        /// <summary>The first-band budget of <paramref name="rarity"/>: 0.05, 0.08, 0.12 (0 for an unknown rarity).</summary>
        public static double Budget(int rarity)
        {
            switch (rarity)
            {
                case 0:
                    return 0.05;
                case 1:
                    return 0.08;
                case 2:
                    return 0.12;
                default:
                    return 0.0;
            }
        }

        /// <summary>Structure only (no budget).</summary>
        public static List<string> Validate(GearLibraryData data)
        {
            return Validate(data, null);
        }

        /// <summary>Every problem found; empty = importable. With <paramref name="species"/> (the roster) the budget is checked too.</summary>
        public static List<string> Validate(GearLibraryData data, IReadOnlyList<CreatureSpeciesSO> species)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("The gear library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != GearLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + GearLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            ValidateList(data.BeastGear, "BeastGear", false, ids, species, errors);
            ValidateList(data.AvatarGear, "AvatarGear", true, ids, species, errors);
            return errors;
        }

        /// <summary>
        /// A piece's budget points (see the class remarks) against the roster <paramref name="species"/>.
        /// NaN when a reference stat is 0.
        /// </summary>
        public static double Points(GearItemData item, IReadOnlyList<CreatureSpeciesSO> species)
        {
            double points = 0.0;
            int level = Math.Min(100, Math.Max(1, item.MinimumLevel) + ReferenceLevelOffset);
            foreach (GearModifierData m in item.Modifiers ?? new GearModifierData[0])
            {
                if (m == null || !Enum.TryParse(m.Stat, false, out StatType stat))
                {
                    continue;
                }

                double value;
                if (stat == StatType.CritChance)
                {
                    value = (m.Flat * CritPointValue) + m.Pct;
                }
                else
                {
                    double reference = MeanStat(species, stat, level);
                    value = (m.Flat / reference) + m.Pct;
                }

                points += stat == StatType.Speed ? value * SpeedWeight : value;
            }

            return points;
        }

        /// <summary>The roster's mean of <paramref name="stat"/> at <paramref name="level"/>.</summary>
        public static double MeanStat(IReadOnlyList<CreatureSpeciesSO> species, StatType stat, int level)
        {
            double sum = 0.0;
            int count = 0;
            foreach (CreatureSpeciesSO s in species)
            {
                if (s != null)
                {
                    sum += s.GetStatAtLevel(stat, level);
                    count++;
                }
            }

            return count == 0 ? 0.0 : sum / count;
        }

        private static void ValidateList(GearItemData[] items, string list, bool avatar, HashSet<string> ids, IReadOnlyList<CreatureSpeciesSO> species, List<string> errors)
        {
            if (items == null)
            {
                errors.Add(list + " is null.");
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                GearItemData item = items[i];
                string at = list + "[" + i + "]";
                if (item == null)
                {
                    errors.Add(at + " is null.");
                    continue;
                }

                at += " '" + item.GearId + "'";
                if (!Progression.DropTableValidator.IsSnakeCase(item.GearId))
                {
                    errors.Add(at + ": GearId is not a lowercase snake_case id.");
                }
                else if (!ids.Add(item.GearId))
                {
                    errors.Add(at + ": GearId is used twice.");
                }

                if (string.IsNullOrEmpty(item.DisplayName))
                {
                    errors.Add(at + ": no DisplayName.");
                }

                bool slotOk = avatar
                    ? Enum.TryParse(item.Slot, false, out AvatarGearSlot avatarSlot) && Enum.IsDefined(typeof(AvatarGearSlot), avatarSlot) && item.Slot == avatarSlot.ToString()
                    : Enum.TryParse(item.Slot, false, out GearSlot beastSlot) && Enum.IsDefined(typeof(GearSlot), beastSlot) && item.Slot == beastSlot.ToString();
                if (!slotOk)
                {
                    errors.Add(at + ": Slot '" + item.Slot + "' is not a " + (avatar ? "AvatarGearSlot" : "GearSlot") + " name.");
                }

                if (item.Rarity < 0 || item.Rarity > Progression.DropTableValidator.MaxGearRarity)
                {
                    errors.Add(at + ": Rarity is " + item.Rarity + "; it must be 0 to " + Progression.DropTableValidator.MaxGearRarity + ".");
                }

                if (item.MinimumLevel < 1 || item.MinimumLevel > 100)
                {
                    errors.Add(at + ": MinimumLevel is " + item.MinimumLevel + "; it must be 1 to 100.");
                }

                if (Array.IndexOf(Archetypes, item.Archetype) < 0)
                {
                    errors.Add(at + ": Archetype '" + item.Archetype + "' is not one of " + string.Join(", ", Archetypes) + ".");
                }

                bool modifiersOk = ValidateModifiers(item, at, errors);
                ValidateSources(item, at, errors);
                if (species != null && species.Count > 0 && modifiersOk && item.Rarity >= 0 && item.Rarity <= 2 && item.MinimumLevel >= 1 && item.MinimumLevel <= 100)
                {
                    ValidateBudget(item, at, species, errors);
                }
            }
        }

        private static bool ValidateModifiers(GearItemData item, string at, List<string> errors)
        {
            GearModifierData[] modifiers = item.Modifiers ?? new GearModifierData[0];
            if (modifiers.Length == 0)
            {
                errors.Add(at + ": no Modifiers.");
                return false;
            }

            bool ok = true;
            HashSet<string> stats = new HashSet<string>(StringComparer.Ordinal);
            foreach (GearModifierData m in modifiers)
            {
                if (m == null || !Enum.TryParse(m.Stat, false, out StatType stat) || !Enum.IsDefined(typeof(StatType), stat) || m.Stat != stat.ToString())
                {
                    errors.Add(at + ": a modifier's Stat '" + (m == null ? null : m.Stat) + "' is not a StatType name.");
                    ok = false;
                    continue;
                }

                if (stat == StatType.MoveRange)
                {
                    errors.Add(at + ": MoveRange modifiers are not allowed in v1.");
                    ok = false;
                }

                if (!stats.Add(m.Stat))
                {
                    errors.Add(at + ": " + m.Stat + " is modified twice.");
                    ok = false;
                }

                if (m.Flat < 0 || m.Pct < 0f || (m.Flat == 0 && m.Pct == 0f))
                {
                    errors.Add(at + ": " + m.Stat + " must add something (Flat and Pct at least 0, not both 0).");
                    ok = false;
                }
            }

            return ok;
        }

        private static void ValidateSources(GearItemData item, string at, List<string> errors)
        {
            string[] sources = item.Sources ?? new string[0];
            if (sources.Length == 0)
            {
                errors.Add(at + ": no Sources (it could never be had).");
            }

            foreach (string source in sources)
            {
                if (source != GearLibrary.SourceShop && source != GearLibrary.SourceDrop && source != GearLibrary.SourceBoss)
                {
                    errors.Add(at + ": Source '" + source + "' is not shop, drop or boss.");
                }
            }

            if (item.Rarity == 2 && (sources.Length != 1 || sources[0] != GearLibrary.SourceBoss))
            {
                errors.Add(at + ": an epic comes from bosses only (Sources must be exactly [\"boss\"]).");
            }
        }

        private static void ValidateBudget(GearItemData item, string at, IReadOnlyList<CreatureSpeciesSO> species, List<string> errors)
        {
            double budget = Budget(item.Rarity, item.MinimumLevel, species);
            double points = Points(item, species);
            if (double.IsNaN(points) || points < budget * (1.0 - Tolerance) || points > budget * (1.0 + Tolerance))
            {
                errors.Add(at + ": " + (points * 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "% of budget points; a rarity-" + item.Rarity +
                           " piece of its band must be " + (budget * 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "% +/- " + (Tolerance * 100.0) + "% of that.");
            }

            foreach (GearModifierData m in item.Modifiers)
            {
                Enum.TryParse(m.Stat, false, out StatType stat);
                if (stat == StatType.CritChance)
                {
                    continue;
                }

                foreach (CreatureSpeciesSO s in species)
                {
                    int value = s == null ? 0 : s.GetStatAtLevel(stat, item.MinimumLevel);
                    double share = value <= 0 ? double.PositiveInfinity : (m.Flat / (double)value) + m.Pct;
                    if (share > MaxAxisShare)
                    {
                        errors.Add(at + ": adds " + (share * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "% to " + s.SpeciesId + "'s " + m.Stat +
                                   " at level " + item.MinimumLevel + " (at most " + (MaxAxisShare * 100.0) + "%).");
                        break;
                    }
                }
            }
        }
    }
}
