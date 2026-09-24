using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// Structural checks for <see cref="EnemyLibraryData"/>: the rules every enemy must satisfy to
    /// fight at all. Ids are present, unique, lowercase snake_case and never a roster species id; the
    /// growth curve exists; numbers sit in their bands (crit and status resist are percents, move
    /// range at least 1); every enum name parses; and every kit can actually reach the player's
    /// beasts: at least one skill that walks the unit in (<see cref="SkillLibraryValidator.Approaches"/>),
    /// and for a <see cref="CombatStance.Ranged"/> enemy (which never walks into melee) one such
    /// skill with range 2 or more. Targeting <c>Random</c> is refused (battles never spend the rng on
    /// enemy targeting), and a skill <c>Element</c> is refused (every skill takes the unit's element).
    /// Balance is not checked here; that is the balance simulator's job.
    /// </summary>
    public static class EnemyLibraryValidator
    {
        /// <summary>Returns every problem found; an empty list means the library is importable.</summary>
        /// <param name="library">The enemy library.</param>
        /// <param name="roster">The beast roster, for the growth curve and the id-collision check. Null skips both.</param>
        public static List<string> Validate(EnemyLibraryData library, BeastRosterData roster)
        {
            List<string> errors = new List<string>();

            if (library == null)
            {
                errors.Add("Enemy library is null (the JSON did not parse).");
                return errors;
            }

            if (library.SchemaVersion != EnemyLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + library.SchemaVersion + "; this code reads version " + EnemyLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> speciesIds = new HashSet<string>(StringComparer.Ordinal);
            if (roster != null)
            {
                bool curveFound = false;
                foreach (GrowthCurveData curve in roster.GrowthCurves ?? new GrowthCurveData[0])
                {
                    curveFound |= curve != null && string.Equals(curve.CurveId, library.GrowthCurveId, StringComparison.Ordinal);
                }

                if (!curveFound)
                {
                    errors.Add("GrowthCurveId '" + library.GrowthCurveId + "' is not a growth curve in beast-roster.json.");
                }

                foreach (SpeciesData species in roster.Species ?? new SpeciesData[0])
                {
                    if (species != null && !string.IsNullOrEmpty(species.SpeciesId))
                    {
                        speciesIds.Add(species.SpeciesId);
                    }
                }
            }
            else if (string.IsNullOrEmpty(library.GrowthCurveId))
            {
                errors.Add("GrowthCurveId is empty.");
            }

            if (library.Enemies == null || library.Enemies.Length == 0)
            {
                errors.Add("No enemies defined.");
                return errors;
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < library.Enemies.Length; i++)
            {
                EnemyData enemy = library.Enemies[i];
                if (enemy == null)
                {
                    errors.Add("Enemy #" + i + " is null.");
                    continue;
                }

                string where = "Enemy '" + enemy.EnemyId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(enemy.EnemyId))
                {
                    errors.Add(where + ": EnemyId must be lowercase snake_case.");
                }
                else if (!ids.Add(enemy.EnemyId))
                {
                    errors.Add(where + ": duplicate EnemyId.");
                }
                else if (speciesIds.Contains(enemy.EnemyId))
                {
                    errors.Add(where + ": EnemyId is also a roster SpeciesId; enemy and beast ids must not collide.");
                }

                if (string.IsNullOrEmpty(enemy.DisplayName))
                {
                    errors.Add(where + ": DisplayName is empty.");
                }

                if (!(enemy.Threat > 0.0))
                {
                    errors.Add(where + ": Threat must be above 0.");
                }

                ValidateEnemy(enemy, where, errors);
            }

            return errors;
        }

        /// <summary>
        /// The per-enemy rules (stats, stance, footprint, status resist, kit), without the id and
        /// threat checks: shared with tooling that authors enemies outside the library (the balance
        /// simulator's legacy fixed encounters).
        /// </summary>
        public static void ValidateEnemy(EnemyData enemy, string where, List<string> errors)
        {
            // MoveRange >= 1: BattleTurnExecutor's partial approach lets any mobile unit close any
            // distance over several turns, but a unit with move 0 never leaves its deployment tiles,
            // and a whole encounter of them against a team that cannot reach them is a stalemate.
            if (enemy.BaseStats.Hp < 1 || enemy.BaseStats.MoveRange < 1)
            {
                errors.Add(where + ": BaseStats needs Hp >= 1 and MoveRange >= 1.");
            }

            if (enemy.BaseStats.CritChance < BeastRosterValidator.MinCritChance || enemy.BaseStats.CritChance > BeastRosterValidator.MaxCritChance)
            {
                errors.Add(where + ": BaseStats.CritChance must be between " + BeastRosterValidator.MinCritChance + " and " +
                           BeastRosterValidator.MaxCritChance + ".");
            }

            if (!BeastRosterValidator.TryParseStance(enemy.Stance, out CombatStance stance))
            {
                errors.Add(where + ": Stance '" + enemy.Stance + "' is not Vanguard, Ranged or Skirmisher.");
            }

            if (!TryParseFootprint(enemy.Footprint, out UnitFootprint _))
            {
                errors.Add(where + ": Footprint '" + enemy.Footprint + "' is not Single, Triangle or Hex7.");
            }

            if (enemy.StatusResist < 0 || enemy.StatusResist > 100)
            {
                errors.Add(where + ": StatusResist must be between 0 and 100.");
            }

            if (enemy.Skills == null || enemy.Skills.Length == 0)
            {
                errors.Add(where + ": no skills.");
                return;
            }

            bool approaches = false;
            bool reach = false;
            HashSet<string> skillIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < enemy.Skills.Length; i++)
            {
                SkillData skill = enemy.Skills[i];
                if (skill == null)
                {
                    errors.Add(where + " skill #" + i + " is null.");
                    continue;
                }

                string label = where + " skill '" + skill.SkillId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(skill.SkillId))
                {
                    errors.Add(label + ": SkillId must be lowercase snake_case.");
                }
                else if (!skillIds.Add(skill.SkillId))
                {
                    errors.Add(label + ": duplicate SkillId in this enemy's kit.");
                }

                bool shapeOk = SkillLibraryValidator.CheckEnum<SkillTargetShape>(skill.TargetShape, label, "TargetShape", errors);
                SkillLibraryValidator.CheckEnum<SkillTargetSide>(skill.TargetSide, label, "TargetSide", errors);
                bool criterionOk = SkillLibraryValidator.CheckEnum<SkillTargetingCriterion>(skill.TargetingCriterion, label, "TargetingCriterion", errors);
                SkillLibraryValidator.CheckEnum<SkillTargetingOrder>(skill.TargetingOrder, label, "TargetingOrder", errors);
                SkillLibraryValidator.CheckEnum<StatType>(skill.TargetingStat, label, "TargetingStat", errors);
                SkillLibraryValidator.CheckEnum<DamageCategory>(skill.Category, label, "Category", errors);

                if (!string.IsNullOrEmpty(skill.Element))
                {
                    errors.Add(label + ": Element must be empty; an enemy skill takes the unit's element.");
                }

                SkillLibraryValidator.CheckBand(skill.ResourceCost, 0, 1000, label, "ResourceCost", errors);
                SkillLibraryValidator.CheckBand(skill.Cooldown, 0, SkillLibraryValidator.MaxCooldown, label, "Cooldown", errors);
                SkillLibraryValidator.CheckBand(skill.InitialCooldown, -1, SkillLibraryValidator.MaxCooldown, label, "InitialCooldown", errors);
                SkillLibraryValidator.CheckBand(skill.MaxUsesPerBattle, 0, SkillLibraryValidator.MaxUses, label, "MaxUsesPerBattle", errors);

                SkillTargetShape shape = SkillLibraryValidator.ParseOr(skill.TargetShape, SkillTargetShape.SingleTarget);
                SkillLibraryValidator.CheckBand(skill.Range, SkillLibraryValidator.IsPositional(shape) ? 1 : 0, SkillLibraryValidator.MaxRange, label, "Range",
                                                errors);

                if (criterionOk && SkillLibraryValidator.ParseOr(skill.TargetingCriterion, SkillTargetingCriterion.Distance) == SkillTargetingCriterion.Random)
                {
                    errors.Add(label + ": TargetingCriterion Random is refused (battles never spend the rng on enemy targeting); pick a rule.");
                }

                SkillLibraryValidator.CheckEffects(skill.Effects, label, "Effects", false, errors);

                SkillTargetSide side = SkillLibraryValidator.ParseOr(skill.TargetSide, SkillTargetSide.Enemy);
                if (shapeOk && SkillLibraryValidator.Approaches(shape) && side == SkillTargetSide.Enemy)
                {
                    approaches = true;
                    reach |= skill.Range >= 2;
                }
            }

            // BattleTurnExecutor only walks a unit toward a focus picked by an approaching skill, so
            // an enemy without one would never leave its deployment tiles.
            if (!approaches)
            {
                errors.Add(where + ": needs at least one enemy-side SingleTarget or Line skill (the only shapes that move the unit).");
            }

            // A Ranged unit never walks in for a range-1 skill, so a Ranged enemy with nothing longer
            // would never leave its deployment tiles either.
            if (approaches && stance == CombatStance.Ranged && !reach)
            {
                errors.Add(where + ": a Ranged enemy needs a SingleTarget or Line skill with Range >= 2 (Ranged units never walk into melee).");
            }
        }

        /// <summary>
        /// Parses a <see cref="UnitFootprint"/> name exactly as written (names only). Missing or
        /// empty is <see cref="UnitFootprint.Single"/>.
        /// </summary>
        public static bool TryParseFootprint(string text, out UnitFootprint footprint)
        {
            footprint = UnitFootprint.Single;
            if (string.IsNullOrEmpty(text))
            {
                return true;
            }

            if (!Enum.IsDefined(typeof(UnitFootprint), text))
            {
                return false;
            }

            footprint = (UnitFootprint)Enum.Parse(typeof(UnitFootprint), text);
            return true;
        }
    }
}
