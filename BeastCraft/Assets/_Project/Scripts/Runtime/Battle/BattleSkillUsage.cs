using System;
using System.Collections.Generic;

namespace BeastCraft.Battle
{
    /// <summary>
    /// Reads a finished battle for how often each unit used each skill — the input to practice XP
    /// (see <c>BeastCraft.Progression.SkillProgression.AwardPractice</c> and
    /// <c>BeastSkillBook.AwardPractice</c>).
    /// <para>
    /// <strong>A use is a slot that fired</strong>: a <see cref="BattleSkillOutcome"/> whose
    /// <see cref="BattleSkillOutcome.Status"/> is <see cref="BattleSkillStatus.Fired"/>, whiffs
    /// included (the skill went off; it just found nobody). Slots that came up ready and did not go
    /// off are not uses. Uses are counted per <see cref="SkillSO.SkillId"/>, so the same skill in
    /// two slots adds up; a skill with no id cannot be progressed and is not counted.
    /// </para>
    /// <para>
    /// Pure and static: reads the result, changes nothing. Units are keyed by
    /// <see cref="BattleUnit.Id"/>, both maps compare ordinally, and a null result or unit id is
    /// simply skipped. The avatar's casts are recorded on player beasts' turns without a caster, so
    /// they are counted only when the avatar is passed in, under its own id.
    /// </para>
    /// </summary>
    public static class BattleSkillUsage
    {
        /// <summary>
        /// Every unit's fired-skill counts: unit id to (skill id to uses). Units that fired nothing
        /// are absent. When <paramref name="avatar"/> is given, its activations
        /// (<see cref="BattleTurnResult.AvatarActivations"/>) are counted under its id.
        /// </summary>
        public static Dictionary<string, Dictionary<string, int>> CountFiredSkills(BattleResult result, BattleUnit avatar = null)
        {
            Dictionary<string, Dictionary<string, int>> counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

            if (result == null)
            {
                return counts;
            }

            string avatarId = avatar == null ? null : avatar.Id;

            for (int t = 0; t < result.Turns.Count; t++)
            {
                BattleTurnResult turn = result.Turns[t];

                if (turn == null)
                {
                    continue;
                }

                string unitId = turn.Unit == null ? null : turn.Unit.Id;

                for (int o = 0; o < turn.SkillOutcomes.Count; o++)
                {
                    BattleSkillOutcome outcome = turn.SkillOutcomes[o];

                    if (outcome != null && outcome.Fired)
                    {
                        Increment(counts, unitId, outcome.Skill);
                    }
                }

                if (avatarId == null)
                {
                    continue;
                }

                for (int a = 0; a < turn.AvatarActivations.Count; a++)
                {
                    SkillActivation activation = turn.AvatarActivations[a];

                    if (activation != null)
                    {
                        Increment(counts, avatarId, activation.Skill);
                    }
                }
            }

            return counts;
        }

        /// <summary>
        /// One unit's fired-skill counts (skill id to uses), from
        /// <see cref="CountFiredSkills(BattleResult, BattleUnit)"/>. Empty — never <c>null</c> —
        /// when the unit fired nothing or is not in the result.
        /// </summary>
        public static Dictionary<string, int> CountFiredSkillsFor(BattleResult result, string unitId)
        {
            Dictionary<string, int> uses;

            if (unitId != null && CountFiredSkills(result).TryGetValue(unitId, out uses))
            {
                return uses;
            }

            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        private static void Increment(Dictionary<string, Dictionary<string, int>> counts, string unitId, SkillSO skill)
        {
            if (unitId == null || skill == null || string.IsNullOrEmpty(skill.SkillId))
            {
                return;
            }

            Dictionary<string, int> uses;

            if (!counts.TryGetValue(unitId, out uses))
            {
                uses = new Dictionary<string, int>(StringComparer.Ordinal);
                counts.Add(unitId, uses);
            }

            int current;
            uses.TryGetValue(skill.SkillId, out current);
            uses[skill.SkillId] = current + 1;
        }
    }
}
