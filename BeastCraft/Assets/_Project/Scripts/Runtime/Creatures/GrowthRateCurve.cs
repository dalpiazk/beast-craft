using UnityEngine;

namespace BeastCraft.Creatures
{
    /// <summary>
    /// A shared, reusable leveling curve ("Fast", "Medium", "Slow", ... — the names are data, not
    /// code). Maps normalized level progress (0-1) to normalized stat-scale progress (0-1).
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Creatures/Growth Rate", fileName = "NewGrowthRate")]
    public class GrowthRateCurve : ScriptableObject
    {
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
                Debug.LogError("[Creatures] Growth rate '" + name + "' has no Curve assigned; returning scale 1.", this);
                return 1f;
            }

            if (MaxLevel <= 1)
            {
                // Single-level curve: there is no progress axis, so the level-1 value is the answer.
                return Curve.Evaluate(0f);
            }

            int clampedLevel = Mathf.Clamp(level, 1, MaxLevel);
            float progress = (clampedLevel - 1f) / (MaxLevel - 1f);
            return Curve.Evaluate(progress);
        }

        private void OnValidate()
        {
            if (MaxLevel < 1)
            {
                Debug.LogError("[Creatures] Growth rate '" + name + "' has MaxLevel " + MaxLevel +
                               "; it must be at least 1.", this);
            }
        }
    }
}
