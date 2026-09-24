using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;

namespace BeastCraft.Session
{
    /// <summary>
    /// The game content a battle session resolves save ids against: species, skills (beast skills
    /// and avatar actives share one id space), passives and team bonds — the asset types the
    /// importers and <c>SkillLibraryBuilder</c> produce. The game fills it from its loaded assets;
    /// tests and tools from assets built out of the authored JSON.
    /// <para>
    /// Lookups are by stable id, ordinal. Null entries and entries with an empty id are skipped;
    /// when two entries share an id the first wins. Lookups of a null, empty or unknown id return
    /// <c>null</c>. Never throws.
    /// </para>
    /// </summary>
    public class BattleContent
    {
        private readonly Dictionary<string, CreatureSpeciesSO> _species = new Dictionary<string, CreatureSpeciesSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillSO> _skills = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, PassiveSkillSO> _passives = new Dictionary<string, PassiveSkillSO>(StringComparer.Ordinal);
        private readonly List<TeamBondSO> _teamBonds = new List<TeamBondSO>();

        /// <param name="species">Every species a beast or enemy may be.</param>
        /// <param name="skills">Every skill a beast, enemy or the avatar may have equipped.</param>
        /// <param name="passives">Every avatar passive.</param>
        /// <param name="teamBonds">The team bonds, in application order. May be null (no bonds).</param>
        public BattleContent(IEnumerable<CreatureSpeciesSO> species, IEnumerable<SkillSO> skills, IEnumerable<PassiveSkillSO> passives,
                             IEnumerable<TeamBondSO> teamBonds)
        {
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
