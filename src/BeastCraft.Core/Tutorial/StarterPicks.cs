using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Localization;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Tutorial
{
    /// <summary>
    /// How a new player gets their first three beasts (producer decision, Hearthglen): the 1st is a
    /// free pick of the whole roster at New Game; the 2nd, won at Hearthglen's first trial, is any
    /// beast of the NEXT stance in the cycle <see cref="StanceCycle"/> (Vanguard, then Ranged, then
    /// Skirmisher, then Vanguard again) after the 1st pick's; the 3rd, won at the second trial, any
    /// beast of the remaining stance. So every team of three is one of each stance, and no species
    /// twice. Every pick joins at <see cref="JoinLevel"/> wearing its species' default loadout.
    /// <para>
    /// The skip-tutorial path makes the same three picks in a row (<see cref="IsLegalSequence"/>) and
    /// lands in the first campaign region (<see cref="NewGameSkippingTutorial"/>). A pick is
    /// <em>pending</em> (<see cref="PendingStep"/>) while Hearthglen is not cleared and the player
    /// owns fewer beasts than the picks they have earned: none at all (the New Game pick), or a
    /// won trial whose pick was not yet made (the app closed in between: it is offered again).
    /// Non-throwing; a refused call changes nothing.
    /// </para>
    /// </summary>
    public static class StarterPicks
    {
        /// <summary>How many beasts the picks give (one per stance).</summary>
        public const int PickCount = 3;

        /// <summary>The level every pick joins at (producer decision).</summary>
        public const int JoinLevel = 1;

        /// <summary>The stance cycle: each pick after the first is the stance after the previous one's.</summary>
        public static readonly CombatStance[] StanceCycle = { CombatStance.Vanguard, CombatStance.Ranged, CombatStance.Skirmisher };

        /// <summary>The stance after <paramref name="stance"/> in <see cref="StanceCycle"/>.</summary>
        public static CombatStance NextStance(CombatStance stance)
        {
            int index = Array.IndexOf(StanceCycle, stance);
            return StanceCycle[(index + 1) % StanceCycle.Length];
        }

        /// <summary>
        /// The stance pick <paramref name="step"/> (1-3) must be, given the 1st pick's
        /// <paramref name="firstStance"/>: null for step 1 (any), the next stance for step 2, the one
        /// after that (the remaining one) for step 3.
        /// </summary>
        public static CombatStance? RequiredStance(int step, CombatStance firstStance)
        {
            if (step <= 1)
            {
                return null;
            }

            CombatStance stance = firstStance;
            for (int i = 1; i < step; i++)
            {
                stance = NextStance(stance);
            }

            return stance;
        }

        /// <summary>
        /// The species pick number <c>picked.Count + 1</c> may be, in roster order: every species
        /// for the 1st pick; for the 2nd and 3rd, the species of the required stance
        /// (<see cref="RequiredStance"/>) — never one already picked. Empty once all three are made or
        /// when <paramref name="picked"/> names a species the roster lacks.
        /// </summary>
        public static List<CreatureSpeciesSO> Options(IReadOnlyList<string> picked, IReadOnlyList<CreatureSpeciesSO> roster)
        {
            List<CreatureSpeciesSO> options = new List<CreatureSpeciesSO>();
            int count = picked == null ? 0 : picked.Count;
            if (roster == null || count >= PickCount)
            {
                return options;
            }

            CombatStance? required = null;
            if (count > 0)
            {
                CreatureSpeciesSO first = Find(roster, picked[0]);
                if (first == null)
                {
                    return options;
                }

                required = RequiredStance(count + 1, first.Stance);
            }

            foreach (CreatureSpeciesSO species in roster)
            {
                if (species == null || (required.HasValue && species.Stance != required.Value) || Contains(picked, species.SpeciesId))
                {
                    continue;
                }

                options.Add(species);
            }

            return options;
        }

        /// <summary>Whether <paramref name="speciesId"/> may be the next pick after <paramref name="picked"/>; <paramref name="reason"/> says why not.</summary>
        public static bool IsLegal(IReadOnlyList<string> picked, string speciesId, IReadOnlyList<CreatureSpeciesSO> roster, out string reason)
        {
            reason = null;
            CreatureSpeciesSO species = Find(roster, speciesId);
            if (species == null)
            {
                reason = "Unknown beast '" + speciesId + "'.";
                return false;
            }

            int count = picked == null ? 0 : picked.Count;
            if (count >= PickCount)
            {
                reason = RulesText.Format("ui.rules.pick.all_picked", PickCount);
                return false;
            }

            if (Contains(picked, speciesId))
            {
                reason = RulesText.Format("ui.rules.pick.in_team", species.DisplayName);
                return false;
            }

            if (count > 0)
            {
                CreatureSpeciesSO first = Find(roster, picked[0]);
                CombatStance? required = first == null ? null : RequiredStance(count + 1, first.Stance);
                if (!required.HasValue || species.Stance != required.Value)
                {
                    reason = RulesText.Format("ui.rules.pick.must_be", required.HasValue ? required.Value.ToString() : "?");
                    return false;
                }
            }

            return true;
        }

        /// <summary>Whether <paramref name="species"/> (1-3 ids, in pick order) is a legal run of picks.</summary>
        public static bool IsLegalSequence(IReadOnlyList<string> species, IReadOnlyList<CreatureSpeciesSO> roster, out string reason)
        {
            reason = null;
            if (species == null || species.Count == 0 || species.Count > PickCount)
            {
                reason = "Pick 1 to " + PickCount + " beasts.";
                return false;
            }

            List<string> picked = new List<string>();
            foreach (string id in species)
            {
                if (!IsLegal(picked, id, roster, out reason))
                {
                    return false;
                }

                picked.Add(id);
            }

            return true;
        }

        /// <summary>The species of the save's first <see cref="PickCount"/> beasts, in the order obtained (its picks).</summary>
        public static List<string> Picked(PlayerSave save)
        {
            List<string> picked = new List<string>();
            foreach (OwnedBeast beast in save == null || save.Beasts == null ? new List<OwnedBeast>() : save.Beasts)
            {
                if (beast != null && beast.Progress != null && picked.Count < PickCount)
                {
                    picked.Add(beast.Progress.SpeciesId);
                }
            }

            return picked;
        }

        /// <summary>
        /// The pick waiting to be made (1-3), or 0 for none: 0 once Hearthglen is cleared; 1 when the
        /// save owns no beast; otherwise the next pick when the Hearthglen expedition in progress has
        /// a cleared trial (<see cref="FixedNodeData.PickStep"/>) whose pick is not owned yet.
        /// </summary>
        public static int PendingStep(PlayerSave save, RegionLibrary regions)
        {
            if (save == null || (save.Tutorial != null && save.Tutorial.HearthglenCleared))
            {
                return 0;
            }

            int owned = save.Beasts == null ? 0 : save.Beasts.Count;
            if (owned == 0)
            {
                return 1;
            }

            MapRun run = save.Campaign == null ? null : save.Campaign.ActiveRun;
            if (regions == null || run == null || !save.Campaign.HasActiveRun || !regions.IsTutorial(run.RegionId) || owned >= PickCount)
            {
                return 0;
            }

            foreach (int nodeId in run.Cleared ?? new List<int>())
            {
                FixedNodeData node = regions.FixedNode(run.RegionId, nodeId);
                if (node != null && node.PickStep > owned)
                {
                    return owned + 1;
                }
            }

            return 0;
        }

        /// <summary>
        /// Makes the pending pick (<see cref="PendingStep"/>): <paramref name="speciesId"/> joins at
        /// <paramref name="level"/> with its default loadout (<see cref="AddBeast"/>). Refused when no
        /// pick is pending or the species is not a legal pick (<see cref="IsLegal"/>).
        /// </summary>
        public static PickResult Pick(PlayerSave save, RegionLibrary regions, IReadOnlyList<CreatureSpeciesSO> roster, SkillLibraryData skills, string speciesId,
                                      int level = JoinLevel)
        {
            if (save == null)
            {
                return PickResult.Refused("No save.");
            }

            save.EnsureInitialized();
            int step = PendingStep(save, regions);
            if (step == 0)
            {
                return PickResult.Refused(RulesText.Get("ui.rules.pick.none_waiting"));
            }

            List<string> picked = Picked(save);
            if (!IsLegal(picked, speciesId, roster, out string reason))
            {
                return PickResult.Refused(reason);
            }

            OwnedBeast beast = AddBeast(save, skills, speciesId, level);
            return PickResult.Done(step, beast);
        }

        /// <summary>
        /// Adds <paramref name="speciesId"/> to the collection at <paramref name="level"/> (at least 1)
        /// under the next free id (<c>b1</c>, <c>b2</c>, …), knowing and wearing its species' default
        /// loadout (<c>skill-library.json</c> <c>SpeciesKits</c>). No rule is checked: callers do.
        /// </summary>
        public static OwnedBeast AddBeast(PlayerSave save, SkillLibraryData skills, string speciesId, int level)
        {
            OwnedBeast beast = OwnedBeast.Create(NextBeastId(save), speciesId, Math.Max(1, level));
            SpeciesKitData kit = skills == null ? null : Array.Find(skills.SpeciesKits ?? new SpeciesKitData[0], k => k != null && k.SpeciesId == speciesId);
            string[] loadout = kit == null || kit.DefaultLoadout == null ? new string[0] : kit.DefaultLoadout;
            for (int slot = 0; slot < loadout.Length && slot < beast.Skills.SlotCount; slot++)
            {
                beast.Skills.Learn(loadout[slot]);
                beast.Skills.Equip(slot, loadout[slot]);
            }

            save.Beasts.Add(beast);
            return beast;
        }

        /// <summary>The first unused id of the form <c>b{n}</c>, n from 1.</summary>
        public static string NextBeastId(PlayerSave save)
        {
            for (int n = 1; ; n++)
            {
                string id = "b" + n.ToString(CultureInfo.InvariantCulture);
                if (save.FindBeast(id) == null)
                {
                    return id;
                }
            }
        }

        /// <summary>
        /// The avatar's starting kit: the skill library's first <see cref="SkillLibraryData.AvatarDefaultActiveCount"/>
        /// actives and its default passives, learned and equipped (what every new game starts with).
        /// </summary>
        public static void GrantAvatarDefaults(PlayerSave save, SkillLibraryData skills)
        {
            SkillData[] actives = skills == null ? new SkillData[0] : skills.AvatarActives ?? new SkillData[0];
            for (int i = 0; i < actives.Length && i < SkillLibraryData.AvatarDefaultActiveCount && i < save.AvatarSkills.Actives.SlotCount; i++)
            {
                save.AvatarSkills.Actives.Learn(actives[i].SkillId);
                save.AvatarSkills.Actives.Equip(i, actives[i].SkillId);
            }

            string[] passives = skills == null ? new string[0] : skills.AvatarDefaultPassives ?? new string[0];
            for (int i = 0; i < passives.Length && i < save.AvatarSkills.Passives.SlotCount; i++)
            {
                save.AvatarSkills.Passives.Learn(passives[i]);
                save.AvatarSkills.Passives.Equip(i, passives[i]);
            }
        }

        /// <summary>
        /// A brand-new game (the Hearthglen path): a blank save whose only unlocked region is
        /// Hearthglen (<see cref="CampaignProgress.TutorialRegionId"/>; clearing it unlocks the first
        /// campaign region), the avatar's starting kit, and the 1st pick <paramref name="firstSpeciesId"/>
        /// at <paramref name="level"/>. Null with <paramref name="error"/> for an unknown species.
        /// </summary>
        public static PlayerSave NewGame(string firstSpeciesId, IReadOnlyList<CreatureSpeciesSO> roster, SkillLibraryData skills, out string error, int level = JoinLevel)
        {
            if (!IsLegal(new string[0], firstSpeciesId, roster, out error))
            {
                return null;
            }

            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.Lock(CampaignProgress.StartingRegionId);
            save.Campaign.Unlock(CampaignProgress.TutorialRegionId);
            GrantAvatarDefaults(save, skills);
            AddBeast(save, skills, firstSpeciesId, level);
            return save;
        }

        /// <summary>
        /// A brand-new game that skips Hearthglen: the same three picks in a row
        /// (<paramref name="species"/>, legal by <see cref="IsLegalSequence"/>) at <paramref name="level"/>,
        /// the avatar's starting kit, and Hearthglen completed as if played
        /// (<see cref="CampaignRules.SkipTutorial"/>: its completion rewards granted, the first
        /// campaign region unlocked). Null with <paramref name="error"/> when the picks are not legal.
        /// </summary>
        public static PlayerSave NewGameSkippingTutorial(IReadOnlyList<string> species, IReadOnlyList<CreatureSpeciesSO> roster, SkillLibraryData skills, RegionLibrary regions,
                                                         Func<string, Economy.ConsumableSO> consumables, out string error, int level = JoinLevel)
        {
            if (species == null || species.Count != PickCount)
            {
                error = RulesText.Format("ui.rules.pick.one_of_each", PickCount);
                return null;
            }

            if (!IsLegalSequence(species, roster, out error))
            {
                return null;
            }

            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.Lock(CampaignProgress.StartingRegionId);
            save.Campaign.Unlock(CampaignProgress.TutorialRegionId);
            GrantAvatarDefaults(save, skills);
            foreach (string id in species)
            {
                AddBeast(save, skills, id, level);
            }

            CampaignRules.SkipTutorial(save, regions, consumables);
            return save;
        }

        private static CreatureSpeciesSO Find(IReadOnlyList<CreatureSpeciesSO> roster, string speciesId)
        {
            if (roster == null || string.IsNullOrEmpty(speciesId))
            {
                return null;
            }

            foreach (CreatureSpeciesSO species in roster)
            {
                if (species != null && string.Equals(species.SpeciesId, speciesId, StringComparison.Ordinal))
                {
                    return species;
                }
            }

            return null;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null)
            {
                return false;
            }

            foreach (string other in ids)
            {
                if (string.Equals(other, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>What <see cref="StarterPicks.Pick"/> did.</summary>
    public sealed class PickResult
    {
        private PickResult()
        {
        }

        public bool Success { get; private set; }

        /// <summary>Why it was refused; null on success.</summary>
        public string Error { get; private set; }

        /// <summary>Which pick it was (1-3).</summary>
        public int Step { get; private set; }

        /// <summary>The beast that joined.</summary>
        public OwnedBeast Beast { get; private set; }

        internal static PickResult Refused(string error)
        {
            return new PickResult { Error = error };
        }

        internal static PickResult Done(int step, OwnedBeast beast)
        {
            return new PickResult { Success = true, Step = step, Beast = beast };
        }
    }
}
