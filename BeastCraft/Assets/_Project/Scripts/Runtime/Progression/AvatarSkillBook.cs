using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The avatar's skills, as save data: its active support skills (<see cref="Actives"/>, on the
    /// cooldown rotation it has always had) and its passives (<see cref="Passives"/>, its main role).
    /// Both are acquired and leveled slowly through play on the same model as beast skills —
    /// practice XP from battle use, materials, breakthrough gates. See the battle-system design doc,
    /// "Avatar passives".
    /// <para>
    /// The two halves are separate books because they hold different kinds of asset under
    /// separate id spaces and slot counts; every equip and practice rule is the shared
    /// <see cref="SkillBook"/>'s. Materials and breakthroughs go through
    /// <see cref="SkillProgression"/> on the entry from <see cref="SkillBook.GetProgress"/>, with the
    /// skill's or passive's own <see cref="SkillProgressionDefinition"/>, exactly as for a beast.
    /// Build a battle avatar from it with
    /// <see cref="BattleAvatar.Create(AvatarSkillBook, Func{string, SkillSO}, Func{string, PassiveSkillSO}, BeastCraft.Creatures.StatBlock, IEnumerable{AvatarGearSO}, int, out PassiveLoadout, string)"/>.
    /// </para>
    /// </summary>
    [Serializable]
    public class AvatarSkillBook
    {
        /// <summary>
        /// How many active skills the avatar takes into battle. Before this book the avatar's
        /// loadout had no fixed size; 3 matches a beast's <see cref="BeastSkillBook.EquipSlotCount"/>.
        /// Tunable, but save data sizes to it.
        /// </summary>
        public const int ActiveSlotCount = 3;

        /// <summary>How many passives the avatar takes into battle (decided by the user). Tunable, but save data sizes to it.</summary>
        public const int PassiveSlotCount = 3;

        /// <summary>The avatar's active support skills.</summary>
        public AvatarActiveSkillBook Actives = new AvatarActiveSkillBook();

        /// <summary>The avatar's passive skills.</summary>
        public AvatarPassiveSkillBook Passives = new AvatarPassiveSkillBook();

        /// <summary>
        /// Credits one battle's practice XP to both halves: <paramref name="activeUses"/> (typically
        /// <see cref="BattleSkillUsage.CountAvatarActiveUses"/>) to the actives with each skill's
        /// <see cref="SkillSO.Progression"/> from <paramref name="activeLookup"/>, and
        /// <paramref name="passiveTriggers"/> (typically
        /// <see cref="BattleSkillUsage.CountPassiveTriggers"/>) to the passives with each passive's
        /// <see cref="PassiveSkillSO.Progression"/> from <paramref name="passiveLookup"/>. A passive
        /// earns practice per time it fired, exactly as a skill does per fire, under the same
        /// per-award cap. Ids a book does not know are ignored; unresolvable ids use the default
        /// definition; null maps credit nothing. Returns the total levels gained.
        /// </summary>
        public int AwardPractice(IReadOnlyDictionary<string, int> activeUses, Func<string, SkillSO> activeLookup,
                                 IReadOnlyDictionary<string, int> passiveTriggers, Func<string, PassiveSkillSO> passiveLookup)
        {
            int gained = 0;

            if (Actives != null)
            {
                gained += Actives.AwardPractice(activeUses, BeastSkillBook.SkillProgressionLookup(activeLookup));
            }

            if (Passives != null)
            {
                gained += Passives.AwardPractice(passiveTriggers, PassiveProgressionLookup(passiveLookup));
            }

            return gained;
        }

        private static Func<string, SkillProgressionDefinition> PassiveProgressionLookup(Func<string, PassiveSkillSO> passiveLookup)
        {
            if (passiveLookup == null)
            {
                return null;
            }

            return id =>
            {
                PassiveSkillSO passive = passiveLookup(id);
                return passive == null ? null : passive.Progression;
            };
        }
    }
}
