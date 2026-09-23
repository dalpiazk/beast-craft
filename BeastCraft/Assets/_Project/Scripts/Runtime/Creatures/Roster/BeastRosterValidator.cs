using System;
using System.Collections.Generic;

namespace BeastCraft.Creatures.Roster
{
    /// <summary>
    /// Structural integrity checks for <see cref="BeastRosterData"/>: the rules every roster file
    /// must satisfy to import at all (ids present, unique and well-formed, elements that parse,
    /// curve references that resolve, curves that start above 0 and end at 1, stats that are
    /// usable, a crit chance that is a percent, stances that parse). Balance guidelines — stat budgets, move-range bands, one beast per element — are
    /// deliberately NOT here; they are tests over the current starter roster, and the balance
    /// simulator is free to move them.
    /// </summary>
    public static class BeastRosterValidator
    {
        /// <summary>The least <see cref="StatBlock.CritChance"/> a species may author: 0, never crits.</summary>
        public const int MinCritChance = 0;

        /// <summary>The most <see cref="StatBlock.CritChance"/> a species may author: 100, always crits.</summary>
        public const int MaxCritChance = 100;

        /// <summary>Returns every problem found; an empty list means the roster is importable.</summary>
        public static List<string> Validate(BeastRosterData roster)
        {
            List<string> errors = new List<string>();

            if (roster == null)
            {
                errors.Add("Roster is null (the JSON did not parse).");
                return errors;
            }

            HashSet<string> curveIds = ValidateCurves(roster.GrowthCurves, errors);
            ValidateSpecies(roster.Species, curveIds, errors);
            return errors;
        }

        /// <summary>
        /// Parses an element name exactly as written (case-sensitive, names only — a numeric string
        /// such as <c>"3"</c> is rejected, since the JSON is meant to be read by people).
        /// </summary>
        public static bool TryParseElement(string name, out Element element)
        {
            element = Element.None;

            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(Element), name))
            {
                return false;
            }

