using System;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The rules of skill progression: the XP curve, practice XP from battle use, material XP, the
    /// level caps set by breakthrough gates, and passing those gates. See the battle-system design
    /// doc, "Skill progression".
    /// <para>
    /// <strong>How a skill grows.</strong> XP comes from two sources — <em>practice</em>
    /// (<see cref="AwardPractice"/>, a flat <see cref="PracticeXpPerUse"/> per time the skill fired
    /// in battle) and <em>materials</em> (<see cref="ApplyMaterial"/>, a material's
    /// <see cref="SkillMaterialSO.XpValue"/>). Whenever banked XP covers
    /// <see cref="XpToNextLevel"/> of the current level, the skill levels up and the cost is spent,
    /// repeatedly, until XP runs short or the skill reaches its <see cref="LevelCap"/>.
    /// </para>
    /// <para>
    /// <strong>Gates.</strong> The cap is the next unpassed gate's threshold
    /// (<see cref="SkillProgressionDefinition.Tiers"/>), or the max level once every gate is
    /// passed. At the cap XP keeps banking but only up to one level's worth
    /// (<c>XpToNextLevel(level)</c>); anything past that is discarded, so practice done while
    /// waiting on a material is not wholly lost but cannot be stockpiled. At the max level XP is
    /// held at 0. <see cref="TryBreakthrough"/> with a material of high enough tier passes the gate,
    /// after which a full bank immediately buys the next level.
    /// </para>
    /// <para>
    /// <strong>Generic.</strong> Every function takes the <see cref="SkillProgressionDefinition"/>
    /// rather than a skill asset, so beast skills and (later) avatar passives share it. A null
    /// definition reads as the defaults; a null progress is a no-op. Non-throwing throughout, like
    /// the battle namespace. Mutates only the <see cref="SkillProgress"/> it is handed.
    /// </para>
    /// <para>
    /// <strong>Tunable starting defaults, not confirmed balance.</strong> With the constants below,
    /// reaching level 5 takes 1,703 XP (171 uses), level 10 takes 11,106 XP (1,111 uses) and level
    /// 20 takes 67,135 XP (6,714 uses) on practice alone — deliberately slow; materials are the
    /// intended accelerator.
    /// </para>
    /// </summary>
    public static class SkillProgression
    {
        /// <summary>XP to go from level 1 to level 2; the scale of the whole curve.</summary>
        public const int XpCurveBase = 100;

        /// <summary>
        /// Exponent of the curve: <c>XpToNextLevel(level) = round(XpCurveBase * level ^ XpCurveExponent)</c>.
        /// </summary>
        public const double XpCurveExponent = 1.5;

        /// <summary>Practice XP per time the skill fired in battle. Flat: no diminishing returns per use.</summary>
        public const int PracticeXpPerUse = 10;

        /// <summary>
        /// The most uses one <see cref="AwardPractice"/> call credits. Callers award once per
        /// battle, so this is a per-battle cap: it stops a long fight with a cooldown-0 skill from
        /// being farmed.
        /// </summary>
        public const int PracticeUseCapPerAward = 20;

        /// <summary>
        /// XP needed to go from <paramref name="level"/> to the next:
        /// <c>round(XpCurveBase * level ^ XpCurveExponent)</c>, with a level below 1 read as 1.
        /// Strictly increasing. Level 1 costs 100, level 4 costs 800, level 10 costs 3,162, level 19
        /// costs 8,282.
        /// </summary>
        public static int XpToNextLevel(int level)
        {
            int l = level < 1 ? 1 : level;
            return (int)Math.Round(XpCurveBase * Math.Pow(l, XpCurveExponent), MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Total XP needed to climb from level 1 to <paramref name="level"/>, ignoring gates: the sum
        /// of <see cref="XpToNextLevel"/> over levels 1 to <c>level - 1</c>. 0 at level 1 or below.
        /// For UI and tuning.
        /// </summary>
        public static int TotalXpToReach(int level)
        {
            int total = 0;

            for (int l = 1; l < level; l++)
            {
                total += XpToNextLevel(l);
            }

            return total;
        }

        /// <summary>
        /// The level <paramref name="progress"/> cannot rise above until its next gate is passed:
        /// the next unpassed gate's <see cref="SkillTierDefinition.ThresholdLevel"/> (clamped into
        /// [1, max level]), or the max level when every gate is passed. A null gate entry blocks
        /// nothing (it reads as the max level).
        /// </summary>
        public static int LevelCap(SkillProgressionDefinition definition, int tier)
        {
            SkillProgressionDefinition def = definition ?? SkillProgressionDefinition.Fallback;
            int max = def.EffectiveMaxLevel;
            SkillTierDefinition next = def.GetTier(def.ClampTier(tier));

            return next == null ? max : def.ClampLevel(next.ThresholdLevel);
        }

        /// <summary>
        /// Whether <paramref name="progress"/> is sitting on a gate: it has reached
        /// <see cref="LevelCap"/> and a gate remains. Such a skill gains no more levels until
        /// <see cref="TryBreakthrough"/> succeeds.
        /// </summary>
        public static bool IsAwaitingBreakthrough(SkillProgress progress, SkillProgressionDefinition definition)
        {
            if (progress == null)
            {
                return false;
            }

            SkillProgressionDefinition def = definition ?? SkillProgressionDefinition.Fallback;
            int tier = def.ClampTier(progress.Tier);

            return tier < def.TierCount && progress.Level >= LevelCap(def, tier);
        }

        /// <summary>
        /// Credits <paramref name="uses"/> battle uses of the skill: <c>min(uses,
        /// PracticeUseCapPerAward) * PracticeXpPerUse</c> XP, then levels up as far as XP and the
        /// cap allow. Returns the levels gained. Zero or negative uses change nothing.
        /// </summary>
        public static int AwardPractice(SkillProgress progress, SkillProgressionDefinition definition, int uses)
        {
            if (uses <= 0)
            {
                return 0;
            }

            int credited = uses > PracticeUseCapPerAward ? PracticeUseCapPerAward : uses;
            return AddXp(progress, definition, credited * PracticeXpPerUse);
        }

        /// <summary>
        /// Feeds one <paramref name="material"/> to the skill: adds its
        /// <see cref="SkillMaterialSO.XpValue"/> (negative read as 0) and levels up as far as XP and
        /// the cap allow. Any tier of material may be fed; tier matters only for
        /// <see cref="TryBreakthrough"/>. Returns the levels gained; a null material adds nothing.
        /// </summary>
        public static int ApplyMaterial(SkillProgress progress, SkillProgressionDefinition definition, SkillMaterialSO material)
        {
            if (material == null)
            {
                return 0;
            }

            return AddXp(progress, definition, material.XpValue);
        }

        /// <summary>
        /// Adds raw XP (negative read as 0) and levels up while the bank covers
        /// <see cref="XpToNextLevel"/> and the level is below <see cref="LevelCap"/>. At the cap the
        /// bank is trimmed to one level's worth, or to 0 at the max level. Also normalizes the
        /// progress (level and tier clamped to the definition, XP not negative). Returns the levels
        /// gained. The shared tail of every XP source.
        /// </summary>
        public static int AddXp(SkillProgress progress, SkillProgressionDefinition definition, int xp)
        {
            if (progress == null)
            {
                return 0;
            }

            SkillProgressionDefinition def = definition ?? SkillProgressionDefinition.Fallback;
            Normalize(progress, def);

            long bank = (long)progress.Xp + (xp < 0 ? 0 : xp);
            int cap = LevelCap(def, progress.Tier);
            int gained = 0;

            while (progress.Level < cap)
            {
                int cost = XpToNextLevel(progress.Level);

                if (bank < cost)
                {
                    break;
                }

                bank -= cost;
                progress.Level += 1;
                gained += 1;
            }

            if (progress.Level >= def.EffectiveMaxLevel)
            {
                bank = 0;
            }
            else if (progress.Level >= cap)
            {
                long full = XpToNextLevel(progress.Level);
                bank = bank > full ? full : bank;
            }

            progress.Xp = bank > int.MaxValue ? int.MaxValue : (int)bank;
            return gained;
        }

        /// <summary>
        /// Tries to pass the next gate with <paramref name="material"/>. Succeeds only when a gate
        /// remains, the skill has reached that gate's threshold level, and the material's tier is
        /// at least the gate's required tier; the tier then goes up by one and any banked XP is
        /// immediately spent on levels under the new cap. The material's
        /// <see cref="SkillMaterialSO.XpValue"/> is <em>not</em> added — feeding XP is
        /// <see cref="ApplyMaterial"/>'s job. On anything but
        /// <see cref="SkillBreakthroughResult.Success"/> the progress is unchanged and the caller
        /// should not consume the material.
        /// </summary>
        public static SkillBreakthroughResult TryBreakthrough(SkillProgress progress, SkillProgressionDefinition definition, SkillMaterialSO material)
        {
            if (progress == null || material == null)
            {
                return SkillBreakthroughResult.MissingInput;
            }

            SkillProgressionDefinition def = definition ?? SkillProgressionDefinition.Fallback;
            int tier = def.ClampTier(progress.Tier);

            if (tier >= def.TierCount)
            {
                return SkillBreakthroughResult.NoTierRemaining;
            }

            if (def.ClampLevel(progress.Level) < LevelCap(def, tier))
            {
                return SkillBreakthroughResult.BelowThreshold;
            }

            SkillTierDefinition gate = def.GetTier(tier);
            int required = gate == null ? 0 : gate.RequiredMaterialTier;

            if (material.Tier < required)
            {
                return SkillBreakthroughResult.MaterialTierTooLow;
            }

            progress.Tier = tier + 1;
            AddXp(progress, def, 0);
            return SkillBreakthroughResult.Success;
        }

        /// <summary>Clamps level and tier into the definition's ranges and XP to non-negative.</summary>
        private static void Normalize(SkillProgress progress, SkillProgressionDefinition def)
        {
            progress.Level = def.ClampLevel(progress.Level);
            progress.Tier = def.ClampTier(progress.Tier);

            if (progress.Xp < 0)
            {
                progress.Xp = 0;
            }
        }
    }
}
