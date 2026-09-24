using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Creatures.Roster
{
    /// <summary>
    /// Copies <see cref="BeastRosterData"/> entries onto the runtime objects: the one field mapping
    /// for growth curves and species, shared by the Editor importer (which applies it to existing
    /// or new assets, keeping their GUIDs) and anything that needs the roster outside the Editor
    /// (tests, tools), which builds fresh in-memory instances with <see cref="BuildAll"/>. The
    /// roster counterpart of <see cref="Skills.SkillLibraryBuilder"/>.
    /// <para>
    /// Only the JSON-owned fields are written: on a curve its id, keys and max level; on a species
    /// its id, text, base stats, growth-curve link, elements, stance and footprint. A species'
    /// icon, evolution options, learnable skills, default loadout and customization schema are
    /// never touched (the skill library importer wires the skill lists).
    /// </para>
    /// <para>
    /// Expects data that passed <see cref="BeastRosterValidator.Validate"/>: a name that does not
    /// parse falls back to the field's default rather than throwing, but a validated file never
    /// hits that.
    /// </para>
    /// </summary>
    public static class BeastRosterBuilder
    {
        /// <summary>Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="curve"/>.</summary>
        public static void ApplyCurve(GrowthCurveData data, GrowthRateCurve curve)
        {
            curve.CurveId = data.CurveId;
            curve.Curve = data.ToAnimationCurve();
            curve.MaxLevel = data.MaxLevel;
        }

        /// <summary>
        /// Writes every JSON-owned field of <paramref name="data"/> onto <paramref name="species"/>,
        /// linking it to <paramref name="growthRate"/> (the curve named by
        /// <see cref="SpeciesData.GrowthCurveId"/>, resolved by the caller).
        /// </summary>
        public static void ApplySpecies(SpeciesData data, CreatureSpeciesSO species, GrowthRateCurve growthRate)
        {
            species.SpeciesId = data.SpeciesId;
            species.DisplayName = data.DisplayName;
            species.Description = data.Description;
            species.BaseStats = data.BaseStats;
            species.ArtKey = string.IsNullOrEmpty(data.ArtKey) ? null : data.ArtKey;
            species.GrowthRate = growthRate;

            string[] names = data.Elements ?? new string[0];
            Element[] elements = new Element[names.Length];
            for (int i = 0; i < elements.Length; i++)
            {
                BeastRosterValidator.TryParseElement(names[i], out elements[i]);
            }

            species.Elements = elements;
            BeastRosterValidator.TryParseStance(data.Stance, out species.Stance);
            species.Footprint = ParseFootprint(data.Footprint);
        }

        /// <summary>
        /// A <see cref="UnitFootprint"/> member name, case-sensitive, names only. Null, empty or
        /// unparsable is <see cref="UnitFootprint.Single"/> (the validator only admits Single for a
        /// beast anyway).
        /// </summary>
        public static UnitFootprint ParseFootprint(string name)
        {
            if (string.IsNullOrEmpty(name) || !Enum.IsDefined(typeof(UnitFootprint), name))
            {
                return UnitFootprint.Single;
            }

            return (UnitFootprint)Enum.Parse(typeof(UnitFootprint), name);
        }

        /// <summary>
        /// Fresh in-memory instances of the whole roster: one <see cref="GrowthRateCurve"/> per
        /// curve (in <paramref name="curves"/>, by id) and one <see cref="CreatureSpeciesSO"/> per
        /// species, in file order, each named after its id. A species whose curve id does not
        /// resolve gets no curve. The caller owns the instances (destroy them when done in the
        /// Editor). Null data gives empty results.
        /// </summary>
        public static List<CreatureSpeciesSO> BuildAll(BeastRosterData roster, out Dictionary<string, GrowthRateCurve> curves)
        {
            curves = new Dictionary<string, GrowthRateCurve>(StringComparer.Ordinal);
            List<CreatureSpeciesSO> species = new List<CreatureSpeciesSO>();

            if (roster == null)
            {
                return species;
            }

            foreach (GrowthCurveData data in roster.GrowthCurves ?? new GrowthCurveData[0])
            {
                if (data == null || string.IsNullOrEmpty(data.CurveId))
                {
                    continue;
                }

                GrowthRateCurve curve = new GrowthRateCurve();
                curve.name = data.CurveId;
                ApplyCurve(data, curve);
                curves[data.CurveId] = curve;
            }

            foreach (SpeciesData data in roster.Species ?? new SpeciesData[0])
            {
                if (data == null)
                {
                    continue;
                }

                CreatureSpeciesSO beast = new CreatureSpeciesSO();
                beast.name = data.SpeciesId;
                curves.TryGetValue(data.GrowthCurveId ?? string.Empty, out GrowthRateCurve growth);
                ApplySpecies(data, beast, growth);
                species.Add(beast);
            }

            return species;
        }
    }
}
