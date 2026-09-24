using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Save;

namespace BeastCraft.Session
{
    /// <summary>
    /// The game content a battle session resolves save ids against: species, skills (beast skills
    /// and avatar actives share one id space), passives, team bonds, and beast and avatar gear — the asset types the
    /// importers and <c>SkillLibraryBuilder</c> produce. The game fills it from its loaded assets;
    /// tests and tools from assets built out of the authored JSON.
    /// <para>
    /// Lookups are by stable id, ordinal. Null entries and entries with an empty id are skipped;
    /// when two entries share an id the first wins. Lookups of a null, empty or unknown id return
    /// <c>null</c>. Never throws.
    /// </para>
    /// </summary>
    public class BattleContent : ISaveGearCatalog
    {
        private readonly Dictionary<string, CreatureSpeciesSO> _species = new Dictionary<string, CreatureSpeciesSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillSO> _skills = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, PassiveSkillSO> _passives = new Dictionary<string, PassiveSkillSO>(StringComparer.Ordinal);
        private readonly List<TeamBondSO> _teamBonds = new List<TeamBondSO>();
        private readonly Dictionary<string, GearSO> _gear = new Dictionary<string, GearSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, AvatarGearSO> _avatarGear = new Dictionary<string, AvatarGearSO>(StringComparer.Ordinal);

        /// <param name="species">Every species a beast or enemy may be.</param>
        /// <param name="skills">Every skill a beast, enemy or the avatar may have equipped.</param>
        /// <param name="passives">Every avatar passive.</param>
        /// <param name="teamBonds">The team bonds, in application order. May be null (no bonds).</param>
        /// <param name="gear">Every beast gear definition, by <see cref="GearSO.GearId"/>. May be null.</param>
        /// <param name="avatarGear">Every avatar gear definition, by <see cref="AvatarGearSO.AvatarGearId"/>. May be null.</param>
        /// <param name="enemies">The enemy library's catalog, for enemy-library enemies (an <see cref="EnemySpec.SpeciesId"/> that is no roster species). May be null.</param>
        public BattleContent(IEnumerable<CreatureSpeciesSO> species, IEnumerable<SkillSO> skills, IEnumerable<PassiveSkillSO> passives,
                             IEnumerable<TeamBondSO> teamBonds, IEnumerable<GearSO> gear = null, IEnumerable<AvatarGearSO> avatarGear = null,
                             EnemyCatalog enemies = null)
        {
            Enemies = enemies;
            AddAll(_gear, gear, g => g.GearId);
            AddAll(_avatarGear, avatarGear, g => g.AvatarGearId);
            AddAll(_species, species, s => s.SpeciesId);
            AddAll(_skills, skills, s => s.SkillId);
            AddAll(_passives, passives, p => p.PassiveId);

            if (teamBonds != null)
            {
                foreach (TeamBondSO bond in teamBonds)
                {
                    if (bond != null)
                    {
                        _teamBonds.Add(bond);
                    }
                }
            }
        }

        /// <summary>The enemy library's catalog, or null when the content has no enemy library.</summary>
        public EnemyCatalog Enemies { get; }

        /// <summary>The team bonds, in application order.</summary>
        public IReadOnlyList<TeamBondSO> TeamBonds
        {
            get { return _teamBonds; }
        }

        /// <summary>The species with <paramref name="speciesId"/>, or null.</summary>
        public CreatureSpeciesSO GetSpecies(string speciesId)
        {
            return Find(_species, speciesId);
        }

        /// <summary>The skill with <paramref name="skillId"/>, or null. Usable as a <c>Func&lt;string, SkillSO&gt;</c> lookup.</summary>
        public SkillSO GetSkill(string skillId)
        {
            return Find(_skills, skillId);
        }

        /// <summary>The passive with <paramref name="passiveId"/>, or null. Usable as a <c>Func&lt;string, PassiveSkillSO&gt;</c> lookup.</summary>
        public PassiveSkillSO GetPassive(string passiveId)
        {
            return Find(_passives, passiveId);
        }

        /// <summary>The beast gear with <paramref name="gearId"/>, or null.</summary>
        public GearSO GetGear(string gearId)
        {
            return Find(_gear, gearId);
        }

        /// <summary>The avatar gear with <paramref name="avatarGearId"/>, or null.</summary>
        public AvatarGearSO GetAvatarGear(string avatarGearId)
        {
            return Find(_avatarGear, avatarGearId);
        }

        public bool TryGetBeastGear(string gearId, out GearSlot slot, out int minimumLevel)
        {
            GearSO gear = GetGear(gearId);
            slot = gear == null ? default(GearSlot) : gear.Slot;
            minimumLevel = gear == null ? 0 : gear.MinimumLevel;
            return gear != null;
        }

        public bool TryGetAvatarGear(string gearId, out AvatarGearSlot slot, out int minimumLevel)
        {
            AvatarGearSO gear = GetAvatarGear(gearId);
            slot = gear == null ? default(AvatarGearSlot) : gear.Slot;
            minimumLevel = gear == null ? 0 : gear.MinimumLevel;
            return gear != null;
        }

        private static void AddAll<T>(Dictionary<string, T> into, IEnumerable<T> items, Func<T, string> idOf) where T : class
        {
            if (items == null)
            {
                return;
            }

            foreach (T item in items)
            {
                string id = item == null ? null : idOf(item);

                if (!string.IsNullOrEmpty(id) && !into.ContainsKey(id))
                {
                    into.Add(id, item);
                }
            }
        }

        private static T Find<T>(Dictionary<string, T> from, string id) where T : class
        {
            return !string.IsNullOrEmpty(id) && from.TryGetValue(id, out T item) ? item : null;
        }
    }
}
