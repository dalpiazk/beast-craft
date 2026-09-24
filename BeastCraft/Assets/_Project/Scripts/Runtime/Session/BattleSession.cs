using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Placement;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Session
{
    /// <summary>
    /// The single entry point a scene calls to fight a battle from save data and pay it out.
    /// <para>
    /// <strong><see cref="Run"/></strong> validates the <see cref="BattleSetup"/> up front, then
    /// builds everything through the battle code's own public APIs, in the balance simulator's
    /// order: the enemies (<see cref="BattleUnitFactory"/>) placed on the board (explicit
    /// positions, then <see cref="DeploymentPacker"/>), the team's beasts at their saved levels, wearing their saved gear,
    /// with loadouts from their <see cref="BeastSkillBook"/>s, seated front-most first through
    /// <see cref="PlacementValidator.TryPlaceAll"/>, the avatar
    /// (<see cref="BattleAvatar"/> from its progress, books, profile and worn gear, or <see cref="BattleSetup.AvatarGear"/> when set, with its
    /// <see cref="PassiveLoadout"/>), the team's bonds (<see cref="TeamBondLoadout.For"/>), and a
    /// <see cref="TurnManager"/> over the beasts plus the avatar; then
    /// <see cref="BattleTurnExecutor.RunBattle(TurnManager, IEnumerable{BattleUnit}, HexGrid, Random, BattleUnit, PassiveLoadout, TeamBondLoadout, int)"/>
    /// with one <see cref="Random"/> seeded from <see cref="BattleSetup.Seed"/>. Same setup, same
    /// seed, same battle.
    /// </para>
    /// <para>
    /// <strong><see cref="ApplyRewards"/></strong> pays a finished battle into the save: practice XP
    /// (<see cref="PostBattleAward.AwardPractice"/>), drops on a clear
    /// (<see cref="PostBattleAward.AwardDrops"/>, first-clear and pity included) and avatar XP
    /// (<see cref="AvatarProgression.AwardBattle"/>) and every owned beast's own XP
    /// (<see cref="BeastProgression.AwardBattle"/>: participation for every fielded beast, the
    /// clear bonus for those still standing on a win; <see cref="BeastProgression.AwardBench"/> for
    /// the beasts left on the bench), all after the level-gap falloff (<see cref="LevelGapXp"/>) and,
    /// through the overloads that take one, under a beast level cap (<see cref="LevelCap"/>).
    /// </para>
    /// <para>
    /// Neither method throws on bad input: a bad setup is a failed <see cref="BattleSessionResult"/>
    /// listing every problem, and a reward that cannot be applied is an unapplied
    /// <see cref="BattleRewardSummary"/>.
    /// </para>
    /// </summary>
    public static class BattleSession
    {
        /// <summary>The prefix of a team beast's battle unit id: <c>"beast:" + BeastId</c>.</summary>
        public const string BeastUnitIdPrefix = "beast:";

        /// <summary>Validates <paramref name="setup"/>, builds the battle and plays it out. See the class remarks.</summary>
        public static BattleSessionResult Run(BattleSetup setup)
        {
            BattleSessionResult result = new BattleSessionResult();
            List<string> errors = result.Errors;

            if (setup == null)
            {
                errors.Add("No battle setup.");
                return result;
            }

            result.Seed = setup.Seed;
            PlayerSave save = setup.Save;
            BattleContent content = setup.Content;
            EncounterSetup encounter = setup.Encounter;
            result.ShapeId = encounter == null ? null : encounter.ShapeId;
            result.EncounterLevel = encounter == null ? 0 : encounter.EncounterLevel;

            if (save == null)
            {
                errors.Add("The setup has no save.");
            }

            if (content == null)
            {
                errors.Add("The setup has no content.");
            }

            if (encounter == null)
            {
                errors.Add("The setup has no encounter.");
            }

            if (errors.Count > 0)
            {
                return result;
            }

            List<OwnedBeast> team = ResolveTeam(setup, save, content, errors);
            List<ResolvedEnemy> enemies = ResolveEnemies(encounter, content, errors);

            if (setup.IncludeAvatar && save.AvatarSkills != null)
            {
                CheckEquipped(save.AvatarSkills.Actives, "The avatar", "skill", id => content.GetSkill(id) != null, errors);
                CheckEquipped(save.AvatarSkills.Passives, "The avatar", "passive", id => content.GetPassive(id) != null, errors);
            }

            List<AvatarGearSO> avatarGear = setup.IncludeAvatar ? setup.AvatarGear ?? ResolveAvatarGear(save, content, errors) : null;
            ConsumableLoadout.Check(save, setup.Consumables, content.GetConsumable, errors);

            CheckUnitIds(team, enemies, encounter, setup.IncludeAvatar, errors);

            if (errors.Count > 0)
            {
                return result;
            }

            HexGrid grid = new HexGrid(encounter.Arena);
            List<BattleUnit> units = new List<BattleUnit>();

            if (!PlaceEnemies(grid, encounter, enemies, units, errors))
            {
                return result;
            }

            List<BattleUnit> members = PlaceTeam(grid, team, save, content, errors);

            if (members == null)
            {
                return result;
            }

            units.AddRange(members);

            PassiveLoadout passives = null;
            BattleUnit avatar = null;

            if (setup.IncludeAvatar)
            {
                avatar = BattleAvatar.Create(save.AvatarSkills, content.GetSkill, content.GetPassive, setup.AvatarProfile, save.Avatar, avatarGear,
                                             out passives);
            }

            foreach (BattleUnit unit in units)
            {
                result.StartingStats[unit.Id] = unit.Stats;
            }

            if (avatar != null)
            {
                result.StartingStats[avatar.Id] = avatar.Stats;
            }

            List<CreatureSpeciesSO> teamSpecies = new List<CreatureSpeciesSO>();
            foreach (OwnedBeast beast in team)
            {
                teamSpecies.Add(content.GetSpecies(beast.Progress.SpeciesId));
            }

            TeamBondLoadout bonds = content.TeamBonds.Count == 0 ? null : TeamBondLoadout.For(content.TeamBonds, TeamBondResolver.MembersOf(teamSpecies), members);

            List<ConsumableSO> consumables = new List<ConsumableSO>();
            foreach (string id in setup.Consumables ?? new List<string>())
            {
                consumables.Add(content.GetConsumable(id));
            }

            if (consumables.Count > 0)
            {
                // Before the bonds and passives (applied by RunBattle's battle-start hook), on its own stream.
                ConsumableLoadout.Apply(consumables, members, units.FindAll(u => u.Team == BattleTeam.Enemy), grid,
                                        new Random(LootRoller.DeriveSeed(setup.Seed, PostBattleAward.ConsumableStream)));
                result.ConsumablesUsed = new List<string>(setup.Consumables);
            }

            TurnManager turnManager = new TurnManager(avatar == null ? units : new List<BattleUnit>(units) { avatar });
            Random rng = new Random(setup.Seed);
            BattleResult battle = BattleTurnExecutor.RunBattle(turnManager, units, grid, rng, avatar, passives, bonds, setup.MaxTime);

            result.Battle = battle;
            result.Grid = grid;
            result.Units = units;
            result.Avatar = avatar;
            result.ActiveBonds = bonds == null ? new List<ActiveTeamBond>() : new List<ActiveTeamBond>(bonds.Bonds);

            List<KeyValuePair<string, string>> unitIds = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < team.Count; i++)
            {
                unitIds.Add(new KeyValuePair<string, string>(team[i].BeastId, members[i].Id));
                result.SkillUsesByBeastId[team[i].BeastId] = BattleSkillUsage.CountFiredSkillsFor(battle, members[i].Id);
            }

            result.TeamUnitIds = unitIds;

            if (avatar != null)
            {
                result.AvatarActiveUses = BattleSkillUsage.CountAvatarActiveUses(battle);
                result.PassiveTriggers = BattleSkillUsage.CountPassiveTriggers(battle);
            }

            return result;
        }

        /// <summary>
        /// <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, string, int, DropTable, int, Random)"/>
        /// with no beast level cap (<see cref="BeastProgression.MaxLevel"/>).
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, string shape, int encounterLevel,
                                                       DropTable dropTable, Random rng = null)
        {
            return ApplyRewards(save, result, content, shape, encounterLevel, dropTable, BeastProgression.MaxLevel, rng);
        }

        /// <summary>
        /// Pays <paramref name="result"/> out into <paramref name="save"/>: practice XP to every team
        /// beast's and the avatar's skills that fired, drops for a clear of
        /// (<paramref name="shape"/>, <paramref name="encounterLevel"/>) from
        /// <paramref name="dropTable"/> into <see cref="PlayerSave.Materials"/> (only on a player
        /// victory; a null table drops nothing), beast XP to every team beast
        /// (<see cref="BeastProgression.AwardBattle"/>; a beast knocked out by the end gets
        /// participation only), bench XP to every other beast in the save
        /// (<see cref="BeastProgression.AwardBench"/>) and avatar XP for the battle at
        /// <paramref name="encounterLevel"/> when the avatar took part — every XP award after the
        /// level-gap falloff on the earner's level before the award (<see cref="LevelGapXp"/>), and
        /// beast XP added under <paramref name="beastLevelCap"/> (<see cref="LevelCap"/>: a beast at
        /// the cap banks instead of levelling; <see cref="BeastProgression.MaxLevel"/> is no cap; the
        /// campaign passes <c>Campaign.LevelCaps.BeastCap</c>). <paramref name="content"/>
        /// supplies each skill's and passive's progression definition (null uses the defaults).
        /// <paramref name="rng"/> drives the drop rolls; null seeds one from the battle's seed
        /// (<see cref="LootRoller.DeriveSeed"/>), so rewards are deterministic either way.
        /// <para>
        /// Refused — nothing changes — for a null save or result, a failed battle, or a result
        /// already paid out. A team beast no longer in the save is skipped.
        /// </para>
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, string shape, int encounterLevel,
                                                       DropTable dropTable, int beastLevelCap, Random rng = null)
        {
            return ApplyRewards(save, result, content, shape, encounterLevel, dropTable, beastLevelCap, null, rng);
        }

        /// <summary>
        /// <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, string, int, DropTable, int, Random)"/>
        /// with the caller's economy <paramref name="modifiers"/> (null = <see cref="RewardModifiers.None"/>).
        /// On a clear the battle also pays gold (<see cref="PostBattleAward.AwardGold"/>: the drop
        /// table's <c>Gold</c> with the first-clear bonus when the material roll was a first clear,
        /// then the modifiers' multiplier and bonus) into the <see cref="Wallet"/>, on its own seed
        /// stream (<c>LootRoller.DeriveSeed(result.Seed, PostBattleAward.GoldStream)</c>), so the
        /// material rolls are exactly those of a gold-free table. With <see cref="RewardModifiers.Gear"/>
        /// a clear also rolls the table's gear drops (<see cref="GearDrops.Roll"/>, stream
        /// <see cref="PostBattleAward.GearStream"/>), each granted as a new instance.
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, string shape, int encounterLevel,
                                                       DropTable dropTable, int beastLevelCap, RewardModifiers modifiers, Random rng = null)
        {
            BattleRewardSummary summary = new BattleRewardSummary();
            RewardModifiers mods = modifiers ?? RewardModifiers.None;

            if (save == null || result == null)
            {
                summary.Error = "No save or no battle result.";
                return summary;
            }

            if (!result.Success)
            {
                summary.Error = "The battle did not run: " + result.Error;
                return summary;
            }

            if (result.RewardsApplied)
            {
                summary.Error = "This battle's rewards were already applied.";
                return summary;
            }

            save.EnsureInitialized();
            summary.Outcome = result.Outcome;
            summary.BeastLevelCap = beastLevelCap < 1 ? 1 : beastLevelCap > BeastProgression.MaxLevel ? BeastProgression.MaxLevel : beastLevelCap;

            Dictionary<string, BeastSkillBook> books = new Dictionary<string, BeastSkillBook>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
            {
                OwnedBeast beast = save.FindBeast(pair.Key);

                if (beast != null && !books.ContainsKey(pair.Value))
                {
                    books.Add(pair.Value, beast.Skills);
                }
            }

            Func<string, SkillSO> skillLookup = content == null ? null : new Func<string, SkillSO>(content.GetSkill);
            Func<string, BeastCraft.Avatar.PassiveSkillSO> passiveLookup = content == null ? null : new Func<string, BeastCraft.Avatar.PassiveSkillSO>(content.GetPassive);
            AvatarSkillBook avatarBook = result.Avatar == null ? null : save.AvatarSkills;

            summary.SkillLevelsGained = PostBattleAward.AwardPractice(result.Battle, books, skillLookup, avatarBook, skillLookup, passiveLookup);
            summary.Loot = PostBattleAward.AwardDrops(result.Battle, dropTable, shape, encounterLevel, save.Materials,
                                                      rng ?? new Random(LootRoller.DeriveSeed(result.Seed, 0)));
            if (result.Outcome == BattleOutcome.PlayerVictory && dropTable != null && dropTable.Gold.PaysGold)
            {
                int gold = PostBattleAward.AwardGold(result.Battle, dropTable, shape, encounterLevel, summary.Loot.FirstClear, mods.GoldMultiplier, mods.BonusGold,
                                                     new Random(LootRoller.DeriveSeed(result.Seed, PostBattleAward.GoldStream)));
                summary.GoldGained = Wallet.Add(save, gold);
            }

            foreach (string id in result.ConsumablesUsed)
            {
                if (ConsumableInventory.TryRemove(save, id, 1))
                {
                    summary.ConsumablesSpent.Add(id);
                }
            }

            if (result.Outcome == BattleOutcome.PlayerVictory && mods.Gear != null)
            {
                foreach (GearItem item in GearDrops.Roll(dropTable, mods.Gear, shape, encounterLevel, new Random(LootRoller.DeriveSeed(result.Seed, PostBattleAward.GearStream))))
                {
                    GearDrops.Grant(save, item);
                    summary.GearGained.Add(item.GearId);
                }
            }

            foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
            {
                OwnedBeast beast = save.FindBeast(pair.Key);
                BattleUnit unit = FindUnit(result.Units, pair.Value);

                if (beast == null || summary.BeastXpGained.ContainsKey(pair.Key))
                {
                    continue;
                }

                bool knockedOut = unit != null && unit.IsDefeated;
                int bankBefore = beast.Progress.BankedXp;
                summary.FalloffPercent[pair.Key] = LevelGapXp.Percent(LevelGapXp.Gap(beast.Progress.Level, encounterLevel));
                summary.BeastXpGained[pair.Key] = BeastProgression.BattleXp(result.Outcome, encounterLevel, knockedOut, beast.Progress.Level);
                summary.BeastLevelsGained += BeastProgression.AwardBattle(beast.Progress, result.Outcome, encounterLevel, knockedOut, summary.BeastLevelCap);
                NoteBanked(summary, pair.Key, bankBefore, beast.Progress.BankedXp);
            }

            foreach (OwnedBeast beast in save.Beasts)
            {
                if (string.IsNullOrEmpty(beast.BeastId) || summary.BeastXpGained.ContainsKey(beast.BeastId) || summary.BenchXpGained.ContainsKey(beast.BeastId))
                {
                    continue;
                }

                int bankBefore = beast.Progress.BankedXp;
                summary.FalloffPercent[beast.BeastId] = LevelGapXp.Percent(LevelGapXp.Gap(beast.Progress.Level, encounterLevel));
                summary.BenchXpGained[beast.BeastId] = BeastProgression.BenchXp(result.Outcome, encounterLevel, beast.Progress.Level);
                summary.BenchLevelsGained += BeastProgression.AwardBench(beast.Progress, result.Outcome, encounterLevel, summary.BeastLevelCap);
                NoteBanked(summary, beast.BeastId, bankBefore, beast.Progress.BankedXp);
            }

            if (result.Avatar != null)
            {
                summary.AvatarFalloffPercent = LevelGapXp.Percent(LevelGapXp.Gap(save.Avatar.Level, encounterLevel));
                summary.AvatarXpGained = AvatarProgression.BattleXp(result.Outcome, encounterLevel, save.Avatar.Level);
                summary.AvatarLevelsGained = AvatarProgression.AwardBattle(save.Avatar, result.Outcome, encounterLevel);
            }

            summary.Applied = true;
            result.RewardsApplied = true;
            return summary;
        }

        /// <summary>
        /// <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, string, int, DropTable, int, Random)"/>
        /// for the shape and level the battle's <see cref="EncounterSetup"/> named, with no beast
        /// level cap. See <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, DropTable, int, Random)"/>.
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, DropTable dropTable, Random rng = null)
        {
            return ApplyRewards(save, result, content, dropTable, BeastProgression.MaxLevel, rng);
        }

        /// <summary>
        /// <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, string, int, DropTable, int, Random)"/>
        /// for the shape and level the battle's <see cref="EncounterSetup"/> named
        /// (<see cref="BattleSessionResult.ShapeId"/>, <see cref="BattleSessionResult.EncounterLevel"/>;
        /// <c>EncounterPlan.ToSetup</c> sets both), under <paramref name="beastLevelCap"/>. Refused,
        /// changing nothing, when the setup named no shape or no level.
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, DropTable dropTable, int beastLevelCap,
                                                       Random rng = null)
        {
            return ApplyRewards(save, result, content, dropTable, beastLevelCap, null, rng);
        }

        /// <summary>
        /// <see cref="ApplyRewards(PlayerSave, BattleSessionResult, BattleContent, DropTable, int, Random)"/>
        /// with the caller's economy <paramref name="modifiers"/> (the campaign passes
        /// <c>CampaignRules.RewardModifiersFor(node)</c>).
        /// </summary>
        public static BattleRewardSummary ApplyRewards(PlayerSave save, BattleSessionResult result, BattleContent content, DropTable dropTable, int beastLevelCap,
                                                       RewardModifiers modifiers, Random rng = null)
        {
            if (result != null && result.Success && (string.IsNullOrEmpty(result.ShapeId) || result.EncounterLevel < 1))
            {
                return new BattleRewardSummary
                {
                    Error = "The battle's encounter named no shape or level (EncounterSetup.ShapeId / EncounterLevel); pass them explicitly."
                };
            }

            return ApplyRewards(save, result, content, result == null ? null : result.ShapeId, result == null ? 0 : result.EncounterLevel, dropTable, beastLevelCap, modifiers,
                                rng);
        }

        private static void NoteBanked(BattleRewardSummary summary, string beastId, int before, int after)
        {
            if (after > before)
            {
                summary.XpBanked[beastId] = after - before;
            }
        }

        private static BattleUnit FindUnit(IReadOnlyList<BattleUnit> units, string unitId)
        {
            foreach (BattleUnit unit in units)
            {
                if (unit != null && string.Equals(unit.Id, unitId, StringComparison.Ordinal))
                {
                    return unit;
                }
            }

            return null;
        }

        private static List<OwnedBeast> ResolveTeam(BattleSetup setup, PlayerSave save, BattleContent content, List<string> errors)
        {
            List<OwnedBeast> team = new List<OwnedBeast>();
            List<string> ids = setup.TeamBeastIds;

            if (ids == null || ids.Count == 0)
            {
                errors.Add("The team is empty.");
                return team;
            }

            int maxParty = BattleFormat.LargeGroup.MaxPartySize();
            if (ids.Count > maxParty)
            {
                errors.Add("The team has " + ids.Count + " beasts; at most " + maxParty + " can deploy.");
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (string id in ids)
            {
                if (string.IsNullOrEmpty(id))
                {
                    errors.Add("The team names an empty beast id.");
                    continue;
                }

                if (!seen.Add(id))
                {
                    errors.Add("Beast '" + id + "' is in the team twice.");
                    continue;
                }

                OwnedBeast beast = save.FindBeast(id);

                if (beast == null)
                {
                    errors.Add("Beast '" + id + "' is not in the save.");
                    continue;
                }

                string speciesId = beast.Progress == null ? null : beast.Progress.SpeciesId;
                CreatureSpeciesSO species = content.GetSpecies(speciesId);

                if (species == null)
                {
                    errors.Add("Beast '" + id + "' is of unknown species '" + speciesId + "'.");
                    continue;
                }

                if (species.Footprint != UnitFootprint.Single)
                {
                    errors.Add("Beast '" + id + "' (" + speciesId + ") is not single-tile; the team can only deploy single-tile beasts.");
                    continue;
                }

                CheckEquipped(beast.Skills, "Beast '" + id + "'", "skill", skillId => content.GetSkill(skillId) != null, errors);
                ResolveBeastGear(save, beast, content, errors);
                team.Add(beast);
            }

            return team;
        }

        private static void CheckEquipped(SkillBook book, string owner, string kind, Func<string, bool> resolves, List<string> errors)
        {
            if (book == null)
            {
                return;
            }

            for (int slot = 0; slot < book.SlotCount; slot++)
            {
                string id = book.GetEquipped(slot);

                if (id != null && !resolves(id))
                {
                    errors.Add(owner + " has unknown " + kind + " '" + id + "' equipped in slot " + slot + ".");
                }
            }
        }

        private sealed class ResolvedEnemy
        {
            public EnemySpec Spec;
            public string UnitId;
            public CreatureSpeciesSO Species;
            public List<SkillSO> Skills;
        }

        private static List<ResolvedEnemy> ResolveEnemies(EncounterSetup encounter, BattleContent content, List<string> errors)
        {
            List<ResolvedEnemy> enemies = new List<ResolvedEnemy>();
            int specCount = encounter.Enemies == null ? 0 : encounter.Enemies.Count;
            int prebuiltCount = encounter.PrebuiltEnemies == null ? 0 : encounter.PrebuiltEnemies.Count;

            if (specCount + prebuiltCount == 0)
            {
                errors.Add("The encounter has no enemies.");
                return enemies;
            }

            for (int i = 0; i < specCount; i++)
            {
                EnemySpec spec = encounter.Enemies[i];
                string label = "Enemy " + (i + 1);

                if (spec == null)
                {
                    errors.Add(label + " is missing.");
                    continue;
                }

                CreatureSpeciesSO species = content.GetSpecies(spec.SpeciesId);
                bool libraryEnemy = false;

                if (species == null && content.Enemies != null)
                {
                    species = content.Enemies.Species(spec.SpeciesId, spec.Element ?? Element.None);
                    libraryEnemy = species != null;
                }

                if (species == null)
                {
                    errors.Add(label + " is of unknown species '" + spec.SpeciesId + "'.");
                    continue;
                }

                if (spec.Element.HasValue && !libraryEnemy)
                {
                    errors.Add(label + " (" + spec.SpeciesId + ") is a roster species; Element only applies to enemy-library enemies.");
                }

                if (!(spec.StatMultiplier > 0.0) || double.IsInfinity(spec.StatMultiplier))
                {
                    errors.Add(label + " (" + spec.SpeciesId + ") has StatMultiplier " + spec.StatMultiplier + "; it must be a positive number.");
                }

                List<SkillSO> skills = new List<SkillSO>();

                if (spec.SkillIds == null && libraryEnemy)
                {
                    foreach (SkillSO skill in content.Enemies.Kit(spec.SpeciesId, spec.Element ?? Element.None))
                    {
                        skills.Add(skill);
                    }
                }
                else if (spec.SkillIds == null)
                {
                    if (species.DefaultLoadout != null)
                    {
                        foreach (SkillSO skill in species.DefaultLoadout)
                        {
                            if (skill != null)
                            {
                                skills.Add(skill);
                            }
                        }
                    }
                }
                else
                {
                    foreach (string skillId in spec.SkillIds)
                    {
                        SkillSO skill = content.GetSkill(skillId);

                        if (skill == null)
                        {
                            errors.Add(label + " (" + spec.SpeciesId + ") has unknown skill '" + skillId + "'.");
                        }
                        else
                        {
                            skills.Add(skill);
                        }
                    }
                }

                enemies.Add(new ResolvedEnemy
                {
                    Spec = spec,
                    UnitId = string.IsNullOrEmpty(spec.UnitId) ? "enemy" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : spec.UnitId,
                    Species = species,
                    Skills = skills
                });
            }

            for (int i = 0; i < prebuiltCount; i++)
            {
                BattleUnit unit = encounter.PrebuiltEnemies[i];

                if (unit == null)
                {
                    errors.Add("Prebuilt enemy " + (i + 1) + " is missing.");
                }
                else if (unit.Team != BattleTeam.Enemy)
                {
                    errors.Add("Prebuilt enemy '" + unit.Id + "' is not on the enemy team.");
                }
            }

            return enemies;
        }

        private static void CheckUnitIds(List<OwnedBeast> team, List<ResolvedEnemy> enemies, EncounterSetup encounter, bool includeAvatar, List<string> errors)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            if (includeAvatar)
            {
                ids.Add(BattleAvatar.DefaultId);
            }

            foreach (OwnedBeast beast in team)
            {
                ids.Add(BeastUnitIdPrefix + beast.BeastId);
            }

            foreach (ResolvedEnemy enemy in enemies)
            {
                if (!ids.Add(enemy.UnitId))
                {
                    errors.Add("Unit id '" + enemy.UnitId + "' is used twice.");
                }
            }

            if (encounter.PrebuiltEnemies == null)
            {
                return;
            }

            foreach (BattleUnit unit in encounter.PrebuiltEnemies)
            {
                if (unit != null && (string.IsNullOrEmpty(unit.Id) || !ids.Add(unit.Id)))
                {
                    errors.Add("Prebuilt enemy unit id '" + unit.Id + "' is empty or used twice.");
                }
            }
        }

        private static bool PlaceEnemies(HexGrid grid, EncounterSetup encounter, List<ResolvedEnemy> enemies, List<BattleUnit> units, List<string> errors)
        {
            // Explicit positions and prebuilt units first, so auto-placement packs around them.
            if (encounter.PrebuiltEnemies != null)
            {
                foreach (BattleUnit unit in encounter.PrebuiltEnemies)
                {
                    if (!grid.TryPlaceUnit(unit.Id, unit.Position, unit.Footprint))
                    {
                        errors.Add("Prebuilt enemy '" + unit.Id + "' cannot stand at " + unit.Position + ".");
                    }
                }
            }

            HexCoordinate[] anchors = new HexCoordinate[enemies.Count];
            List<int> auto = new List<int>();

            for (int i = 0; i < enemies.Count; i++)
            {
                ResolvedEnemy enemy = enemies[i];

                if (!enemy.Spec.Position.HasValue)
                {
                    auto.Add(i);
                    continue;
                }

                HexCoordinate anchor = enemy.Spec.Position.Value;

                if (!grid.FitsDeploymentZone(anchor, enemy.Species.Footprint, BattleTeam.Enemy) || !grid.TryPlaceUnit(enemy.UnitId, anchor, enemy.Species.Footprint))
                {
                    errors.Add("Enemy '" + enemy.UnitId + "' cannot stand at " + anchor + " (outside the enemy zone or taken).");
                }

                anchors[i] = anchor;
            }

            if (errors.Count > 0)
            {
                return false;
            }

            if (auto.Count > 0)
            {
                List<UnitFootprint> footprints = new List<UnitFootprint>();
                foreach (int i in auto)
                {
                    footprints.Add(enemies[i].Species.Footprint);
                }

                List<HexCoordinate> packed = new List<HexCoordinate>();
                if (!DeploymentPacker.TryPack(grid, BattleTeam.Enemy, footprints, packed, new List<HexCoordinate>()))
                {
                    errors.Add("The enemies do not fit the " + grid.Size + " arena's enemy deployment zone.");
                    return false;
                }

                for (int k = 0; k < auto.Count; k++)
                {
                    ResolvedEnemy enemy = enemies[auto[k]];
                    anchors[auto[k]] = packed[k];
                    grid.TryPlaceUnit(enemy.UnitId, packed[k], enemy.Species.Footprint);
                }
            }

            for (int i = 0; i < enemies.Count; i++)
            {
                ResolvedEnemy enemy = enemies[i];
                BattleUnit unit = BattleUnitFactory.CreateBeast(enemy.UnitId, BattleTeam.Enemy, enemy.Species, enemy.Spec.Level, null, anchors[i],
                                                                new SkillLoadout(enemy.Skills), enemy.Spec.StatusResist);

                // The difficulty multiplier, exactly as the balance simulator fields a calibrated
                // enemy: scale the level-computed stats, then start at full (scaled) HP.
                if (enemy.Spec.StatMultiplier != 1.0)
                {
                    unit.Stats = EnemyScaling.Scale(unit.Stats, enemy.Spec.StatMultiplier);
                    unit.CurrentHp = unit.Stats.Hp;
                }

                units.Add(unit);
            }

            if (encounter.PrebuiltEnemies != null)
            {
                units.AddRange(encounter.PrebuiltEnemies);
            }

            return true;
        }

        /// <summary>
        /// The gear <paramref name="beast"/> wears, resolved to definitions. Every problem — an
        /// instance not owned, worn by another beast too, an unknown gear id, a gear in the wrong
        /// slot — is an error, like an unknown equipped skill. Gear below the beast's level is not
        /// an error: <see cref="StatCalculator"/> ignores it.
        /// </summary>
        private static List<GearSO> ResolveBeastGear(PlayerSave save, OwnedBeast beast, BattleContent content, List<string> errors)
        {
            List<GearSO> gear = new List<GearSO>();

            for (int slot = 0; beast.EquippedGear != null && slot < beast.EquippedGear.Length; slot++)
            {
                string instanceId = GearRules.GetSlot(beast.EquippedGear, slot);
                if (instanceId == null)
                {
                    continue;
                }

                string owner = "Beast '" + beast.BeastId + "'";
                OwnedGear owned = save.Gear == null ? null : save.Gear.FindBeastGear(instanceId);
                GearSO definition = owned == null ? null : content.GetGear(owned.GearId);

                if (owned == null)
                {
                    errors.Add(owner + " wears gear instance '" + instanceId + "', which is not in the inventory.");
                }
                else if (definition == null)
                {
                    errors.Add(owner + " wears unknown gear '" + owned.GearId + "'.");
                }
                else if (slot >= GearRules.BeastSlotCount || (int)definition.Slot != slot)
                {
                    errors.Add(owner + " wears '" + owned.GearId + "' in slot " + slot + "; it belongs in " + definition.Slot + ".");
                }
                else if (GearRules.CountBeastGearWorn(save, instanceId) != 1)
                {
                    errors.Add(owner + " wears gear instance '" + instanceId + "', which is worn more than once.");
                }
                else
                {
                    gear.Add(definition);
                }
            }

            return gear;
        }

        /// <summary>The gear the save's avatar wears, resolved to definitions; problems are errors as for a beast.</summary>
        private static List<AvatarGearSO> ResolveAvatarGear(PlayerSave save, BattleContent content, List<string> errors)
        {
            List<AvatarGearSO> gear = new List<AvatarGearSO>();

            for (int slot = 0; save.AvatarEquippedGear != null && slot < save.AvatarEquippedGear.Length; slot++)
            {
                string instanceId = GearRules.GetSlot(save.AvatarEquippedGear, slot);
                if (instanceId == null)
                {
                    continue;
                }

                OwnedGear owned = save.Gear == null ? null : save.Gear.FindAvatarGear(instanceId);
                AvatarGearSO definition = owned == null ? null : content.GetAvatarGear(owned.GearId);

                if (owned == null)
                {
                    errors.Add("The avatar wears gear instance '" + instanceId + "', which is not in the inventory.");
                }
                else if (definition == null)
                {
                    errors.Add("The avatar wears unknown gear '" + owned.GearId + "'.");
                }
                else if (slot >= GearRules.AvatarSlotCount || (int)definition.Slot != slot)
                {
                    errors.Add("The avatar wears '" + owned.GearId + "' in slot " + slot + "; it belongs in " + definition.Slot + ".");
                }
                else if (Array.IndexOf(save.AvatarEquippedGear, instanceId) != slot)
                {
                    errors.Add("The avatar wears gear instance '" + instanceId + "' more than once.");
                }
                else
                {
                    gear.Add(definition);
                }
            }

            return gear;
        }

        private static List<BattleUnit> PlaceTeam(HexGrid grid, List<OwnedBeast> team, PlayerSave save, BattleContent content, List<string> errors)
        {
            List<HexCoordinate> front = new List<HexCoordinate>();
            foreach (HexCoordinate tile in DeploymentPacker.FrontOrder(grid, BattleTeam.Player))
            {
                if (!grid.IsOccupied(tile) && !grid.IsBlocked(tile))
                {
                    front.Add(tile);
                }
            }

            if (front.Count < team.Count)
            {
                errors.Add(team.Count + " beasts do not fit the " + grid.Size + " arena's player deployment zone (" + front.Count + " tiles free).");
                return null;
            }

            List<BattleUnit> members = new List<BattleUnit>();
            List<PlacementRequest> requests = new List<PlacementRequest>();

            for (int i = 0; i < team.Count; i++)
            {
                OwnedBeast beast = team[i];
                string unitId = BeastUnitIdPrefix + beast.BeastId;
                List<GearSO> gear = ResolveBeastGear(save, beast, content, new List<string>());
                members.Add(BattleUnitFactory.CreateBeast(unitId, BattleTeam.Player, content.GetSpecies(beast.Progress.SpeciesId), beast.Progress.Level, gear,
                                                          front[i], beast.Skills, content.GetSkill));
                requests.Add(new PlacementRequest(unitId, front[i]));
            }

            if (!PlacementValidator.TryPlaceAll(grid, BattleTeam.Player, FormatFor(team.Count), requests, null, out PlacementValidationResult placement))
            {
                foreach (PlacementOutcome outcome in placement.InvalidOutcomes)
                {
                    errors.Add("Beast unit '" + outcome.UnitId + "' cannot deploy at " + outcome.Position + ": " + outcome.Status + ".");
                }

                if (placement.CountStatus != PlacementCountStatus.Valid)
                {
                    errors.Add("The team size is not allowed: " + placement.CountStatus + ".");
                }

                return null;
            }

            return members;
        }

        private static BattleFormat FormatFor(int teamSize)
        {
            if (teamSize <= BattleFormat.Solo.MaxPartySize())
            {
                return BattleFormat.Solo;
            }

            return teamSize <= BattleFormat.SmallGroup.MaxPartySize() ? BattleFormat.SmallGroup : BattleFormat.LargeGroup;
        }
    }
}
