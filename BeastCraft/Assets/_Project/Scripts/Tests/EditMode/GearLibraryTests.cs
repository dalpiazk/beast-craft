using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The gear library (<c>Data/Items/gear-library.json</c>): the authored pieces pass
    /// <see cref="GearLibraryValidator"/> including the power budget against the roster; the
    /// validator's rules; <see cref="GearLibrary"/>'s bands and pools; <see cref="GearDrops"/>;
    /// avatar gear's minimum level; the campaign's first-clear pass and lair rewards.
    /// </summary>
    public class GearLibraryTests
    {
        private GearLibraryData _data;
        private GearLibrary _library;
        private List<CreatureSpeciesSO> _species;

        [SetUp]
        public void SetUp()
        {
            _data = LoadGear();
            _library = GearLibrary.Build(_data);
            _species = BeastRosterBuilder.BuildAll(BeastRosterTests.LoadRoster(), out Dictionary<string, GrowthRateCurve> _);
        }

        internal static GearLibraryData LoadGear()
        {
            return EncounterContentTests.Load<GearLibraryData>(GearLibraryData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredLibrary_PassesValidation_AndTheBudget()
        {
            List<string> errors = GearLibraryValidator.Validate(_data, _species);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(78, _data.BeastGear.Length, "5 bands x 6 pieces x common/rare + 3 bands x 6 epics");
            Assert.AreEqual(30, _data.AvatarGear.Length, "5 bands x 3 slots x common/rare");
            CollectionAssert.AreEqual(new[] { 1, 21, 41, 61, 81 }, _library.BandFloors);
            foreach (GearItemData item in _data.BeastGear)
            {
                double points = GearLibraryValidator.Points(item, _species);
                Assert.That(points, Is.InRange(GearLibraryValidator.Budget(item.Rarity, item.MinimumLevel, _species) * 0.9, GearLibraryValidator.Budget(item.Rarity, item.MinimumLevel, _species) * 1.1), item.GearId);
                if (item.Rarity == 2)
                {
                    Assert.GreaterOrEqual(item.MinimumLevel, 41, item.GearId + ": epics start at band 41");
                    CollectionAssert.AreEqual(new[] { GearLibrary.SourceBoss }, item.Sources, item.GearId);
                }
            }
        }

        [Test]
        public void Validator_CatchesStructureAndBudgetMistakes()
        {
            Assert.IsNotEmpty(GearLibraryValidator.Validate(null));
            GearLibraryData data = new GearLibraryData
            {
                SchemaVersion = 1,
                BeastGear = new[]
                {
                    Piece("ok_fang", "WeaponOrCore", 0, 21, new GearModifierData { Stat = "Attack", Flat = 2 }),
                    Piece("ok_fang", "WeaponOrCore", 0, 21, new GearModifierData { Stat = "Attack", Flat = 2 }),
                    Piece("mover", "Accessory", 0, 21, new GearModifierData { Stat = "MoveRange", Flat = 1 }),
                    Piece("wrong_slot", "Weapon", 0, 21, new GearModifierData { Stat = "Attack", Flat = 2 }),
                    Piece("greedy", "WeaponOrCore", 0, 21, new GearModifierData { Stat = "Attack", Flat = 9 }),
                    Piece("tiny_level", "WeaponOrCore", 1, 1, new GearModifierData { Stat = "Attack", Flat = 3 })
                },
                AvatarGear = new[] { Piece("sold_epic", "Weapon", 2, 41, new GearModifierData { Stat = "SpecialAttack", Pct = 0.12f }) }
            };
            data.AvatarGear[0].Sources = new[] { "shop" };

            List<string> errors = GearLibraryValidator.Validate(data, _species);
            string all = string.Join("\n", errors);

            StringAssert.Contains("used twice", all);
            StringAssert.Contains("MoveRange", all);
            StringAssert.Contains("'Weapon' is not a GearSlot", all);
            StringAssert.Contains("'greedy'", all);
            StringAssert.Contains("from bosses only", all);
            StringAssert.Contains("adds", all, "a flat 3 is more than 25% of a weak species' Attack at level 1");
        }

        [Test]
        public void Library_IndexesBandsAndPools()
        {
            Assert.AreEqual(1, _library.BandFloor(0));
            Assert.AreEqual(1, _library.BandFloor(20));
            Assert.AreEqual(21, _library.BandFloor(21));
            Assert.AreEqual(81, _library.BandFloor(100));
            GearItem fang = _library.Get("fang_t2");
            Assert.AreEqual((int)GearSlot.WeaponOrCore, fang.SlotIndex);
            Assert.IsFalse(fang.IsAvatarGear);
            Assert.IsTrue(_library.Get("avatar_staff_t1").IsAvatarGear);
            Assert.IsNull(_library.Get("nope"));

            List<GearItem> drops = _library.Pool(GearLibrary.SourceDrop, 0, 33);
            Assert.AreEqual(9, drops.Count, "six beast and three avatar commons of band 21");
            Assert.IsTrue(drops.TrueForAll(i => i.MinimumLevel == 21 && i.Rarity == 0));
            Assert.AreEqual(6, _library.Pool(GearLibrary.SourceShop, 0, 33, false).Count);
            Assert.IsEmpty(_library.Pool(GearLibrary.SourceShop, 2, 90), "epics are never sold");
            Assert.AreEqual(6, _library.Pool(GearLibrary.SourceBoss, 2, 90).Count);

            GearSO asset = _library.BeastGearAsset("barding_t3_rare");
            Assert.AreEqual(GearSlot.ArmorOrShell, asset.Slot);
            Assert.AreEqual(41, asset.MinimumLevel);
            Assert.AreEqual(1, asset.Rarity);
            Assert.AreSame(asset, _library.BeastGearAsset("barding_t3_rare"), "assets are created once");
            Assert.AreEqual(30, _library.AvatarGearAssets.Count);
            Assert.AreEqual(41, new List<AvatarGearSO>(_library.AvatarGearAssets).Find(a => a.AvatarGearId == "avatar_ring_t3").MinimumLevel);
        }

        [Test]
        public void GearDrops_RollTheTablesChances_Deterministically()
        {
            DropTableData tableData = DropTableTests.LoadTables();
            tableData.GearDrops = new[] { new GearDropData { Shape = "elite", Rarity = 0, ChancePerMille = 1000 }, new GearDropData { Shape = "elite", Rarity = 2, ChancePerMille = 1000 } };
            DropTable table = DropTableBuilder.Build(tableData, null);

            List<GearItem> low = GearDrops.Roll(table, _library, "elite", 12, new Random(3));
            Assert.AreEqual(1, low.Count, "a certain common; the epic's pool below band 41 is empty");
            Assert.AreEqual(1, low[0].MinimumLevel);
            Assert.IsTrue(low[0].HasSource(GearLibrary.SourceDrop));
            Assert.IsEmpty(GearDrops.Roll(table, _library, "squad", 12, new Random(3)));

            List<GearItem> a = GearDrops.Roll(DropTableBuilder.Build(DropTableTests.LoadTables(), null), _library, "elite", 55, new Random(9));
            List<GearItem> b = GearDrops.Roll(DropTableBuilder.Build(DropTableTests.LoadTables(), null), _library, "elite", 55, new Random(9));
            CollectionAssert.AreEqual(a.ConvertAll(i => i.GearId), b.ConvertAll(i => i.GearId));

            Assert.AreEqual(2, GearDrops.RollGuaranteed(_library, 2, 55, new Random(1)).Rarity);
            Assert.AreEqual(1, GearDrops.RollGuaranteed(_library, 2, 15, new Random(1)).Rarity, "no epics below band 41: the lair gives a rare");
            Assert.IsNull(GearDrops.RollGuaranteed(null, 1, 15, new Random(1)));

            PlayerSave save = PlayerSave.CreateNew();
            Assert.AreEqual("gear1", GearDrops.Grant(save, _library.Get("fang_t1")));
            Assert.AreEqual("gear2", GearDrops.Grant(save, _library.Get("avatar_coat_t1")));
            Assert.AreEqual(1, save.Gear.BeastGear.Count);
            Assert.AreEqual(1, save.Gear.AvatarGear.Count);
        }

        [Test]
        public void AvatarGear_IsGatedOnTheAvatarsLevel()
        {
            BattleContent content = new BattleContent(null, null, null, null, _library.BeastGearAssets, _library.AvatarGearAssets);
            PlayerSave save = PlayerSave.CreateNew();
            save.Avatar.Level = 30;
            string ring = GearDrops.Grant(save, _library.Get("avatar_ring_t3"));

            Assert.AreEqual(GearEquipResult.LevelTooLow, GearRules.EquipAvatarGear(save, AvatarGearSlot.Trinket, ring, content));
            Assert.IsNull(GearRules.GetSlot(save.AvatarEquippedGear, (int)AvatarGearSlot.Trinket));
            save.Avatar.Level = 41;
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipAvatarGear(save, AvatarGearSlot.Trinket, ring, content));
            Assert.IsEmpty(SaveValidator.Validate(save, null, content));

            save.Avatar.Level = 12;
            List<SaveIssue> issues = SaveValidator.Validate(save, null, content);
            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(SaveIssueKind.GearLevelTooLow, issues[0].Kind);
        }

        [Test]
        public void FirstPassAndLairClears_GrantGear_AndReplaysDoNot()
        {
            RegionLibrary regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
            EconomyContent economy = new EconomyContent { Gear = _library };
            PlayerSave save = PlayerSave.CreateNew();

            CampaignResult gate = WalkToTheTop(save, regions, "r01", 0, 11, economy);
            Assert.AreEqual(CampaignOutcome.StageCleared, gate.Outcome);
            Assert.IsNotNull(gate.GearGranted);
            Assert.AreEqual(1, _library.Get(gate.GearGranted).Rarity, "a pass's first clear: a rare");
            Assert.AreEqual(1, save.Gear.BeastGear.Count + save.Gear.AvatarGear.Count);

            CampaignResult replay = WalkToTheTop(save, regions, "r01", 0, 12, economy);
            Assert.AreEqual(CampaignOutcome.StageCleared, replay.Outcome);
            Assert.IsNull(replay.GearGranted, "a replayed pass grants nothing");

            CampaignResult noEconomy = WalkToTheTop(save, regions, "r01", 1, 13, null);
            Assert.IsNull(noEconomy.GearGranted);

            save.Campaign.FindRegion("r01").StagesCleared = 3;
            CampaignResult lair = WalkToTheTop(save, regions, "r01", 3, 14, economy);
            Assert.AreEqual(CampaignOutcome.RegionCleared, lair.Outcome);
            Assert.AreEqual(1, _library.Get(lair.GearGranted).Rarity, "r01's lair is below the epic bands: a rare");
            Assert.IsNull(WalkToTheTop(save, regions, "r01", 3, 15, economy).GearGranted);
        }

        private static CampaignResult WalkToTheTop(PlayerSave save, RegionLibrary regions, string regionId, int stage, int seed, EconomyContent economy)
        {
            Assert.IsTrue(CampaignRules.StartRun(save, regions, regionId, stage, seed).Success);
            while (true)
            {
                MapNode node = CampaignRules.Choices(save.Campaign.ActiveRun).Find(n => n.IsBattle);
                if (node == null)
                {
                    node = CampaignRules.Choices(save.Campaign.ActiveRun)[0];
                    if (save.FindBeast("walker") == null)
                    {
                        save.Beasts.Add(OwnedBeast.Create("walker", "griffin", 1));
                    }

                    CampaignResult visited = node.Type == MapNodeType.Rest
                        ? CampaignRules.Camp(save, regions, node.NodeId, "walker")
                        : CampaignRules.Trade(save, regions, node.NodeId, null);
                    Assert.IsTrue(visited.Success, visited.Error);
                    continue;
                }

                CampaignResult result = CampaignRules.ResolveBattle(save, regions, node.NodeId, BattleOutcome.PlayerVictory, economy);
                Assert.IsTrue(result.Success, result.Error);
                if (node.Type == MapNodeType.Gate || node.Type == MapNodeType.Boss)
                {
                    return result;
                }
            }
        }

        private static GearItemData Piece(string id, string slot, int rarity, int minimumLevel, GearModifierData modifier)
        {
            return new GearItemData
            {
                GearId = id,
                DisplayName = id,
                Slot = slot,
                Rarity = rarity,
                MinimumLevel = minimumLevel,
                Archetype = "Striker",
                Modifiers = new[] { modifier },
                Sources = rarity == 2 ? new[] { "boss" } : new[] { "shop", "drop" }
            };
        }
    }
}
