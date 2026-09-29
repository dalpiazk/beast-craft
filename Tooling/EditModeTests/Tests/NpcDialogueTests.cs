using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Npc;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Tutorial;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The NPC dialogue layer (docs/design/grove.md, "NPCs" — D2): condition resolution and its
    /// priority/specificity tie-break (<see cref="DialogueBook.Resolve"/>, unchanged from the existing
    /// scene mechanism), the fact builder (<see cref="NpcRules.BuildFacts"/>), requests
    /// (<see cref="NpcRules.IsRequestAvailable"/>/<see cref="NpcRules.FulfillRequest"/>: consume,
    /// refuse, once-only) and side stories (<see cref="NpcRules.CurrentChapter"/>/
    /// <see cref="NpcRules.FulfillChapter"/>: chapter progression, determinism), plus the authored
    /// <c>dialogue.json</c> and <c>achievements.json</c> against their validators.
    /// </summary>
    public class NpcDialogueTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static PlayerSave SaveWithBeast(string beastId = "b1", string speciesId = "phoenix")
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create(beastId, speciesId, 10));
            return save;
        }

        // ---- Authored content ----

        [Test]
        public void AuthoredDialogue_IsValid_AndCoversTheV1Cast()
        {
            DialogueLibraryData dialogue = FieldJsonHelper.Read<DialogueLibraryData>(DialogueLibraryData.ProjectRelativePath);
            GroveLibraryData grove = FieldJsonHelper.Read<GroveLibraryData>(GroveLibraryData.ProjectRelativePath);
            GardenLibraryData garden = FieldJsonHelper.Read<GardenLibraryData>(GardenLibraryData.ProjectRelativePath);
            ExpeditionLibraryData expedition = FieldJsonHelper.Read<ExpeditionLibraryData>(ExpeditionLibraryData.ProjectRelativePath);
            CosmeticLibraryData cosmetics = FieldJsonHelper.Read<CosmeticLibraryData>(CosmeticLibraryData.ProjectRelativePath);

            List<string> errors = DialogueValidator.Validate(dialogue);
            errors.AddRange(DialogueValidator.ValidateRequestsAndSideStories(dialogue, grove, garden, expedition, cosmetics));
            Assert.IsEmpty(errors, string.Join("\n", errors));

            Assert.AreEqual(4, dialogue.Npcs.Length, "Trader, Grove Keeper, Wandering Scholar, Forest Folk");
            Assert.That(dialogue.Requests.Length, Is.GreaterThanOrEqualTo(4));
            Assert.That(dialogue.SideStories.Length, Is.GreaterThanOrEqualTo(3));
            foreach (SideStoryData story in dialogue.SideStories)
            {
                Assert.That(story.Chapters.Length, Is.InRange(3, 4), story.StoryId);
            }

            foreach (string npcId in new[] { "trader", "grove_keeper", "wandering_scholar", "forest_folk" })
            {
                int lines = 0;
                foreach (DialogueLineData line in dialogue.Lines)
                {
                    if (line.NpcId == npcId)
                    {
                        lines++;
                    }
                }

                Assert.That(lines, Is.GreaterThanOrEqualTo(10), npcId + " needs ~10-15 lines");
            }
        }

        [Test]
        public void EveryCastNpc_HasADefaultLine_ThatResolvesWhenNoFactsHold()
        {
            foreach (string npcId in new[] { "trader", "grove_keeper", "wandering_scholar", "forest_folk" })
            {
                DialogueLineData resolved = Content.Dialogue.Resolve(npcId, new HashSet<string>());
                Assert.IsNotNull(resolved, npcId + " has no default line");
                Assert.IsEmpty(resolved.Conditions, npcId + "'s default line must have no conditions");
            }
        }

        [Test]
        public void AchievementsAuthoredForTheSideStoryArcs()
        {
            AchievementLibraryData achievements = FieldJsonHelper.Read<AchievementLibraryData>(AchievementLibraryData.ProjectRelativePath);
            int found = 0;
            foreach (AchievementData a in achievements.Achievements)
            {
                if (a.Kind == AchievementKinds.SideStoryComplete)
                {
                    found++;
                    Assert.IsNotEmpty(a.StoryId);
                }
            }

            Assert.AreEqual(3, found, "one achievement per authored side story arc");
        }

        [Test]
        public void NpcLoreEntries_ListsEveryEntry_FoundOnlyOnceRecorded()
        {
            PlayerSave save = SaveWithBeast();
            List<Discovery.CompendiumLoreEntry> before = Discovery.CompendiumRules.NpcLoreEntries(save, Content.Dialogue);
            Assert.That(before.Count, Is.GreaterThanOrEqualTo(15));
            Assert.IsFalse(before.Exists(e => e.Found));

            string firstId = before[0].LoreId;
            save.Npc.LoreIds.Add(firstId);
            List<Discovery.CompendiumLoreEntry> after = Discovery.CompendiumRules.NpcLoreEntries(save, Content.Dialogue);
            Assert.IsTrue(after.Find(e => e.LoreId == firstId).Found);
            Assert.AreEqual(1, after.FindAll(e => e.Found).Count);
        }

        // ---- Resolution: priority and specificity ----

        private static DialogueLibraryData SyntheticDialogue()
        {
            return new DialogueLibraryData
            {
                SchemaVersion = 1,
                Npcs = new[] { new NpcData { NpcId = "n1", DisplayName = "N1" } },
                Lines = new[]
                {
                    new DialogueLineData { LineId = "default", NpcId = "n1", Conditions = new string[0], Text = "Default line.", Priority = -1 },
                    new DialogueLineData { LineId = "low", NpcId = "n1", Conditions = new[] { "habitat_unlocked:glade" }, Text = "Low priority.", Priority = 0 },
                    new DialogueLineData { LineId = "high", NpcId = "n1", Conditions = new[] { "grove_lore_found:x" }, Text = "High priority.", Priority = 1 },
                    new DialogueLineData
                    {
                        LineId = "specific", NpcId = "n1", Conditions = new[] { "habitat_unlocked:glade", "variety_count:5" }, Text = "Two conditions, same priority as low.",
                        Priority = 0
                    }
                }
            };
        }

        [Test]
        public void Resolve_NoFactsHold_ReturnsTheDefaultLine()
        {
            DialogueBook book = DialogueBook.Build(SyntheticDialogue());
            DialogueLineData resolved = book.Resolve("n1", new HashSet<string>());
            Assert.AreEqual("default", resolved.LineId);
        }

        [Test]
        public void Resolve_HighestPriorityAmongHoldingLinesWins()
        {
            DialogueBook book = DialogueBook.Build(SyntheticDialogue());
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:glade", "grove_lore_found:x" };
            DialogueLineData resolved = book.Resolve("n1", facts);
            Assert.AreEqual("high", resolved.LineId, "high (priority 1) beats low and specific (priority 0)");
        }

        [Test]
        public void Resolve_TiesOnPriority_BrokenByMoreConditions()
        {
            DialogueBook book = DialogueBook.Build(SyntheticDialogue());
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:glade", "variety_count:5" };
            DialogueLineData resolved = book.Resolve("n1", facts);
            Assert.AreEqual("specific", resolved.LineId, "same priority as 'low', but two conditions beats one");
        }

        [Test]
        public void Resolve_UnknownNpc_ReturnsNull()
        {
            DialogueBook book = DialogueBook.Build(SyntheticDialogue());
            Assert.IsNull(book.Resolve("nobody", new HashSet<string>()));
        }

        [Test]
        public void ResolveAndMark_RecordsTheLineSeen_Once()
        {
            PlayerSave save = SaveWithBeast();
            DialogueBook book = DialogueBook.Build(SyntheticDialogue());

            DialogueLineData first = NpcRules.ResolveAndMark(save, book, "n1", new HashSet<string>());
            DialogueLineData second = NpcRules.ResolveAndMark(save, book, "n1", new HashSet<string>());

            Assert.AreEqual("default", first.LineId);
            Assert.AreEqual("default", second.LineId);
            Assert.AreEqual(1, save.Npc.Dialogue.LinesSeen.Count, "seen once, not twice");
            CollectionAssert.Contains(save.Npc.Dialogue.LinesSeen, "default");
        }

        // ---- BuildFacts ----

        [Test]
        public void BuildFacts_IsPureAndDeterministic_SameSaveSameFacts()
        {
            PlayerSave save = SaveWithBeast();
            GroveRules.RefreshUnlocks(save, Content.GroveLibrary);
            GroveRules.Feed(save, Content.GroveLibrary, "b1", System.DateTime.UtcNow, System.TimeSpan.FromHours(1));

            HashSet<string> a = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);
            HashSet<string> b = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);

            CollectionAssert.AreEquivalent(a, b);
        }

        [Test]
        public void BuildFacts_EmitsHabitatAndAffinityTierFacts_CascadingUpToTheCurrentTier()
        {
            PlayerSave save = SaveWithBeast("b1", "phoenix");
            save.Discovery.GroveUnlockIds.Add("grove_habitat_mossy_glade");
            GroveRules.RefreshUnlocks(save, Content.GroveLibrary);
            save.Grove.AffinityOf("b1").Tier = 3;

            HashSet<string> facts = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);

            CollectionAssert.Contains(facts, "affinity_tier_any:1");
            CollectionAssert.Contains(facts, "affinity_tier_any:2");
            CollectionAssert.Contains(facts, "affinity_tier_any:3");
            CollectionAssert.DoesNotContain(facts, "affinity_tier_any:4");
            CollectionAssert.Contains(facts, "affinity_tier_species:phoenix:3");
            CollectionAssert.Contains(facts, "habitat_unlocked:mossy_glade");
        }

        [Test]
        public void BuildFacts_EmitsRegionCleared_OnlyAfterTheBossIsCleared()
        {
            PlayerSave save = SaveWithBeast();
            save.Campaign.Unlock("r01");

            HashSet<string> before = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);
            CollectionAssert.DoesNotContain(before, "region_cleared:r01");

            save.Campaign.FindRegion("r01").BossCleared = true;
            HashSet<string> after = NpcRules.BuildFacts(save, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary);
            CollectionAssert.Contains(after, "region_cleared:r01");
        }

        [Test]
        public void BuildFacts_NullSave_ReturnsEmpty()
        {
            Assert.IsEmpty(NpcRules.BuildFacts(null, Content.GroveLibrary, Content.GardenLibrary, Content.ExpeditionLibrary));
        }

        // ---- Requests ----

        private static RequestData SyntheticRequest()
        {
            return new RequestData
            {
                RequestId = "req_test",
                NpcId = "trader",
                Conditions = new[] { "habitat_unlocked:mossy_glade" },
                ItemId = "sunpetal_pure",
                Count = 2,
                RewardKind = "lore",
                RewardId = "trader_lore_market",
                Text = "Bring me two sunpetal blooms."
            };
        }

        [Test]
        public void IsRequestAvailable_FalseWithoutConditions_TrueOnceTheyHold()
        {
            PlayerSave save = SaveWithBeast();
            RequestData request = SyntheticRequest();

            Assert.IsFalse(NpcRules.IsRequestAvailable(save, request, new HashSet<string>()));
            Assert.IsTrue(NpcRules.IsRequestAvailable(save, request, new HashSet<string> { "habitat_unlocked:mossy_glade" }));
        }

        [Test]
        public void CanFulfillRequest_RequiresEnoughOfTheItem()
        {
            PlayerSave save = SaveWithBeast();
            RequestData request = SyntheticRequest();
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };

            Assert.IsFalse(NpcRules.CanFulfillRequest(save, request, facts), "no item held yet");

            save.Grove.Items.Add("sunpetal_pure", 1);
            Assert.IsFalse(NpcRules.CanFulfillRequest(save, request, facts), "only 1 of 2 needed");

            save.Grove.Items.Add("sunpetal_pure", 1);
            Assert.IsTrue(NpcRules.CanFulfillRequest(save, request, facts));
        }

        [Test]
        public void FulfillRequest_ConsumesTheItem_GrantsLore_AndRecordsFulfilled_Once()
        {
            PlayerSave save = SaveWithBeast();
            RequestData request = SyntheticRequest();
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };
            save.Grove.Items.Add("sunpetal_pure", 2);

            NpcActionResult result = NpcRules.FulfillRequest(save, request, facts);

            Assert.IsTrue(result.Success);
            Assert.AreEqual("lore", result.RewardKind);
            Assert.AreEqual("trader_lore_market", result.RewardId);
            Assert.AreEqual(0, save.Grove.Items.GetCount("sunpetal_pure"));
            CollectionAssert.Contains(save.Npc.LoreIds, "trader_lore_market");
            Assert.IsTrue(NpcRules.IsRequestFulfilled(save, request));

            NpcActionResult again = NpcRules.FulfillRequest(save, request, facts);
            Assert.IsFalse(again.Success, "a request is fulfilled once, forever");
        }

        [Test]
        public void FulfillRequest_RefusesWithoutEnoughItem_AndChangesNothing()
        {
            PlayerSave save = SaveWithBeast();
            RequestData request = SyntheticRequest();
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };

            NpcActionResult result = NpcRules.FulfillRequest(save, request, facts);

            Assert.IsFalse(result.Success);
            Assert.IsFalse(NpcRules.IsRequestFulfilled(save, request));
            Assert.IsEmpty(save.Npc.LoreIds);
        }

        // ---- Side stories ----

        private static SideStoryData SyntheticStory()
        {
            return new SideStoryData
            {
                StoryId = "story_test",
                NpcId = "trader",
                DisplayName = "Test Story",
                Chapters = new[]
                {
                    new SideStoryChapterData
                    {
                        ChapterId = "c1", Conditions = new string[0], Text = "Chapter one.", RequestItemId = "", RequestCount = 0, RewardKind = "none", RewardId = "",
                        LoreId = "lore_c1", NextChapterId = "c2"
                    },
                    new SideStoryChapterData
                    {
                        ChapterId = "c2", Conditions = new[] { "habitat_unlocked:mossy_glade" }, Text = "Chapter two.", RequestItemId = "sunpetal_pure", RequestCount = 1,
                        RewardKind = "none", RewardId = "", LoreId = "lore_c2", NextChapterId = "c3"
                    },
                    new SideStoryChapterData
                    {
                        ChapterId = "c3", Conditions = new string[0], Text = "Chapter three.", RequestItemId = "", RequestCount = 0, RewardKind = "none", RewardId = "",
                        LoreId = "lore_c3", NextChapterId = ""
                    }
                }
            };
        }

        [Test]
        public void CurrentChapter_StartsAtTheEntryChapter_AndAdvancesAsChaptersComplete()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();

            Assert.AreEqual("c1", NpcRules.CurrentChapter(save, story).ChapterId);

            NpcRules.FulfillChapter(save, story, new HashSet<string>());
            Assert.AreEqual("c2", NpcRules.CurrentChapter(save, story).ChapterId);
        }

        [Test]
        public void IsChapterAvailable_FalseUntilItsConditionsHold()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();
            NpcRules.FulfillChapter(save, story, new HashSet<string>()); // completes c1

            Assert.IsFalse(NpcRules.IsChapterAvailable(save, story, new HashSet<string>()), "c2 needs the habitat condition");
            Assert.IsTrue(NpcRules.IsChapterAvailable(save, story, new HashSet<string> { "habitat_unlocked:mossy_glade" }));
        }

        [Test]
        public void CanFulfillChapter_RequiresTheRequestItem_WhenOneIsSet()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();
            NpcRules.FulfillChapter(save, story, new HashSet<string>()); // completes c1
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };

            Assert.IsFalse(NpcRules.CanFulfillChapter(save, story, facts), "c2 needs sunpetal_pure");
            save.Grove.Items.Add("sunpetal_pure", 1);
            Assert.IsTrue(NpcRules.CanFulfillChapter(save, story, facts));
        }

        [Test]
        public void FulfillChapter_ConsumesTheItem_GrantsLore_AndAdvancesTheChain()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };

            NpcActionResult c1 = NpcRules.FulfillChapter(save, story, facts);
            Assert.IsTrue(c1.Success);
            Assert.AreEqual("lore_c1", c1.LoreId);
            CollectionAssert.Contains(save.Npc.LoreIds, "lore_c1");

            save.Grove.Items.Add("sunpetal_pure", 1);
            NpcActionResult c2 = NpcRules.FulfillChapter(save, story, facts);
            Assert.IsTrue(c2.Success);
            Assert.AreEqual(0, save.Grove.Items.GetCount("sunpetal_pure"), "the chapter's item request is consumed");
            CollectionAssert.Contains(save.Npc.LoreIds, "lore_c2");

            Assert.IsFalse(NpcRules.IsSideStoryComplete(save, story), "c3 remains");
            NpcRules.FulfillChapter(save, story, facts);
            Assert.IsTrue(NpcRules.IsSideStoryComplete(save, story));
            Assert.IsNull(NpcRules.CurrentChapter(save, story), "nothing left once complete");
        }

        [Test]
        public void FulfillChapter_RefusesWhenNotAvailable_AndChangesNothing()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();

            NpcActionResult refused = NpcRules.FulfillChapter(save, story, new HashSet<string>()); // c1 has no conditions, so this succeeds
            Assert.IsTrue(refused.Success);

            // c2 needs the habitat condition, which does not hold.
            NpcActionResult stillRefused = NpcRules.FulfillChapter(save, story, new HashSet<string>());
            Assert.IsFalse(stillRefused.Success);
            Assert.AreEqual("c2", NpcRules.CurrentChapter(save, story).ChapterId, "no progress made");
        }

        [Test]
        public void IsSideStoryComplete_IsDeterministic_GivenTheSameProgress()
        {
            PlayerSave save = SaveWithBeast();
            SideStoryData story = SyntheticStory();
            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };
            save.Grove.Items.Add("sunpetal_pure", 1);
            NpcRules.FulfillChapter(save, story, facts);
            NpcRules.FulfillChapter(save, story, facts);
            NpcRules.FulfillChapter(save, story, facts);

            Assert.IsTrue(NpcRules.IsSideStoryComplete(save, story));
            Assert.IsTrue(NpcRules.IsSideStoryComplete(save, story), "calling it again reads the same answer");
        }

        // ---- Achievement hook ----

        [Test]
        public void SideStoryComplete_Achievement_EarnedOnceEveryChapterIsDone()
        {
            PlayerSave save = SaveWithBeast();
            DialogueLibraryData data = SyntheticDialogue();
            data.SideStories = new[] { SyntheticStory() };
            DialogueBook book = DialogueBook.Build(data);

            AchievementData def = new AchievementData
            {
                AchievementId = "test_story",
                DisplayName = "Test",
                Kind = AchievementKinds.SideStoryComplete,
                StoryId = "story_test",
                TitleId = "title_test_story",
                TitleText = "Storied"
            };
            AchievementContent content = new AchievementContent
            {
                Library = AchievementLibrary.Build(new AchievementLibraryData { SchemaVersion = 1, Achievements = new[] { def } }),
                Discovery = Content.Discovery,
                Dialogue = book
            };

            Assert.IsEmpty(AchievementRules.Evaluate(save, content));

            HashSet<string> facts = new HashSet<string> { "habitat_unlocked:mossy_glade" };
            save.Grove.Items.Add("sunpetal_pure", 1);
            NpcRules.FulfillChapter(save, SyntheticStory(), facts);
            NpcRules.FulfillChapter(save, SyntheticStory(), facts);
            NpcRules.FulfillChapter(save, SyntheticStory(), facts);

            List<AchievementData> earned = AchievementRules.Evaluate(save, content);
            Assert.AreEqual(1, earned.Count);
            Assert.AreEqual("test_story", earned[0].AchievementId);
            Assert.IsTrue(save.Achievements.HasEarned("test_story"));
        }

        // ---- Validator mistakes ----

        [Test]
        public void ConditionKinds_UnknownKindIsCaught()
        {
            DialogueLibraryData data = SyntheticDialogue();
            data.Lines[1].Conditions = new[] { "not_a_real_kind:whatever" };

            List<string> errors = DialogueValidator.Validate(data);
            StringAssert.Contains("unknown kind", string.Join("\n", errors));
        }

        [Test]
        public void RequestsAndSideStories_ValidatorCatchesAuthoringMistakes()
        {
            DialogueLibraryData data = SyntheticDialogue();
            RequestData badItem = SyntheticRequest();
            badItem.ItemId = "not_a_grove_item";
            RequestData badNpc = SyntheticRequest();
            badNpc.RequestId = "req_test_2";
            badNpc.NpcId = "no_such_npc";
            data.Requests = new[] { badItem, badNpc };

            SideStoryChapterData[] cyclic =
            {
                new SideStoryChapterData { ChapterId = "x1", Text = "One.", RewardKind = "none", NextChapterId = "x2" },
                new SideStoryChapterData { ChapterId = "x2", Text = "Two.", RewardKind = "none", NextChapterId = "x1" }
            };
            data.SideStories = new[] { new SideStoryData { StoryId = "story_bad", NpcId = "trader", DisplayName = "Bad", Chapters = cyclic } };

            List<string> errors = DialogueValidator.ValidateRequestsAndSideStories(data, null, null, null, null);

            StringAssert.Contains("not a Grove item", string.Join("\n", errors));
            StringAssert.Contains("unknown NpcId", string.Join("\n", errors));
            StringAssert.Contains("entry chapter", string.Join("\n", errors));
        }

        [Test]
        public void SideStory_ChapterChain_MustBeAcyclicAndFullyReachable()
        {
            SideStoryChapterData[] orphaned =
            {
                new SideStoryChapterData { ChapterId = "a", Text = "A.", RewardKind = "none", NextChapterId = "" },
                new SideStoryChapterData { ChapterId = "b", Text = "B.", RewardKind = "none", NextChapterId = "" }
            };
            DialogueLibraryData data = SyntheticDialogue();
            data.SideStories = new[] { new SideStoryData { StoryId = "story_orphan", NpcId = "n1", DisplayName = "Orphan", Chapters = orphaned } };

            List<string> errors = DialogueValidator.ValidateRequestsAndSideStories(data, null, null, null, null);

            StringAssert.Contains("entry chapter", string.Join("\n", errors), "two chapters with no NextChapterId link means two entry candidates");
        }

        [Test]
        public void SideStoryChapter_RewardKindLore_IsRejected_UseLoreIdInstead()
        {
            SideStoryChapterData[] chapters =
            {
                new SideStoryChapterData { ChapterId = "a", Text = "A.", RewardKind = "lore", RewardId = "x", NextChapterId = "" }
            };
            DialogueLibraryData data = SyntheticDialogue();
            data.SideStories = new[] { new SideStoryData { StoryId = "story_lore", NpcId = "n1", DisplayName = "Lore", Chapters = chapters } };

            List<string> errors = DialogueValidator.ValidateRequestsAndSideStories(data, null, null, null, null);

            StringAssert.Contains("may not be 'lore'", string.Join("\n", errors));
        }
    }
}
