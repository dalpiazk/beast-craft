using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The authored skill library (<c>skill-library.json</c>) as the simulator fields it: every
    /// beast skill built twice (as authored, and with its element forced to <c>None</c> for
    /// <see cref="KitMode.Neutral"/>), each species' default loadout, and the avatar's default
    /// actives (the first <see cref="SkillLibraryData.AvatarDefaultActiveCount"/>) and passives.
    /// Everything is built through <see cref="SkillLibraryBuilder"/>, the same mapping the game's
    /// content loader uses, so the simulator fights with the skills the game builds.
    /// <para>
    /// Skills are fielded at one skill level for the whole run (<c>--skill-level</c>), at the tier
    /// that level implies (<see cref="SkillLibraryBuilder.TierForLevel"/>): level 1 is the authored
    /// numbers, level 16 or above has passed every gate.
    /// </para>
    /// </summary>
    public sealed class SkillLibraryKits
    {
        /// <summary>The library's path relative to the repo root.</summary>
        public const string RepoRelativePath = SkillLibraryData.ProjectRelativePath;

        private readonly Dictionary<string, SkillSO> _elemental = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillSO> _neutral = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> _defaults = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly List<SkillSO> _avatarActives = new List<SkillSO>();
        private readonly List<PassiveSkillSO> _avatarPassives = new List<PassiveSkillSO>();
        private readonly List<TeamBondSO> _teamBonds = new List<TeamBondSO>();

        private SkillLibraryKits(int skillLevel)
        {
            SkillLevel = skillLevel;
        }

        /// <summary>The skill level every skill and passive is fielded at.</summary>
        public int SkillLevel { get; }

        /// <summary>The avatar's default actives, in slot order.</summary>
        public IReadOnlyList<SkillSO> AvatarActives
        {
            get { return _avatarActives; }
        }

        /// <summary>The avatar's default passives, in slot order.</summary>
        public IReadOnlyList<PassiveSkillSO> AvatarPassives
        {
            get { return _avatarPassives; }
        }

        /// <summary>The library's team bonds, in file order (the order they are resolved and applied in).</summary>
        public IReadOnlyList<TeamBondSO> TeamBonds
        {
            get { return _teamBonds; }
        }

        /// <summary>The explicit path when given, otherwise found by walking up like the roster.</summary>
        public static string ResolvePath(string explicitPath)
        {
            return RosterLoader.ResolveFile(explicitPath, RepoRelativePath);
        }

        /// <summary>
        /// Parses and validates the library (against the roster file too, so kits and species must
        /// agree), then builds everything. Returns null and fills <paramref name="errors"/> on any problem.
        /// </summary>
        public static SkillLibraryKits Load(string path, string rosterPath, int skillLevel, List<string> errors)
        {
            SkillLibraryData library;
            BeastRosterData roster;
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions { IncludeFields = true };
                library = JsonSerializer.Deserialize<SkillLibraryData>(File.ReadAllText(path), options);
                roster = JsonSerializer.Deserialize<BeastRosterData>(File.ReadAllText(rosterPath), options);
            }
            catch (Exception exception)
            {
                errors.Add("Could not read '" + path + "': " + exception.Message);
                return null;
            }

            errors.AddRange(SkillLibraryValidator.Validate(library, roster));
            if (errors.Count > 0)
            {
                return null;
            }

            SkillLibraryKits kits = new SkillLibraryKits(skillLevel);
            foreach (SkillData data in library.BeastSkills)
            {
                kits._elemental[data.SkillId] = Build(data, false);
                kits._neutral[data.SkillId] = Build(data, true);
            }

            foreach (SpeciesKitData kit in library.SpeciesKits)
            {
                kits._defaults[kit.SpeciesId] = kit.DefaultLoadout;
            }

            for (int i = 0; i < SkillLibraryData.AvatarDefaultActiveCount; i++)
            {
                kits._avatarActives.Add(Build(library.AvatarActives[i], false));
            }

            foreach (string id in library.AvatarDefaultPassives)
            {
                PassiveSkillSO passive = new PassiveSkillSO();
                SkillLibraryBuilder.ApplyPassive(Array.Find(library.AvatarPassives, p => p.PassiveId == id), passive);
                passive.name = id;
                kits._avatarPassives.Add(passive);
            }

            foreach (TeamBondData data in library.TeamBonds ?? new TeamBondData[0])
            {
                TeamBondSO bond = new TeamBondSO();
                SkillLibraryBuilder.ApplyTeamBond(data, bond);
                bond.name = data.BondId;
                kits._teamBonds.Add(bond);
            }

            return kits;
        }

        /// <summary>The species' default loadout ids in slot order (empty when it has no kit).</summary>
        public IReadOnlyList<string> DefaultLoadout(string speciesId)
        {
            return _defaults.TryGetValue(speciesId, out string[] ids) ? ids : new string[0];
        }

        /// <summary>A built beast skill by id, as authored (the elemental copy).</summary>
        public SkillSO Skill(string skillId)
        {
            return _elemental.TryGetValue(skillId, out SkillSO skill) ? skill : null;
        }

        /// <summary>
        /// The species' default loadout at <see cref="SkillLevel"/>, as shared, read-only instances
        /// (a loadout's per-battle state lives in <see cref="SkillLoadout"/>, not here). In
        /// <see cref="KitMode.Neutral"/> every skill is the element-<c>None</c> copy.
        /// </summary>
        public SkillInstance[] BeastKit(CreatureSpeciesSO species, KitMode mode)
        {
            IReadOnlyList<string> ids = DefaultLoadout(species.SpeciesId);
            Dictionary<string, SkillSO> source = mode == KitMode.Neutral ? _neutral : _elemental;
            SkillInstance[] kit = new SkillInstance[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                kit[i] = Instance(source[ids[i]]);
            }

            return kit;
        }

        /// <summary>A skill at <see cref="SkillLevel"/> and the tier that level implies.</summary>
        public SkillInstance Instance(SkillSO skill)
        {
            return new SkillInstance(skill, SkillLevel, SkillLibraryBuilder.TierForLevel(skill.Progression, SkillLevel));
        }

        /// <summary>A fresh passive instance (fresh trigger counters) at <see cref="SkillLevel"/>.</summary>
        public PassiveInstance PassiveInstance(PassiveSkillSO passive)
        {
            return new PassiveInstance(passive, SkillLevel, SkillLibraryBuilder.TierForLevel(passive.Progression, SkillLevel));
        }

        private static SkillSO Build(SkillData data, bool neutral)
        {
            SkillSO skill = new SkillSO();
            SkillLibraryBuilder.ApplySkill(data, skill);
            skill.name = data.SkillId;
            if (neutral)
            {
                skill.Element = Element.None;
            }

            return skill;
        }
    }
}
