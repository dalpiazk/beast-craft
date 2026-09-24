using System;
using System.Collections.Generic;
using System.Text;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="BattleSession"/> end to end over the authored content: a starter
    /// <see cref="PlayerSave"/> built from the roster and skill library fights a small enemy group,
    /// deterministically per seed; rewards land in the save (practice XP, first-clear drops, avatar
    /// XP) and the save still round-trips; a bad setup is a clear error, never an exception.
    /// </summary>
    public class BattleSessionTests
    {
        private const string Shape = "squad";
        private const int EncounterLevel = 8;

        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();
        private BeastRosterData _roster;
        private SkillLibraryData _library;
        private BattleContent _content;
        private DropTable _drops;
        private AvatarStatsSO _profile;

        [SetUp]
        public void SetUp()
        {
            _roster = BeastRosterTests.LoadRoster();
            _library = SkillLibraryTests.LoadLibrary();
            _content = BuildContent(_roster, _library);
            _drops = DropTableBuilder.Build(DropTableTests.LoadTables(), DropTableBuilder.TierLookup(_library.Materials));

            GrowthRateCurve growth = Create<GrowthRateCurve>();
            growth.Curve = AnimationCurve.Linear(0f, 0.2f, 1f, 1f);
            growth.MaxLevel = 100;
            _profile = Create<AvatarStatsSO>();
            _profile.BaseStats = new StatBlock(500, 100, 80, 120, 90, 100, 3, 5);
            _profile.Growth = growth;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Run_IsDeterministic_PerSeed()
        {
            BattleSessionResult first = BattleSession.Run(Setup(StarterSave(), 1234));
            BattleSessionResult second = BattleSession.Run(Setup(StarterSave(), 1234));

            Assert.IsTrue(first.Success, first.Error);
            Assert.IsTrue(second.Success, second.Error);
            Assert.AreEqual(Trace(first), Trace(second));
            Assert.Greater(first.Battle.ActionCount, 0);
            Assert.AreNotEqual(BattleOutcome.Stalemate, first.Outcome);

            bool anyDifferent = false;
            for (int seed = 1; seed <= 5 && !anyDifferent; seed++)
            {
                anyDifferent = Trace(BattleSession.Run(Setup(StarterSave(), 1234 + seed))) != Trace(first);
            }

            Assert.IsTrue(anyDifferent, "the seed drives the battle");
        }

        [Test]
        public void Run_BuildsTheTeamFromTheSave()
        {
            PlayerSave save = StarterSave();

            BattleSessionResult result = BattleSession.Run(Setup(save, 7));

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(4, result.TeamUnitIds.Count);
            Assert.AreEqual(2 + 4, result.Units.Count, "enemies then the team; the avatar is not in the roster");
            Assert.IsNotNull(result.Avatar);

            for (int i = 0; i < 4; i++)
            {
                OwnedBeast beast = save.Beasts[i];
                string unitId = result.UnitIdFor(beast.BeastId);
                BattleUnit unit = FindUnit(result, unitId);

                Assert.AreEqual(BattleSession.BeastUnitIdPrefix + beast.BeastId, unitId);
                Assert.AreEqual(BattleTeam.Player, unit.Team);
                Assert.AreEqual(beast.Progress.Level, unit.Level);
                Assert.AreEqual(BeastSkillBook.EquipSlotCount, unit.Skills.Count);
                Assert.AreEqual(beast.Skills.GetEquipped(0), unit.Skills.Skills[0].SkillId, "slot order is fire priority");
                Assert.IsTrue(result.SkillUsesByBeastId.ContainsKey(beast.BeastId));
            }

            Assert.AreEqual(save.Avatar.Level, result.Avatar.Level);
            Assert.AreEqual(AvatarSkillBook.ActiveSlotCount, result.Avatar.Skills.Count);
            Assert.AreEqual(BattleSkillUsage.CountPassiveTriggers(result.Battle).Count, result.PassiveTriggers.Count);
        }

        [Test]
        public void Run_ResolvesTeamBonds()
        {
            PlayerSave save = StarterSave();
            List<TeamBondMember> members = new List<TeamBondMember>();
            foreach (OwnedBeast beast in save.Beasts)
            {
                members.Add(TeamBondMember.FromSpecies(_content.GetSpecies(beast.Progress.SpeciesId)));
            }

            BattleSessionResult result = BattleSession.Run(Setup(save, 7));

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(TeamBondResolver.Resolve(_content.TeamBonds, members).Count, result.ActiveBonds.Count);
            Assert.AreEqual(result.ActiveBonds.Count == 0, result.Battle.BondActivations.Count == 0);
        }

        [Test]
        public void ApplyRewards_PaysPracticeLootAndAvatarXp_OnceOnly()
        {
            PlayerSave save = StarterSave();
            OwnedBeast benched = OwnedBeast.Create("bench", _roster.Species[0].SpeciesId, 10);
            save.Beasts.Add(benched);
            BattleSetup setup = Setup(save, 42, weakEnemies: true);
            setup.TeamBeastIds.Remove(benched.BeastId);
            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.PlayerVictory, result.Outcome, result.Error);
            int avatarXpBefore = AvatarProgression.TotalXpToReach(save.Avatar.Level) + save.Avatar.Xp;
            int[] beastXpBefore = new int[4];
            for (int i = 0; i < 4; i++)
            {
                beastXpBefore[i] = BeastProgression.TotalXpToReach(save.Beasts[i].Progress.Level) + save.Beasts[i].Progress.Xp;
            }

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops, new System.Random(99));

            Assert.IsTrue(summary.Applied, summary.Error);
            Assert.AreEqual(BattleOutcome.PlayerVictory, summary.Outcome);

            int fired = 0;
            foreach (KeyValuePair<string, Dictionary<string, int>> beastUses in result.SkillUsesByBeastId)
            {
                foreach (KeyValuePair<string, int> use in beastUses.Value)
                {
                    SkillProgress progress = save.FindBeast(beastUses.Key).Skills.GetProgress(use.Key);
                    Assert.IsTrue(progress.Level > 1 || progress.Xp > 0, beastUses.Key + " practised " + use.Key);
                    fired++;
                }
            }

            Assert.Greater(fired, 0, "the team fired something");

            Assert.IsTrue(summary.Loot.FirstClear);
            Assert.IsTrue(save.Materials.HasCleared(Shape, _drops.BandForLevel(EncounterLevel).MinLevel));
            foreach (MaterialStack drop in summary.Loot.Drops)
            {
                Assert.GreaterOrEqual(save.Materials.GetCount(drop.MaterialId), drop.Quantity);
            }

            Assert.AreEqual(AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, EncounterLevel), summary.AvatarXpGained);

            Assert.AreEqual(4, summary.BeastXpGained.Count, "every fielded beast is paid");
            for (int i = 0; i < 4; i++)
            {
                OwnedBeast beast = save.Beasts[i];
                bool knockedOut = FindUnit(result, result.UnitIdFor(beast.BeastId)).IsDefeated;
                int expected = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, EncounterLevel, knockedOut);
                Assert.AreEqual(expected, summary.BeastXpGained[beast.BeastId]);
                Assert.AreEqual(beastXpBefore[i] + expected, BeastProgression.TotalXpToReach(beast.Progress.Level) + beast.Progress.Xp);
            }

            Assert.IsFalse(summary.BeastXpGained.ContainsKey(benched.BeastId), "benched beasts earn nothing");
            Assert.AreEqual(10, benched.Progress.Level);
            Assert.AreEqual(0, benched.Progress.Xp);
            Assert.AreEqual(avatarXpBefore + summary.AvatarXpGained, AvatarProgression.TotalXpToReach(save.Avatar.Level) + save.Avatar.Xp);

            BattleRewardSummary again = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops, new System.Random(99));
            Assert.IsFalse(again.Applied);
            StringAssert.Contains("already", again.Error);
        }

        [Test]
        public void ApplyRewards_IsDeterministic_AndTheSaveRoundTrips()
        {
            SaveSerializer serializer = new SaveSerializer(new JsonUtilitySaveSerializer(), SaveContentCatalog.FromData(_roster, _library));
            string[] written = new string[2];

            for (int run = 0; run < 2; run++)
            {
                PlayerSave save = StarterSave();
                BattleSessionResult result = BattleSession.Run(Setup(save, 5, weakEnemies: true));
                Assert.IsTrue(BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops).Applied);
                written[run] = serializer.Serialize(save);
            }

            Assert.AreEqual(written[0], written[1], "same seed, same rewards");

            SaveLoadResult loaded = serializer.Deserialize(written[0]);
            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual(written[0], serializer.Serialize(loaded.Save));
        }

        [Test]
        public void ApplyRewards_OnADefeat_PaysNoDrops()
        {
            PlayerSave save = StarterSave();
            BattleSetup setup = Setup(save, 3);
            setup.TeamBeastIds = new List<string> { save.Beasts[0].BeastId };
            setup.IncludeAvatar = false;
            setup.Encounter.Enemies = new List<EnemySpec>();
            for (int i = 0; i < 4; i++)
            {
                setup.Encounter.Enemies.Add(new EnemySpec(_library.SpeciesKits[4 + i].SpeciesId, 60));
            }

            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.EnemyVictory, result.Outcome, result.Error);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops);

            Assert.IsTrue(summary.Applied);
            Assert.IsEmpty(summary.Loot.Drops);
            Assert.IsEmpty(save.Materials.ClearedCells);
            Assert.AreEqual(0, summary.AvatarXpGained, "the avatar did not take part");
            Assert.AreEqual(BeastProgression.ParticipationXp, summary.BeastXpGained[save.Beasts[0].BeastId], "a lost battle pays participation only");
            Assert.AreEqual(BeastProgression.ParticipationXp, save.Beasts[0].Progress.Xp);
            Assert.IsNull(result.Avatar);
        }

        [Test]
        public void Run_PlacesExplicitlyPositionedEnemies()
        {
            BattleSetup setup = Setup(StarterSave(), 11);
            HexCoordinate tile = new HexGrid(setup.Encounter.Arena).GetDeploymentZone(BattleTeam.Enemy)[0];
            setup.Encounter.Enemies[1].Position = tile;
            setup.Encounter.Enemies[1].UnitId = "boss";

            BattleSessionResult result = BattleSession.Run(setup);

            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(FindUnit(result, "boss"));
            Assert.AreEqual(tile, StartOf(result, "boss"), "the boss starts where it was put");
        }

        [Test]
        public void Run_BadSetups_ReturnClearErrors()
        {
            AssertFails(null, "No battle setup");

            BattleSetup noSave = Setup(StarterSave(), 1);
            noSave.Save = null;
            AssertFails(noSave, "no save");

            BattleSetup empty = Setup(StarterSave(), 1);
            empty.TeamBeastIds.Clear();
            AssertFails(empty, "team is empty");

            BattleSetup unknownBeast = Setup(StarterSave(), 1);
            unknownBeast.TeamBeastIds.Add("nobody");
            AssertFails(unknownBeast, "'nobody' is not in the save");

            BattleSetup twice = Setup(StarterSave(), 1);
            twice.TeamBeastIds.Add(twice.TeamBeastIds[0]);
            AssertFails(twice, "in the team twice");

            BattleSetup tooMany = Setup(StarterSave(), 1);
            for (int i = 0; i < 3; i++)
            {
                OwnedBeast extra = OwnedBeast.Create("extra" + i, _roster.Species[0].SpeciesId, 5);
                tooMany.Save.Beasts.Add(extra);
                tooMany.TeamBeastIds.Add(extra.BeastId);
            }

            AssertFails(tooMany, "at most 6");

            BattleSetup unknownSpecies = Setup(StarterSave(), 1);
            unknownSpecies.Save.Beasts[0].Progress.SpeciesId = "gone_species";
            AssertFails(unknownSpecies, "unknown species 'gone_species'");

            BattleSetup unknownSkill = Setup(StarterSave(), 1);
            BeastSkillBook book = unknownSkill.Save.Beasts[1].Skills;
            book.Learn("ghost_skill");
            book.Unequip(0);
            book.Equip(0, "ghost_skill");
            AssertFails(unknownSkill, "unknown skill 'ghost_skill'");

            BattleSetup unknownPassive = Setup(StarterSave(), 1);
            unknownPassive.Save.AvatarSkills.Passives.Learn("ghost_passive");
            unknownPassive.Save.AvatarSkills.Passives.Unequip(0);
            unknownPassive.Save.AvatarSkills.Passives.Equip(0, "ghost_passive");
            AssertFails(unknownPassive, "unknown passive 'ghost_passive'");

            BattleSetup noEnemies = Setup(StarterSave(), 1);
            noEnemies.Encounter.Enemies.Clear();
            AssertFails(noEnemies, "no enemies");

            BattleSetup unknownEnemy = Setup(StarterSave(), 1);
            unknownEnemy.Encounter.Enemies[0].SpeciesId = "mystery";
            unknownEnemy.Encounter.Enemies[1].SkillIds = new List<string> { "nope" };
            BattleSessionResult both = AssertFails(unknownEnemy, "unknown species 'mystery'");
            StringAssert.Contains("unknown skill 'nope'", both.Error, "every problem is reported at once");

            BattleSetup badTile = Setup(StarterSave(), 1);
            badTile.Encounter.Enemies[0].Position = new HexGrid(badTile.Encounter.Arena).GetDeploymentZone(BattleTeam.Player)[0];
            AssertFails(badTile, "cannot stand at");

            BattleSetup clash = Setup(StarterSave(), 1);
            clash.Encounter.Enemies[0].UnitId = BattleAvatar.DefaultId;
            AssertFails(clash, "used twice");
        }

        [Test]
        public void ApplyRewards_RefusesAFailedBattle()
        {
            PlayerSave save = StarterSave();
            BattleSetup setup = Setup(save, 1);
            setup.TeamBeastIds.Clear();
            BattleSessionResult failed = BattleSession.Run(setup);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, failed, _content, Shape, EncounterLevel, _drops);

            Assert.IsFalse(summary.Applied);
            StringAssert.Contains("did not run", summary.Error);
            Assert.IsFalse(BattleSession.ApplyRewards(null, failed, _content, Shape, EncounterLevel, _drops).Applied);
            Assert.IsEmpty(save.Materials.ClearedCells);
        }

        private BattleSessionResult AssertFails(BattleSetup setup, string fragment)
        {
            BattleSessionResult result = null;
            Assert.DoesNotThrow(() => result = BattleSession.Run(setup));
            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Battle);
            StringAssert.Contains(fragment, result.Error);
            return result;
        }

        /// <summary>The first four species kits as a level-10 team with their default loadouts, and the avatar's default books at level 6.</summary>
        private PlayerSave StarterSave()
        {
            PlayerSave save = PlayerSave.CreateNew();

            for (int i = 0; i < 4; i++)
            {
                SpeciesKitData kit = _library.SpeciesKits[i];
                OwnedBeast beast = OwnedBeast.Create("b" + (i + 1), kit.SpeciesId, 10);

                for (int slot = 0; slot < kit.DefaultLoadout.Length; slot++)
                {
                    beast.Skills.Learn(kit.DefaultLoadout[slot]);
                    beast.Skills.Equip(slot, kit.DefaultLoadout[slot]);
                }

                save.Beasts.Add(beast);
            }

            save.Avatar.Level = 6;

            for (int i = 0; i < SkillLibraryData.AvatarDefaultActiveCount; i++)
            {
                save.AvatarSkills.Actives.Learn(_library.AvatarActives[i].SkillId);
                save.AvatarSkills.Actives.Equip(i, _library.AvatarActives[i].SkillId);
            }

            for (int i = 0; i < _library.AvatarDefaultPassives.Length; i++)
            {
                save.AvatarSkills.Passives.Learn(_library.AvatarDefaultPassives[i]);
                save.AvatarSkills.Passives.Equip(i, _library.AvatarDefaultPassives[i]);
            }

            return save;
        }

        /// <summary>The whole team against two enemies of the next two kits (level <see cref="EncounterLevel"/>, or 1 when weak).</summary>
        private BattleSetup Setup(PlayerSave save, int seed, bool weakEnemies = false)
        {
            BattleSetup setup = new BattleSetup
            {
                Save = save,
                Content = _content,
                AvatarProfile = _profile,
                Seed = seed,
                Encounter = new EncounterSetup { Arena = ArenaSize.Medium }
            };

            foreach (OwnedBeast beast in save.Beasts)
            {
                setup.TeamBeastIds.Add(beast.BeastId);
            }

            int level = weakEnemies ? 1 : EncounterLevel;
            setup.Encounter.Enemies.Add(new EnemySpec(_library.SpeciesKits[4].SpeciesId, level));
            setup.Encounter.Enemies.Add(new EnemySpec(_library.SpeciesKits[5].SpeciesId, level));
            return setup;
        }

        private static string Trace(BattleSessionResult result)
        {
            StringBuilder trace = new StringBuilder();
            trace.Append(result.Outcome).Append('/').Append(result.Battle.ElapsedTicks).Append('/').Append(result.Battle.ActionCount);

            foreach (BattleTurnResult turn in result.Battle.Turns)
            {
                trace.Append('|').Append(turn.Unit == null ? "?" : turn.Unit.Id).Append('@').Append(turn.EndPosition);
            }

            foreach (BattleUnit unit in result.Units)
            {
                trace.Append('#').Append(unit.Id).Append('=').Append(unit.CurrentHp);
            }

            return trace.ToString();
        }

        private static BattleUnit FindUnit(BattleSessionResult result, string unitId)
        {
            foreach (BattleUnit unit in result.Units)
            {
                if (unit.Id == unitId)
                {
                    return unit;
                }
            }

            return null;
        }

        private static HexCoordinate StartOf(BattleSessionResult result, string unitId)
        {
            foreach (BattleTurnResult turn in result.Battle.Turns)
            {
                if (turn.Unit != null && turn.Unit.Id == unitId)
                {
                    return turn.StartPosition;
                }
            }

            return FindUnit(result, unitId).Position;
        }

        private T Create<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            _created.Add(asset);
            return asset;
        }

        /// <summary>The authored content as runtime assets, through the importers' own builders.</summary>
        private BattleContent BuildContent(BeastRosterData roster, SkillLibraryData library)
        {
            Dictionary<string, SkillSO> skills = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
            foreach (SkillData data in library.BeastSkills)
            {
                skills[data.SkillId] = BuildSkill(data);
            }

            foreach (SkillData data in library.AvatarActives)
            {
                skills[data.SkillId] = BuildSkill(data);
            }

            List<CreatureSpeciesSO> species = BeastRosterBuilder.BuildAll(roster, out Dictionary<string, GrowthRateCurve> curves);
            _created.AddRange(curves.Values);
            foreach (CreatureSpeciesSO beast in species)
            {
                _created.Add(beast);
                SpeciesKitData kit = Array.Find(library.SpeciesKits, k => k.SpeciesId == beast.SpeciesId);
                if (kit != null)
                {
                    foreach (string skillId in kit.DefaultLoadout)
                    {
                        beast.DefaultLoadout.Add(skills[skillId]);
                    }
                }
            }

            List<PassiveSkillSO> passives = new List<PassiveSkillSO>();
            foreach (PassiveData data in library.AvatarPassives)
            {
                PassiveSkillSO passive = Create<PassiveSkillSO>();
                SkillLibraryBuilder.ApplyPassive(data, passive);
                passives.Add(passive);
            }

            List<TeamBondSO> bonds = new List<TeamBondSO>();
            foreach (TeamBondData data in library.TeamBonds)
            {
                TeamBondSO bond = Create<TeamBondSO>();
                SkillLibraryBuilder.ApplyTeamBond(data, bond);
                bonds.Add(bond);
            }

            return new BattleContent(species, skills.Values, passives, bonds);
        }

        private SkillSO BuildSkill(SkillData data)
        {
            SkillSO skill = Create<SkillSO>();
            SkillLibraryBuilder.ApplySkill(data, skill);
            return skill;
        }
    }
}
