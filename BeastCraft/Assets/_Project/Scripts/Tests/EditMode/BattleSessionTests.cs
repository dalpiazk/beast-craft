using System;
using System.Collections.Generic;
using System.Text;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Bonds;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="BattleSession"/> end to end over the authored content: a starter
    /// <see cref="PlayerSave"/> built from the roster and skill library fights a small enemy group,
    /// deterministically per seed; rewards land in the save (practice XP, first-clear drops, avatar
    /// XP) and the save still round-trips; a bad setup is a clear error, never an exception.
    /// </summary>
    public partial class BattleSessionTests
    {
        private const string Shape = "squad";
        private const int EncounterLevel = 8;

        private readonly List<ContentAsset> _created = new List<ContentAsset>();
        private BeastRosterData _roster;
        private SkillLibraryData _library;
        private BattleContent _content;
        private DropTable _drops;
        private AvatarStatsSO _profile;
        private GearSO _blade;
        private GearSO _lateShell;
        private AvatarGearSO _cloak;
        private EnemyCatalog _enemies;

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
                int expected = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, EncounterLevel, knockedOut, 10);
                Assert.AreEqual(expected, summary.BeastXpGained[beast.BeastId], "level 10 vs encounter 8: after the level-gap falloff");
                Assert.AreEqual(beastXpBefore[i] + expected, BeastProgression.TotalXpToReach(beast.Progress.Level) + beast.Progress.Xp);
            }

            Assert.IsFalse(summary.BeastXpGained.ContainsKey(benched.BeastId), "a benched beast is not paid as fielded");
            int benchXp = BeastProgression.BenchXp(BattleOutcome.PlayerVictory, EncounterLevel, 10);
            Assert.Greater(benchXp, 0);
            Assert.AreEqual(benchXp, summary.BenchXpGained[benched.BeastId], "a benched beast earns its bench share");
            Assert.AreEqual(10, benched.Progress.Level);
            Assert.AreEqual(benchXp, benched.Progress.Xp);
            Assert.AreEqual(avatarXpBefore + summary.AvatarXpGained, AvatarProgression.TotalXpToReach(save.Avatar.Level) + save.Avatar.Xp);

            BattleRewardSummary again = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops, new System.Random(99));
            Assert.IsFalse(again.Applied);
            StringAssert.Contains("already", again.Error);
        }

        [Test]
        public void ApplyRewards_IsDeterministic_AndTheSaveRoundTrips()
        {
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), SaveContentCatalog.FromData(_roster, _library));
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
            int participation = LevelGapXp.Apply(BeastProgression.ParticipationXp, LevelGapXp.Gap(10, EncounterLevel));
            Assert.AreEqual(participation, summary.BeastXpGained[save.Beasts[0].BeastId], "a lost battle pays participation only (after the falloff)");
            Assert.AreEqual(participation, save.Beasts[0].Progress.Xp);
            Assert.IsNull(result.Avatar);
        }

        [Test]
        public void ApplyRewards_UnderACap_BanksAtTheCap_PaysTheBench_AndReportsTheFalloff()
        {
            const int cap = 10;
            PlayerSave save = StarterSave();
            OwnedBeast rookie = OwnedBeast.Create("rookie", _roster.Species[0].SpeciesId, 1);
            save.Beasts.Add(rookie);
            int nearlyLevelled = BeastProgression.XpToNextLevel(10) - 3;
            for (int i = 0; i < 4; i++)
            {
                save.Beasts[i].Progress.Xp = nearlyLevelled;
            }

            BattleSetup setup = Setup(save, 42, weakEnemies: true);
            setup.TeamBeastIds.Remove(rookie.BeastId);
            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.PlayerVictory, result.Outcome, result.Error);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, Shape, EncounterLevel, _drops, cap, new System.Random(99));

            Assert.IsTrue(summary.Applied, summary.Error);
            Assert.AreEqual(cap, summary.BeastLevelCap);
            bool bankedAny = false;
            for (int i = 0; i < 4; i++)
            {
                OwnedBeast beast = save.Beasts[i];
                int pool = nearlyLevelled + summary.BeastXpGained[beast.BeastId];
                int held = System.Math.Min(pool, BeastProgression.XpToNextLevel(10) - 1);
                Assert.AreEqual(10, beast.Progress.Level, "held at the cap");
                Assert.AreEqual(held, beast.Progress.Xp);
                Assert.AreEqual(pool - held, beast.Progress.BankedXp, "the rest is banked");
                Assert.AreEqual(pool - held, summary.XpBanked.TryGetValue(beast.BeastId, out int banked) ? banked : 0);
                Assert.AreEqual(25, summary.FalloffPercent[beast.BeastId], "level 10 vs encounter 8");
                Assert.AreEqual(pool >= BeastProgression.XpToNextLevel(10) ? 1 : 0, LevelCap.Release(beast.Progress, 20), "a raised cap spends the bank");
                bankedAny |= pool > held;
            }

            Assert.IsTrue(bankedAny, "a standing beast earned more than the 2 XP it lacked");
            Assert.AreEqual(0, summary.BeastLevelsGained);
            Assert.AreEqual(BeastProgression.BenchXp(BattleOutcome.PlayerVictory, EncounterLevel, 1), summary.BenchXpGained[rookie.BeastId]);
            Assert.AreEqual(BeastProgression.BattleXp(BattleOutcome.PlayerVictory, EncounterLevel, false) * BeastProgression.BenchSharePermille(EncounterLevel, 1) / 1000,
                            summary.BenchXpGained[rookie.BeastId], "seven levels behind: the catch-up share of a standing beast's XP");
            Assert.AreEqual(100, summary.FalloffPercent[rookie.BeastId]);
            Assert.IsFalse(summary.XpBanked.ContainsKey(rookie.BeastId));
            Assert.AreEqual(100, summary.AvatarFalloffPercent, "the avatar (level 6) is below the encounter");
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
        public void Run_BeastsAndAvatarWearTheirSavedGear()
        {
            PlayerSave save = StarterSave();
            OwnedBeast b1 = save.Beasts[0];
            string blade = save.Gear.AddBeastGear(_blade.GearId);
            string shell = save.Gear.AddBeastGear(_lateShell.GearId);
            string cloak = save.Gear.AddAvatarGear(_cloak.AvatarGearId);
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, b1.BeastId, GearSlot.WeaponOrCore, blade, _content));
            Assert.AreEqual(GearEquipResult.LevelTooLow, GearRules.EquipBeastGear(save, b1.BeastId, GearSlot.ArmorOrShell, shell, _content));
            b1.EquippedGear[(int)GearSlot.ArmorOrShell] = shell;
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipAvatarGear(save, AvatarGearSlot.Armor, cloak, _content));

            BattleSessionResult result = BattleSession.Run(Setup(save, 9));

            Assert.IsTrue(result.Success, result.Error);
            CreatureSpeciesSO b1Species = _content.GetSpecies(b1.Progress.SpeciesId);
            StatBlock bare = StatCalculator.ComputeStats(b1Species, b1.Progress.Level, null);
            StatBlock worn = result.StartingStats[result.UnitIdFor(b1.BeastId)];
            Assert.AreEqual(StatCalculator.ComputeStats(b1Species, b1.Progress.Level, new[] { _blade, _lateShell }), worn);
            Assert.AreEqual(bare.Attack + 50, worn.Attack, "the blade applies");
            Assert.AreEqual(bare.Hp, worn.Hp, "the level-50 shell is ignored at level 10 (not an error)");

            OwnedBeast b2 = save.Beasts[1];
            Assert.AreEqual(StatCalculator.ComputeStats(_content.GetSpecies(b2.Progress.SpeciesId), b2.Progress.Level, null),
                            result.StartingStats[result.UnitIdFor(b2.BeastId)]);

            StatBlock avatarBase = _profile.GetStatsAtLevel(save.Avatar.Level);
            Assert.AreEqual(StatCalculator.ComputeStats(avatarBase, StatCalculator.CollectModifiers(new[] { _cloak })), result.StartingStats[result.Avatar.Id]);
            Assert.AreEqual(avatarBase.Hp + 100, result.StartingStats[result.Avatar.Id].Hp);

            BattleSetup overridden = Setup(save, 9);
            overridden.AvatarGear = new List<AvatarGearSO>();
            BattleSessionResult bareAvatar = BattleSession.Run(overridden);
            Assert.IsTrue(bareAvatar.Success, bareAvatar.Error);
            Assert.AreEqual(avatarBase.Hp, bareAvatar.StartingStats[bareAvatar.Avatar.Id].Hp, "an explicit AvatarGear list overrides the save");
        }

        [Test]
        public void Run_BadGear_ReturnsClearErrors()
        {
            BattleSetup missing = Setup(StarterSave(), 1);
            missing.Save.Beasts[0].EquippedGear[0] = "gear99";
            AssertFails(missing, "'gear99', which is not in the inventory");

            BattleSetup unknown = Setup(StarterSave(), 1);
            string ghost = unknown.Save.Gear.AddBeastGear("ghost_gear");
            unknown.Save.Beasts[0].EquippedGear[0] = ghost;
            AssertFails(unknown, "unknown gear 'ghost_gear'");

            BattleSetup wrongSlot = Setup(StarterSave(), 1);
            string blade = wrongSlot.Save.Gear.AddBeastGear(_blade.GearId);
            wrongSlot.Save.Beasts[0].EquippedGear[(int)GearSlot.Accessory] = blade;
            AssertFails(wrongSlot, "belongs in WeaponOrCore");

            BattleSetup twice = Setup(StarterSave(), 1);
            string shared = twice.Save.Gear.AddBeastGear(_blade.GearId);
            twice.Save.Beasts[0].EquippedGear[0] = shared;
            twice.Save.Beasts[1].EquippedGear[0] = shared;
            AssertFails(twice, "worn more than once");

            BattleSetup avatarMissing = Setup(StarterSave(), 1);
            avatarMissing.Save.AvatarEquippedGear[(int)AvatarGearSlot.Armor] = "gear42";
            AssertFails(avatarMissing, "The avatar wears gear instance 'gear42'");
            avatarMissing.AvatarGear = new List<AvatarGearSO>();
            Assert.IsTrue(BattleSession.Run(avatarMissing).Success, "an override skips the save's avatar gear");
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
        public void Run_FieldsAGeneratedPlan_WithItsElementsKitsAndScaledStats()
        {
            EncounterLibrary library = EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterPlanTests.LoadDifficulty()));
            EncounterPlan plan = EncounterPlan.Generate(library, _enemies, "squad", EncounterLevel, 77);
            BattleSetup setup = Setup(StarterSave(), 5);
            setup.Encounter = plan.ToSetup();

            BattleSessionResult result = BattleSession.Run(setup);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual("squad", result.ShapeId);
            Assert.AreEqual(EncounterLevel, result.EncounterLevel);
            Assert.Greater(plan.Multiplier, 1.0, "the calibrated squad is tougher than the raw stats");

            for (int i = 0; i < plan.Enemies.Count; i++)
            {
                EncounterLineupEnemy planned = plan.Enemies[i];
                BattleUnit unit = FindUnit(result, "enemy" + (i + 1));
                CreatureSpeciesSO species = _enemies.Species(planned.EnemyId, planned.Element);
                StatBlock expected = EnemyScaling.Scale(StatCalculator.ComputeStats(species, EncounterLevel, null), plan.Multiplier);

                Assert.AreEqual(BattleTeam.Enemy, unit.Team);
                Assert.AreEqual(expected, result.StartingStats[unit.Id], planned.EnemyId + ": level stats times the plan's multiplier");
                Assert.AreEqual(_enemies.Kit(planned.EnemyId, planned.Element).Count, unit.Skills.Count);
                Assert.AreEqual(_enemies.Kit(planned.EnemyId, planned.Element)[0].SkillId, unit.Skills.Skills[0].SkillId);
                Assert.AreEqual(planned.Element, unit.Skills.Skills[0].Element);
            }
        }

        [Test]
        public void ApplyRewards_WithoutArguments_PaysTheEncountersShapeAndBand()
        {
            // An EXAMPLE template, built here for the test only (the shipped library authors none):
            // one swarmling at a quarter of its stats, so the team wins.
            EncounterLibraryData data = EncounterContentTests.LoadEncounterLibrary();
            data.Templates = new[]
            {
                new EncounterTemplateData
                {
                    EncounterId = "example_lone_swarmling",
                    ShapeId = "horde",
                    Arena = "Medium",
                    Groups = new[] { new EncounterGroupData { EnemyId = "swarmling", Count = 1, Elements = new[] { "Fire" } } },
                    DifficultyOverride = 0.25
                }
            };
            EncounterPlan plan = EncounterPlan.FromTemplate(EncounterLibrary.Build(data), _enemies, "example_lone_swarmling", 30);
            PlayerSave save = StarterSave();
            BattleSetup setup = Setup(save, 3);
            setup.Encounter = plan.ToSetup();

            BattleSessionResult result = BattleSession.Run(setup);
            Assert.AreEqual(BattleOutcome.PlayerVictory, result.Outcome, result.Error);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, _drops, new System.Random(4));

            Assert.IsTrue(summary.Applied, summary.Error);
            Assert.IsTrue(summary.Loot.FirstClear);
            Assert.IsTrue(save.Materials.HasCleared("horde", _drops.BandForLevel(30).MinLevel), "the horde cell of the level-30 band");
            Assert.AreEqual(AvatarProgression.BattleXp(BattleOutcome.PlayerVictory, 30), summary.AvatarXpGained);
        }

        [Test]
        public void ApplyRewards_WithoutArguments_RefusesAnEncounterThatNamedNoShape()
        {
            PlayerSave save = StarterSave();
            BattleSessionResult result = BattleSession.Run(Setup(save, 42, weakEnemies: true));
            Assert.IsTrue(result.Success, result.Error);

            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, _content, _drops);

            Assert.IsFalse(summary.Applied);
            StringAssert.Contains("no shape or level", summary.Error);
            Assert.IsEmpty(save.Materials.ClearedCells);
        }

        [Test]
        public void Run_RefusesAnElementOnARosterSpecies_AndABadMultiplier()
        {
            BattleSetup element = Setup(StarterSave(), 1);
            element.Encounter.Enemies[0].Element = Element.Fire;
            AssertFails(element, "Element only applies to enemy-library enemies");

            BattleSetup multiplier = Setup(StarterSave(), 1);
            multiplier.Encounter.Enemies[0].StatMultiplier = 0.0;
            AssertFails(multiplier, "StatMultiplier");
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

        [Test]
        public void SuggestionFor_TwoLossesAtALocation_OffersNothing()
        {
            PlayerSave save = CampaignSave(out MapNode node);
            LoseAt(save, node, 2);

            Assert.AreEqual(2, CampaignRules.LossesAt(save.Campaign.ActiveRun, node.NodeId));
            Assert.IsNull(CampaignRules.SuggestionFor(save, node.NodeId, new PlayerSettings(), SuggestionEncounters(), _content, 3));
        }

        [Test]
        public void SuggestionFor_ThreeLossesAtALocation_SuggestsTheSuggestersTeamFromTheOwnedBeasts()
        {
            PlayerSave save = CampaignSave(out MapNode node);
            LoseAt(save, node, 3);
            EncounterLibrary encounters = SuggestionEncounters();

            CampaignTeamSuggestion suggestion = CampaignRules.SuggestionFor(save, node.NodeId, null, encounters, _content, 3);

            Assert.IsNotNull(suggestion, "three losses and suggestions on by default");
            Assert.AreEqual(node.NodeId, suggestion.NodeId);
            Assert.AreEqual(3, suggestion.Losses);
            Assert.AreEqual(3, suggestion.BeastIds.Count);
            CollectionAssert.AllItemsAreUnique(suggestion.BeastIds);
            foreach (string beastId in suggestion.BeastIds)
            {
                Assert.IsNotNull(save.FindBeast(beastId), beastId);
            }

            // Exactly TeamSuggester's pick for the node's encounter, over the save's beasts in order.
            EncounterPlan plan = CampaignRules.PlanFor(node, encounters, _enemies);
            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>();
            foreach (OwnedBeast beast in save.Beasts)
            {
                owned.Add(new TeamSuggestionCandidate(_content.GetSpecies(beast.Progress.SpeciesId), beast.Progress.Level));
            }

            List<SkillSO> enemySkills = new List<SkillSO>();
            foreach (EncounterLineupEnemy enemy in plan.Enemies)
            {
                enemySkills.AddRange(_enemies.Kit(enemy.EnemyId, enemy.Element));
            }

            TeamSuggestion direct = TeamSuggester.Suggest(new TeamSuggestionRequest
            {
                Preview = plan.Preview(),
                Owned = owned,
                TeamSize = 3,
                Bonds = _content.TeamBonds,
                EncounterCanAfflict = TeamSuggester.CanAfflict(enemySkills)
            });
            CollectionAssert.AreEqual(direct.Members, suggestion.Suggestion.Members);
            for (int m = 0; m < direct.Members.Count; m++)
            {
                Assert.AreEqual(save.Beasts[direct.Members[m]].BeastId, suggestion.BeastIds[m]);
            }
        }

        [Test]
        public void SuggestionFor_SuggestionsTurnedOff_OffersNothing()
        {
            PlayerSave save = CampaignSave(out MapNode node);
            LoseAt(save, node, 5);

            Assert.IsNull(CampaignRules.SuggestionFor(save, node.NodeId, new PlayerSettings { TeamSuggestionsEnabled = false }, SuggestionEncounters(), _content, 3));
        }

        [Test]
        public void LossesAt_CountsPerLocation_AndAClearResetsThem()
        {
            PlayerSave save = CampaignSave(out MapNode node);
            MapRun run = save.Campaign.ActiveRun;
            MapNode other = CampaignRules.Choices(run).Find(n => n.IsBattle && n.NodeId != node.NodeId);
            Assert.IsNotNull(other, "the first row offers two battle locations");

            LoseAt(save, node, 3);
            LoseAt(save, other, 1);

            Assert.AreEqual(0, CampaignRules.LossesAt(run, node.NodeId), "a loss elsewhere restarts the count");
            Assert.AreEqual(1, CampaignRules.LossesAt(run, other.NodeId));
            Assert.AreEqual(4, run.Attempts, "the run still counts every loss");
            Assert.IsNull(CampaignRules.SuggestionFor(save, node.NodeId, null, SuggestionEncounters(), _content, 3));

            CampaignRules.ResolveBattle(save, CampaignRegions(), other.NodeId, BattleOutcome.PlayerVictory);
            Assert.AreEqual(0, CampaignRules.LossesAt(run, other.NodeId));
            Assert.AreEqual(-1, run.NodeAttemptsNodeId);
        }

        private static RegionLibrary CampaignRegions()
        {
            return RegionLibrary.Build(CampaignMapTests.LoadRegions());
        }

        private static EncounterLibrary SuggestionEncounters()
        {
            return EncounterLibrary.Build(EncounterContentTests.LoadEncounterLibrary(), EncounterDifficultyTable.Build(EncounterPlanTests.LoadDifficulty()));
        }

        /// <summary>A save owning six beasts (six species kits at level 10) on an r01 expedition, and a battle location on its first row.</summary>
        private PlayerSave CampaignSave(out MapNode node)
        {
            PlayerSave save = StarterSave();
            for (int i = 4; i < 6; i++)
            {
                save.Beasts.Add(OwnedBeast.Create("b" + (i + 1), _library.SpeciesKits[i].SpeciesId, 10));
            }

            Assert.IsTrue(CampaignRules.StartRun(save, CampaignRegions(), "r01", 11).Success);
            node = CampaignRules.Choices(save.Campaign.ActiveRun).Find(n => n.IsBattle);
            Assert.IsNotNull(node);
            return save;
        }

        private static void LoseAt(PlayerSave save, MapNode node, int times)
        {
            for (int i = 0; i < times; i++)
            {
                Assert.AreEqual(CampaignOutcome.Lost, CampaignRules.ResolveBattle(save, CampaignRegions(), node.NodeId, BattleOutcome.EnemyVictory).Outcome);
            }
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

        private T Create<T>() where T : ContentAsset, new()
        {
            T asset = new T();
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

            _blade = Create<GearSO>();
            _blade.GearId = "test_blade";
            _blade.Slot = GearSlot.WeaponOrCore;
            _blade.Modifiers.Add(new StatModifier { Stat = StatType.Attack, FlatBonus = 50 });

            _lateShell = Create<GearSO>();
            _lateShell.GearId = "test_late_shell";
            _lateShell.Slot = GearSlot.ArmorOrShell;
            _lateShell.MinimumLevel = 50;
            _lateShell.Modifiers.Add(new StatModifier { Stat = StatType.HP, FlatBonus = 500 });

            _cloak = Create<AvatarGearSO>();
            _cloak.AvatarGearId = "test_cloak";
            _cloak.Slot = AvatarGearSlot.Armor;
            _cloak.Modifiers.Add(new StatModifier { Stat = StatType.HP, FlatBonus = 100 });

            _enemies = EnemyCatalog.Build(EncounterContentTests.LoadEnemyLibrary(), curves["medium"]);
            return new BattleContent(species, skills.Values, passives, bonds, new[] { _blade, _lateShell }, new[] { _cloak }, _enemies, _consumables.All);
        }

        private SkillSO BuildSkill(SkillData data)
        {
            SkillSO skill = Create<SkillSO>();
            SkillLibraryBuilder.ApplySkill(data, skill);
            return skill;
        }
    }
}
