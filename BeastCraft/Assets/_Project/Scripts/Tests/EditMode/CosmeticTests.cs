using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures.Roster;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Cosmetics: the authored <c>Data/Cosmetics/cosmetic-library.json</c> (per-species looks, the
    /// avatar's) and <see cref="CosmeticLibraryValidator"/>; <see cref="CosmeticRules"/> (free,
    /// unlocked and locked looks, owners, colours, default repair); the unlock sources (lairs,
    /// milestones, battle drops) and the save validator through <see cref="EconomyContent"/>.
    /// </summary>
    public class CosmeticTests
    {
        private CosmeticLibraryData _data;
        private CosmeticLibrary _library;

        [SetUp]
        public void SetUp()
        {
            _data = LoadCosmetics();
            _library = CosmeticLibrary.Build(_data);
        }

        internal static CosmeticLibraryData LoadCosmetics()
        {
            return EncounterContentTests.Load<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredLibrary_IsValid_AndEverySpeciesHasItsOwnLooks()
        {
            List<string> species = new List<string>();
            foreach (SpeciesData entry in BeastRosterTests.LoadRoster().Species)
            {
                species.Add(entry.SpeciesId);
            }

            List<string> regions = new List<string>();
            foreach (RegionData region in CampaignMapTests.LoadRegions().Regions)
            {
                regions.Add(region.RegionId);
            }

            List<string> errors = CosmeticLibraryValidator.Validate(_data, species, regions);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            foreach (string id in species)
            {
                List<CosmeticCategory> own = new List<CosmeticCategory>(_library.Categories).FindAll(c => c.Scope == id);
                Assert.GreaterOrEqual(own.Count, 3, id + ": two or more parts and a tint");
                Assert.IsTrue(own.Exists(c => c.IsColor), id);
                Assert.IsTrue(own.Exists(c => !c.IsColor && new List<CosmeticOption>(c.Options).Exists(o => o.Source == CosmeticLibrary.SourceBoss)), id + " has a lair look");
            }

            Assert.IsNotNull(_library.GetCategory("frost_wyrm_wings"), "wings where the species has them");
            Assert.IsNull(_library.GetCategory("golem_wings"));
            foreach (RegionData region in CampaignMapTests.LoadRegions().Regions)
            {
                Assert.IsTrue(new List<CosmeticOption>(AllOptions()).Exists(o => o.Source == CosmeticLibrary.SourceBoss && o.UnlockId == region.RegionId),
                              region.RegionId + "'s lair grants a look");
            }

            Assert.IsNotEmpty(_library.Pool(CosmeticLibrary.SourceShop, 1));
            Assert.IsNotEmpty(_library.Pool(CosmeticLibrary.SourceDrop, 1));
            Assert.IsTrue(_library.Pool(CosmeticLibrary.SourceShop, 10).TrueForAll(o => o.Source != CosmeticLibrary.SourcePremium));
        }

        [Test]
        public void Validator_CatchesAuthoringMistakes()
        {
            CosmeticLibraryData data = LoadCosmetics();
            data.Categories[0].Options[1].IsDefault = true;
            data.Categories[1].DefaultColor = "red";
            data.Categories[2].Scope = "unicorn";
            data.Categories[2].Options[1].Source = "raffle";
            data.Categories[3].Options[4].UnlockId = "r99";
            data.Categories[3].Options[4].Source = CosmeticLibrary.SourceBoss;
            data.Milestones[0].Kind = "Luck";

            List<string> errors = CosmeticLibraryValidator.Validate(data, new[] { "phoenix" }, new[] { "r01" });
            string all = string.Join("\n", errors);
            StringAssert.Contains("exactly one is required", all);
            StringAssert.Contains("#RRGGBB", all);
            StringAssert.Contains("'unicorn'", all);
            StringAssert.Contains("'raffle'", all);
            StringAssert.Contains("names the region", all);
            StringAssert.Contains("Kind must be", all);
        }

        [Test]
        public void Rules_WearFreeAndUnlockedLooks_OnTheRightOwnerOnly()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("g", "griffin", 5));
            save.Beasts.Add(OwnedBeast.Create("p", "phoenix", 5));

            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, "g", "griffin_crest", "swept", _library), "a starter look is free");
            Assert.AreEqual(CosmeticResult.Locked, CosmeticRules.TrySetOption(save, "g", "griffin_crest", "sunlit", _library));
            Assert.AreEqual(CosmeticResult.WrongOwner, CosmeticRules.TrySetOption(save, "p", "griffin_crest", "swept", _library), "looks are per species");
            Assert.AreEqual(CosmeticResult.WrongOwner, CosmeticRules.TrySetOption(save, null, "griffin_crest", "swept", _library));
            Assert.AreEqual(CosmeticResult.UnknownOwner, CosmeticRules.TrySetOption(save, "nobody", "griffin_crest", "swept", _library));
            Assert.AreEqual(CosmeticResult.UnknownCategory, CosmeticRules.TrySetOption(save, "g", "griffin_tail", "swept", _library));
            Assert.AreEqual(CosmeticResult.UnknownOption, CosmeticRules.TrySetOption(save, "g", "griffin_crest", "nope", _library));
            Assert.AreEqual(CosmeticResult.WrongValueType, CosmeticRules.TrySetOption(save, "g", "griffin_tint", "x", _library));

            Assert.IsTrue(CosmeticRules.Unlock(save, _library, "griffin_crest/sunlit"));
            Assert.IsFalse(CosmeticRules.Unlock(save, _library, "griffin_crest/sunlit"), "once");
            Assert.IsFalse(CosmeticRules.Unlock(save, _library, "griffin_crest/swept"), "free looks are never listed");
            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, "g", "griffin_crest", "sunlit", _library));
            Assert.AreEqual("sunlit", CosmeticRules.Worn(save, "g", "griffin_crest", _library).OptionId);
            Assert.AreEqual("natural", CosmeticRules.Worn(save, "g", "griffin_wings", _library).OptionId, "a category never chosen reads as its default");

            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetColor(save, null, "avatar_hair_color", new Color(0.1f, 0.2f, 0.3f, 1f), _library), "colours are free");
            Assert.AreEqual(CosmeticResult.WrongValueType, CosmeticRules.TrySetColor(save, null, "avatar_hair", Color.white, _library));
            Assert.AreEqual(CosmeticResult.Set, CosmeticRules.TrySetOption(save, null, "avatar_hair", "braids", _library));

            EconomyContent economy = new EconomyContent { Cosmetics = _library };
            Assert.IsEmpty(SaveValidator.Validate(save, null, null, economy));
        }

        [Test]
        public void RepairAppearances_DropsLocksAndWrongOwners_BackToTheDefault()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("g", "griffin", 5));
            save.Beasts[0].Appearance.SetOption("griffin_crest", "regal");
            save.Beasts[0].Appearance.SetOption("phoenix_plumage", "swept");
            save.Beasts[0].Appearance.SetOption("griffin_wings", "striped");
            save.AvatarAppearance.SetOption("avatar_hair", "gone_option");
            save.AvatarAppearance.SetColor("griffin_tint", Color.white);

            EconomyContent economy = new EconomyContent { Cosmetics = _library };
            Assert.AreEqual(4, SaveValidator.Validate(save, null, null, economy).Count);
            Assert.AreEqual(4, CosmeticRules.RepairAppearances(save, _library));
            Assert.AreEqual("striped", save.Beasts[0].Appearance.GetOption("griffin_wings"), "a free look survives");
            Assert.AreEqual("natural", CosmeticRules.Worn(save, "g", "griffin_crest", _library).OptionId);
            Assert.IsEmpty(SaveValidator.Validate(save, null, null, economy));
        }

        [Test]
        public void Milestones_UnlockPerSpecies_ByAvatarLevel_AndByBossesBeaten()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("g", "griffin", 49));
            Assert.IsEmpty(CosmeticRules.UnlockMilestones(save, _library));

            save.Beasts[0].Progress.Level = 50;
            List<string> fifty = CosmeticRules.UnlockMilestones(save, _library);
            CollectionAssert.AreEqual(new[] { "griffin_crest/veteran" }, fifty, "a griffin's level 50 unlocks the griffin's veteran look only");
            Assert.IsEmpty(CosmeticRules.UnlockMilestones(save, _library), "idempotent");

            save.Avatar.Level = 50;
            CollectionAssert.AreEqual(new[] { "avatar_outfit/veteran_garb" }, CosmeticRules.UnlockMilestones(save, _library));

            save.Campaign.FindRegion("r01").BossCleared = true;
            CollectionAssert.AreEqual(new[] { "avatar_headwear/champions_laurel" }, CosmeticRules.UnlockMilestones(save, _library));
        }

        [Test]
        public void BattleDrops_RollALowChanceLook_NotYetOwned()
        {
            DropTableData data = DropTableTests.LoadTables();
            data.CosmeticDrops = new[] { new CosmeticDropData { Shape = "squad", ChancePerMille = 1000 } };
            DropTable table = DropTableBuilder.Build(data, null);
            PlayerSave save = PlayerSave.CreateNew();

            CosmeticOption look = CosmeticRules.RollDrop(table, _library, save, "squad", 5, new System.Random(4));
            Assert.IsNotNull(look);
            Assert.AreEqual(CosmeticLibrary.SourceDrop, look.Source);
            Assert.AreEqual(1, look.MinRegion, "level 5 is region 1");
            Assert.IsNull(CosmeticRules.RollDrop(table, _library, save, "elite", 5, new System.Random(4)), "no chance for elite in this table");

            int owned = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                CosmeticOption again = CosmeticRules.RollDrop(table, _library, save, "squad", 95, new System.Random(seed));
                if (again != null)
                {
                    Assert.IsTrue(CosmeticRules.Unlock(save, _library, again.Key), "never a look already owned");
                    owned++;
                }
            }

            Assert.AreEqual(_library.Pool(CosmeticLibrary.SourceDrop, 10).Count, owned, "the pool runs dry, then nothing drops");
            Assert.AreEqual(1, CosmeticLibrary.RegionOfLevel(10));
            Assert.AreEqual(2, CosmeticLibrary.RegionOfLevel(11));
            Assert.AreEqual(10, CosmeticLibrary.RegionOfLevel(100));
        }

        [Test]
        public void FirstLairClear_UnlocksItsBossLooks_AndTheFirstBossMilestone()
        {
            RegionLibrary regions = RegionLibrary.Build(CampaignMapTests.LoadRegions());
            EconomyContent economy = new EconomyContent { Cosmetics = _library };
            PlayerSave save = PlayerSave.CreateNew();
            save.Campaign.FindRegion("r01").StagesCleared = 3;
            Assert.IsTrue(CampaignRules.StartRun(save, regions, "r01", 3, 77).Success);

            CampaignResult lair = null;
            while (lair == null)
            {
                MapNode node = CampaignRules.Choices(save.Campaign.ActiveRun)[0];
                if (!node.IsBattle)
                {
                    if (save.FindBeast("walker") == null)
                    {
                        save.Beasts.Add(OwnedBeast.Create("walker", "kirin", 1));
                    }

                    Assert.IsTrue((node.Type == MapNodeType.Rest ? CampaignRules.Camp(save, regions, node.NodeId, "walker") : CampaignRules.Trade(save, regions, node.NodeId, null)).Success);
                    continue;
                }

                CampaignResult result = CampaignRules.ResolveBattle(save, regions, node.NodeId, BattleOutcome.PlayerVictory, economy);
                lair = node.Type == MapNodeType.Boss ? result : null;
            }

            CollectionAssert.Contains(lair.CosmeticsUnlocked, "kirin_mane/lair");
            CollectionAssert.Contains(lair.CosmeticsUnlocked, "avatar_headwear/champions_laurel");
            Assert.IsTrue(save.Cosmetics.Has("kirin_mane/lair"));
        }

        private IEnumerable<CosmeticOption> AllOptions()
        {
            foreach (CosmeticCategory category in _library.Categories)
            {
                foreach (CosmeticOption option in category.Options)
                {
                    yield return option;
                }
            }
        }
    }
}
