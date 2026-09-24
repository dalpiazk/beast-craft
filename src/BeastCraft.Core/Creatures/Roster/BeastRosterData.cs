using System;

namespace BeastCraft.Creatures.Roster
{
    /// <summary>
    /// The plain-data shape of <c>data/Creatures/beast-roster.json</c>, the source of truth for the
    /// starter roster's species and growth curves. The Unity assets
    /// (<see cref="CreatureSpeciesSO"/>, <see cref="GrowthRateCurve"/>) are generated from it by the
    /// Editor importer (menu: Beast Craft/Data/Import Beast Roster); never hand-edit the imported
    /// fields on those assets, edit the JSON and re-import.
    /// <para>
    /// These are plain serializable classes with public fields and no Unity-object references, so
    /// the same types read the file inside Unity (<c>JsonUtility</c>) and outside it
    /// (<c>System.Text.Json</c> with <c>IncludeFields = true</c>, e.g. a headless balance
    /// simulator). JSON keys are the field names exactly, which match the ScriptableObject field
    /// names they import into.
    /// </para>
    /// </summary>
    [Serializable]
    public class BeastRosterData
    {
        /// <summary>Path of the roster file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Creatures/beast-roster.json";

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        public GrowthCurveData[] GrowthCurves = new GrowthCurveData[0];

        public SpeciesData[] Species = new SpeciesData[0];
    }

    /// <summary>One authored growth curve. Imports into a <see cref="GrowthRateCurve"/> asset.</summary>
    [Serializable]
    public class GrowthCurveData
    {
        /// <summary>Stable key species reference this curve by. Never rename after ship.</summary>
        public string CurveId;

        public int MaxLevel;

        /// <summary>
        /// Points on the curve, as normalized level progress (0 = level 1, 1 = <see cref="MaxLevel"/>)
        /// to stat scale. The curve is piecewise-linear between them.
        /// </summary>
        public GrowthKeyData[] Keys = new GrowthKeyData[0];

        /// <summary>
        /// Builds the Unity curve for these keys with linear tangents, so the curve is exactly
        /// piecewise-linear between the authored points in Unity too. (A <see cref="Keyframe"/> built
        /// from time and value alone has flat tangents, which would ease in and out of every key and
        /// no longer match the numbers in the JSON.)
        /// </summary>
        public AnimationCurve ToAnimationCurve()
        {
            GrowthKeyData[] keys = Keys ?? new GrowthKeyData[0];
            Keyframe[] frames = new Keyframe[keys.Length];

            for (int i = 0; i < keys.Length; i++)
            {
                float inSlope = i > 0 ? Slope(keys[i - 1], keys[i]) : 0f;
                float outSlope = i < keys.Length - 1 ? Slope(keys[i], keys[i + 1]) : 0f;

                if (i == 0)
                {
                    inSlope = outSlope;
                }

                if (i == keys.Length - 1)
                {
                    outSlope = inSlope;
                }

                frames[i] = new Keyframe(keys[i].Progress, keys[i].Scale, inSlope, outSlope);
            }

            return new AnimationCurve(frames);
        }

        private static float Slope(GrowthKeyData from, GrowthKeyData to)
        {
            float span = to.Progress - from.Progress;
            return span > 0f ? (to.Scale - from.Scale) / span : 0f;
        }
    }

    /// <summary>A single point on a <see cref="GrowthCurveData"/>.</summary>
    [Serializable]
    public class GrowthKeyData
    {
        /// <summary>Normalized level progress, 0 (level 1) to 1 (max level).</summary>
        public float Progress;

        /// <summary>Stat scale at that progress; 1 means the species' full <c>BaseStats</c>.</summary>
        public float Scale;
    }

    /// <summary>One authored species. Imports into a <see cref="CreatureSpeciesSO"/> asset.</summary>
    [Serializable]
    public class SpeciesData
    {
        /// <summary>Stable lowercase snake_case key persisted in save data. Never rename after ship.</summary>
        public string SpeciesId;

        public string DisplayName;

        public string Description;

        /// <summary><see cref="Element"/> names (e.g. <c>"Fire"</c>), parsed case-sensitively.</summary>
        public string[] Elements = new string[0];

        /// <summary>The <see cref="GrowthCurveData.CurveId"/> of the curve this species levels on.</summary>
        public string GrowthCurveId;

        /// <summary>
        /// Stats at curve scale 1 (max level). <see cref="StatBlock.MoveRange"/> and
        /// <see cref="StatBlock.CritChance"/> are exempt from the curve and apply as-is at every
        /// level; neither counts toward the six-stat budget.
        /// </summary>
        public StatBlock BaseStats;

        /// <summary>
        /// A <see cref="CombatStance"/> name (<c>"Vanguard"</c>, <c>"Ranged"</c> or
        /// <c>"Skirmisher"</c>), parsed case-sensitively. Missing or empty means
        /// <see cref="CombatStance.Vanguard"/>, the species default.
        /// </summary>
        public string Stance;

        /// <summary>
        /// Not authored: beasts are always one tile. Present only so a roster that tries to give a
        /// beast a footprint is refused by <see cref="BeastRosterValidator"/> rather than silently
        /// ignored. Missing, empty or <c>"Single"</c> is the only accepted value.
        /// </summary>
        public string Footprint;
    }
}
