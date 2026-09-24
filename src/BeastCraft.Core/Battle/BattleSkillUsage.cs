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
    /// <para>
    /// <strong>The avatar's own book.</strong> <see cref="CountAvatarActiveUses"/> and
    /// <see cref="CountPassiveTriggers"/> give the two maps
    /// <c>AvatarSkillBook.AwardPractice</c> takes: its active skills' fires by skill id, and its
    /// passives' firings by passive id (a passive's practice use is a time it fired; blocked
    /// triggers and failed proc rolls are not uses).
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

        /// <summary>
        /// The avatar's active-skill fires (every <see cref="BattleTurnResult.AvatarActivations"/>
        /// entry), by <see cref="SkillSO.SkillId"/>. Needs no avatar unit: only the avatar's casts
        /// are recorded there. Empty — never <c>null</c> — for a null result or an avatar that cast
        /// nothing; skills with no id are not counted.
        /// </summary>
        public static Dictionary<string, int> CountAvatarActiveUses(BattleResult result)
        {
            Dictionary<string, int> uses = new Dictionary<string, int>(StringComparer.Ordinal);

            if (result == null)
            {
                return uses;
            }

            for (int t = 0; t < result.Turns.Count; t++)
            {
                BattleTurnResult turn = result.Turns[t];

                if (turn == null)
                {
                    continue;
                }

                for (int a = 0; a < turn.AvatarActivations.Count; a++)
                {
                    SkillActivation activation = turn.AvatarActivations[a];
                    Increment(uses, activation == null || activation.Skill == null ? null : activation.Skill.SkillId);
                }
            }

            return uses;
        }

        /// <summary>
        /// How many times each avatar passive fired: <see cref="BattleResult.OpeningPassiveActivations"/>
        /// plus every turn's <see cref="BattleTurnResult.PassiveActivations"/>, by
        /// <see cref="BeastCraft.Avatar.PassiveSkillSO.PassiveId"/>. Empty — never <c>null</c> —
        /// for a null result or no firings; passives with no id are not counted.
        /// </summary>
        public static Dictionary<string, int> CountPassiveTriggers(BattleResult result)
        {
            Dictionary<string, int> triggers = new Dictionary<string, int>(StringComparer.Ordinal);

            if (result == null)
            {
                return triggers;
            }

            CountPassives(triggers, result.OpeningPassiveActivations);

            for (int t = 0; t < result.Turns.Count; t++)
            {
                if (result.Turns[t] != null)
                {
                    CountPassives(triggers, result.Turns[t].PassiveActivations);
                }
            }

            return triggers;
        }

        private static void CountPassives(Dictionary<string, int> triggers, IReadOnlyList<PassiveActivation> activations)
        {
            for (int i = 0; i < activations.Count; i++)
            {
                PassiveActivation activation = activations[i];
                Increment(triggers, activation == null || activation.Passive == null ? null : activation.Passive.PassiveId);
            }
        }

        private static void Increment(Dictionary<string, int> counts, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            int current;
            counts.TryGetValue(id, out current);
            counts[id] = current + 1;
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
