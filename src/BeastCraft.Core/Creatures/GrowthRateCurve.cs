
namespace BeastCraft.Creatures
{
    /// <summary>
    /// A shared, reusable leveling curve ("Fast", "Medium", "Slow", ... — the names are data, not
    /// code). Maps normalized level progress (0-1) to a stat scale (0-1).
    /// <para>
    /// Authored curves (imported from <c>beast-roster.json</c>) start above 0 at level 1 — roughly
    /// 0.10-0.20 — and reach exactly 1 at max level, so a species' <c>BaseStats</c> are its
    /// max-level stats and level 1 is a sensible fraction of them. The default below (0 at level 1)
    /// is only the fresh-asset placeholder; a curve that starts at 0 makes every level-1 stat 0.
    /// </para>
    /// </summary>
    public class GrowthRateCurve : ContentAsset
    {
        /// <summary>
        /// Stable key species data refers to this curve by (see <c>beast-roster.json</c>); the roster
        /// importer matches existing assets on it. Never rename after ship.
        /// </summary>
        public string CurveId;

        /// <summary>Normalized level progress on X (0 = level 1, 1 = MaxLevel) to stat scale on Y.</summary>
        public AnimationCurve Curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>Highest level a creature using this curve can reach.</summary>
        public int MaxLevel = 100;

        /// <summary>
        /// Evaluates the curve for the given level. The level is clamped into [1, MaxLevel], so
        /// out-of-range input degrades to the nearest endpoint rather than extrapolating.
        /// </summary>
        public float GetScaleAtLevel(int level)
        {
            if (Curve == null)
            {
                Log.Error("[Creatures] Growth rate '" + name + "' has no Curve assigned; returning scale 1.", this);
                return 1f;
            }

            return EvaluateScale(Curve, MaxLevel, level);
        }

        /// <summary>
        /// The level-to-scale mapping behind <see cref="GetScaleAtLevel"/>, usable without an asset
        /// (the roster validator evaluates JSON-authored curves with it). <paramref name="curve"/>
        /// must not be null.
        /// </summary>
        public static float EvaluateScale(AnimationCurve curve, int maxLevel, int level)
        {
            if (maxLevel <= 1)
            {
                // Single-level curve: there is no progress axis, so the level-1 value is the answer.
                return curve.Evaluate(0f);
            }

            int clampedLevel = MathUtil.Clamp(level, 1, maxLevel);
            float progress = (clampedLevel - 1f) / (maxLevel - 1f);
            return curve.Evaluate(progress);
        }

        private void OnValidate()
        {
            if (MaxLevel < 1)
            {
                Log.Error("[Creatures] Growth rate '" + name + "' has MaxLevel " + MaxLevel +
                               "; it must be at least 1.", this);
            }
        }
    }
}