            element = (Element)Enum.Parse(typeof(Element), name);
            return true;
        }

        /// <summary>
        /// Parses a species' <see cref="SpeciesData.Stance"/>: a <see cref="CombatStance"/> member
        /// name exactly as written (case-sensitive, names only, like
        /// <see cref="TryParseElement"/>). A null or empty string is the default,
        /// <see cref="CombatStance.Vanguard"/>, and parses successfully.
        /// </summary>
        public static bool TryParseStance(string name, out CombatStance stance)
        {
            stance = CombatStance.Vanguard;

            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            if (!Enum.IsDefined(typeof(CombatStance), name))
            {
                return false;
            }

            stance = (CombatStance)Enum.Parse(typeof(CombatStance), name);
            return true;
        }

        /// <summary>
        /// True for a lowercase snake_case id: starts with a letter, then letters, digits and
        /// single underscores, not ending in an underscore.
        /// </summary>
        public static bool IsSnakeCaseId(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] < 'a' || id[0] > 'z' || id[id.Length - 1] == '_')
            {
                return false;
            }

            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || (c == '_' && id[i - 1] != '_');
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The curve's scale at a level, evaluated by the same code <see cref="GrowthRateCurve"/> uses
        /// (<see cref="GrowthRateCurve.EvaluateScale"/>), without needing a Unity asset.
        /// </summary>
        public static float ScaleAtLevel(GrowthCurveData curve, int level)
        {
            return GrowthRateCurve.EvaluateScale(curve.ToAnimationCurve(), curve.MaxLevel, level);
        }

        private static HashSet<string> ValidateCurves(GrowthCurveData[] curves, List<string> errors)
        {
            HashSet<string> ids = new HashSet<string>();

            if (curves == null || curves.Length == 0)
            {
                errors.Add("No growth curves defined.");
                return ids;
            }

            for (int i = 0; i < curves.Length; i++)
            {
                GrowthCurveData curve = curves[i];
                if (curve == null)
                {
                    errors.Add("Growth curve #" + i + " is null.");
                    continue;
                }

                string label = "Growth curve '" + curve.CurveId + "'";

                if (!IsSnakeCaseId(curve.CurveId))
                {
                    errors.Add(label + ": CurveId must be lowercase snake_case.");
                }
                else if (!ids.Add(curve.CurveId))
                {
                    errors.Add(label + ": duplicate CurveId.");
                }

                if (curve.MaxLevel < 2)
                {
                    errors.Add(label + ": MaxLevel must be at least 2.");
                }

                GrowthKeyData[] keys = curve.Keys;
                if (keys == null || keys.Length < 2)
                {
                    errors.Add(label + ": needs at least two keys.");
                    continue;
                }

                if (keys[0].Progress != 0f || keys[keys.Length - 1].Progress != 1f)
                {
                    errors.Add(label + ": keys must start at Progress 0 and end at Progress 1.");
                }

                for (int k = 1; k < keys.Length; k++)
                {
                    if (keys[k].Progress <= keys[k - 1].Progress)
                    {
                        errors.Add(label + ": key Progress values must strictly increase.");
                    }

                    if (keys[k].Scale < keys[k - 1].Scale)
                    {
                        errors.Add(label + ": Scale must never decrease as level rises.");
                    }
                }

                if (curve.MaxLevel >= 2)
                {
                    float first = ScaleAtLevel(curve, 1);
                    float last = ScaleAtLevel(curve, curve.MaxLevel);

                    if (first <= 0f || first > 1f)
                    {
                        errors.Add(label + ": level-1 scale is " + first + "; it must be above 0 and at most 1.");
                    }

                    if (Math.Abs(last - 1f) > 0.0001f)
                    {
                        errors.Add(label + ": max-level scale is " + last + "; it must be exactly 1.");
                    }
                }
            }

            return ids;
        }

        private static void ValidateSpecies(SpeciesData[] species, HashSet<string> curveIds, List<string> errors)
        {
            if (species == null || species.Length == 0)
            {
                errors.Add("No species defined.");
                return;
            }

            HashSet<string> ids = new HashSet<string>();

            for (int i = 0; i < species.Length; i++)
            {
                SpeciesData s = species[i];
                if (s == null)
                {
                    errors.Add("Species #" + i + " is null.");
                    continue;
                }

                string label = "Species '" + s.SpeciesId + "'";

                if (!IsSnakeCaseId(s.SpeciesId))
                {
                    errors.Add(label + ": SpeciesId must be lowercase snake_case.");
                }
                else if (!ids.Add(s.SpeciesId))
                {
                    errors.Add(label + ": duplicate SpeciesId.");
                }

                if (string.IsNullOrEmpty(s.DisplayName))
                {
                    errors.Add(label + ": DisplayName is empty.");
                }

                if (s.Elements == null || s.Elements.Length == 0)
                {
                    errors.Add(label + ": needs at least one element.");
                }
                else
                {
                    HashSet<Element> seen = new HashSet<Element>();
                    for (int e = 0; e < s.Elements.Length; e++)
                    {
                        if (!TryParseElement(s.Elements[e], out Element element))
                        {
                            errors.Add(label + ": '" + s.Elements[e] + "' is not an Element name.");
                        }
                        else if (element == Element.None)
                        {
                            errors.Add(label + ": lists Element None; leave it out instead.");
                        }
                        else if (!seen.Add(element))
                        {
                            errors.Add(label + ": lists element " + element + " twice.");
                        }
                    }
                }

                if (string.IsNullOrEmpty(s.GrowthCurveId) || !curveIds.Contains(s.GrowthCurveId))
                {
                    errors.Add(label + ": GrowthCurveId '" + s.GrowthCurveId + "' does not match any growth curve.");
                }

                if (!TryParseStance(s.Stance, out CombatStance _))
                {
                    errors.Add(label + ": '" + s.Stance + "' is not a CombatStance name (Vanguard, Ranged or Skirmisher).");
                }

                StatBlock stats = s.BaseStats;
                int[] values = { stats.Hp, stats.Attack, stats.Defense, stats.SpecialAttack, stats.SpecialDefense, stats.Speed, stats.MoveRange };
                string[] names = { "Hp", "Attack", "Defense", "SpecialAttack", "SpecialDefense", "Speed", "MoveRange" };
                for (int v = 0; v < values.Length; v++)
                {
                    if (values[v] < 1)
                    {
                        errors.Add(label + ": BaseStats." + names[v] + " is " + values[v] + "; it must be at least 1.");
                    }
                }

                // CritChance is a percent chance, not a combat stat: 0 ("never crits") is legal, and
                // anything outside 0-100 cannot be a chance. The balance band lives in the tests.
                if (stats.CritChance < MinCritChance || stats.CritChance > MaxCritChance)
                {
                    errors.Add(label + ": BaseStats.CritChance is " + stats.CritChance + "; it must be between " + MinCritChance + " and " +
                               MaxCritChance + " (a percent chance).");
                }
            }
        }
    }
}
