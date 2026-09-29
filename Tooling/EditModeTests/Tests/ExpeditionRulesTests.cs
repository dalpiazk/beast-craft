using System;
using System.Collections.Generic;
using BeastCraft.Expeditions;
using BeastCraft.Grove;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The Board (docs/design/grove.md): sending a party (<see cref="ExpeditionRules.Send"/> — party
    /// size, one expedition per destination at a time, and the producer decision that a sent beast
    /// stays fully available for battle: nothing here locks it), waiting through
    /// <c>OfflineClock</c> (<see cref="ExpeditionRules.TimeRemainingHours"/>,
    /// <see cref="ExpeditionRules.IsReturned"/>) and collecting an outcome
    /// (<see cref="ExpeditionRules.Collect"/>: the seeded roll and its pity guarantee). No combat
    /// power anywhere. Also the authored <c>expedition-library.json</c> against
    /// <see cref="ExpeditionLibraryValidator"/>.
    /// </summary>
    public class ExpeditionRulesTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan M0 = TimeSpan.FromHours(3);

        internal static ExpeditionLibraryData LoadExpeditions()
        {
            return EncounterContentTests.Load<ExpeditionLibraryData>(ExpeditionLibraryData.ProjectRelativePath);
        }

        private static ExpeditionLibraryData SyntheticData()
        {
            return new ExpeditionLibraryData
            {
                SchemaVersion = 1,
                Destinations = new[]
                {
                    new DestinationData { DestinationId = "meadow", DisplayName = "Meadow", UnlockSource = "start", UnlockId = "", DurationHours = 2, PartySize = 2, ArtKey = "a" },
                    new DestinationData { DestinationId = "ridge", DisplayName = "Ridge", UnlockSource = "habitat", UnlockId = "glade", DurationHours = 4, PartySize = 1, ArtKey = "a" }
                },
                OutcomeTables = new[]
                {
                    new ExpeditionOutcomeTableData
                    {
                        DestinationId = "meadow",
                        PityAt = 3,
                        Entries = new[]
                        {
                            new ExpeditionOutcomeEntryData { Kind = "trinket", Id = "trinket_leaf", Weight = 95, IsCommon = true },
                            new ExpeditionOutcomeEntryData { Kind = "story", Id = "story_meadow", Weight = 5, IsCommon = false }
                        }
                    },
                    new ExpeditionOutcomeTableData
                    {
                        DestinationId = "ridge",
                        PityAt = 3,
                        Entries = new[] { new ExpeditionOutcomeEntryData { Kind = "trinket", Id = "trinket_stone", Weight = 1, IsCommon = true } }
                    }
                },
                Stories = new[] { new LoreStoryData { StoryId = "story_meadow", Text = "A quiet find.", CodexCategory = "Wilds" } }
            };
        }

        private static PlayerSave SaveWithBeasts(params string[] beastIds)
        {
            PlayerSave save = PlayerSave.CreateNew();
            foreach (string id in beastIds)
            {
                save.Beasts.Add(OwnedBeast.Create(id, "phoenix", 10));
            }

            return save;
        }

        // ---- Content ----

        [Test]
        public void AuthoredLibrary_IsValid_AndHas8Destinations()
        {
            ExpeditionLibraryData data = LoadExpeditions();
            GroveLibraryData grove = GroveRulesTests.LoadGrove();

            List<string> errors = ExpeditionLibraryValidator.Validate(data, grove, null);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.AreEqual(8, data.Destinations.Length);
            Assert.AreEqual(8, data.OutcomeTables.Length);
        }

        // ---- Send ----

        [Test]
        public void Send_RefusesLockedDestination_EmptyOrOversizedParty_OrASecondSendToTheSameDestination()
        {
            PlayerSave save = SaveWithBeasts("b1", "b2", "b3");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());

            Assert.IsFalse(ExpeditionRules.Send(save, library, "ridge", new[] { "b1" }, T0, M0).Success, "ridge needs the glade habitat");
            Assert.IsFalse(ExpeditionRules.Send(save, library, "meadow", new string[0], T0, M0).Success, "empty party");
            Assert.IsFalse(ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2", "b3" }, T0, M0).Success, "meadow's PartySize is 2");

            Assert.IsTrue(ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0).Success);
            Assert.IsFalse(ExpeditionRules.Send(save, library, "meadow", new[] { "b3" }, T0, M0).Success, "an expedition is already away at meadow");
        }

        [Test]
        public void Send_UnlocksTheHabitatGatedDestination_OnceItsHabitatIsUnlocked()
        {
            PlayerSave save = SaveWithBeasts("b1");
            save.Grove.HabitatsUnlocked.Add("glade");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());

            Assert.IsTrue(ExpeditionRules.Send(save, library, "ridge", new[] { "b1" }, T0, M0).Success);
        }

        [Test]
        public void Send_NeverLocksTheBeasts_TheyStayFullyAvailable()
        {
            PlayerSave save = SaveWithBeasts("b1", "b2");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());

            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0);

            // Nothing on OwnedBeast or elsewhere marks a beast "away": it stays a normal roster member,
            // eligible for a battle team or the idle party, exactly as the producer decided.
            Assert.IsNotNull(save.FindBeast("b1"));
            Assert.IsNotNull(save.FindBeast("b2"));
            Assert.AreEqual(2, save.Beasts.Count);
        }

        [Test]
        public void Send_IgnoresUnknownAndRepeatedBeastIds()
        {
            PlayerSave save = SaveWithBeasts("b1");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());

            ExpeditionActionResult result = ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b1", "unknown" }, T0, M0);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(1, save.Expeditions.FindActive("meadow").BeastIds.Count);
        }

        // ---- Timing / collect ----

        [Test]
        public void TimeRemaining_CountsDownToZero_CheckedThroughOfflineClock()
        {
            PlayerSave save = SaveWithBeasts("b1", "b2");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());
            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0);
            ActiveExpedition active = save.Expeditions.FindActive("meadow");

            Assert.IsFalse(ExpeditionRules.IsReturned(library, active, T0 + TimeSpan.FromHours(1), M0 + TimeSpan.FromHours(1)));
            Assert.AreEqual(1.0, ExpeditionRules.TimeRemainingHours(library, active, T0 + TimeSpan.FromHours(1), M0 + TimeSpan.FromHours(1)), 0.001);
            Assert.IsTrue(ExpeditionRules.IsReturned(library, active, T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2)));
        }

        [Test]
        public void Collect_RefusesBeforeReturn_ThenRollsAnOutcome_AndRemovesTheActiveExpedition()
        {
            PlayerSave save = SaveWithBeasts("b1", "b2");
            ExpeditionLibrary library = ExpeditionLibrary.Build(SyntheticData());
            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0);

            Assert.IsFalse(ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(1), M0 + TimeSpan.FromHours(1)).Success);

            ExpeditionCollectResult result = ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2));

            Assert.IsTrue(result.Success);
            Assert.IsNull(save.Expeditions.FindActive("meadow"));
            Assert.IsTrue(result.Kind == "trinket" || result.Kind == "story");
        }

        [Test]
        public void Collect_TrinketOutcome_DepositsAGroveItem()
        {
            PlayerSave save = SaveWithBeasts("b1");
            ExpeditionLibraryData data = SyntheticData();
            // Force a deterministic "always trinket" table.
            data.OutcomeTables[0] = new ExpeditionOutcomeTableData
            {
                DestinationId = "meadow",
                PityAt = 5,
                Entries = new[] { new ExpeditionOutcomeEntryData { Kind = "trinket", Id = "trinket_leaf", Weight = 1, IsCommon = true } }
            };
            ExpeditionLibrary library = ExpeditionLibrary.Build(data);
            ExpeditionRules.Send(save, library, "meadow", new[] { "b1" }, T0, M0);

            ExpeditionCollectResult result = ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2));

            Assert.AreEqual("trinket", result.Kind);
            Assert.AreEqual(1, save.Grove.Items.GetCount("trinket_leaf"));
        }

        [Test]
        public void Collect_StoryOutcome_NewStoryOnlyTheFirstTime()
        {
            PlayerSave save = SaveWithBeasts("b1", "b2");
            ExpeditionLibraryData data = SyntheticData();
            data.OutcomeTables[0] = new ExpeditionOutcomeTableData
            {
                DestinationId = "meadow",
                PityAt = 5,
                Entries = new[] { new ExpeditionOutcomeEntryData { Kind = "story", Id = "story_meadow", Weight = 1, IsCommon = true } }
            };
            ExpeditionLibrary library = ExpeditionLibrary.Build(data);

            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0);
            ExpeditionCollectResult first = ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2));
            Assert.IsTrue(first.NewStory);
            CollectionAssert.Contains(save.Expeditions.StoriesUnlocked, "story_meadow");

            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2));
            ExpeditionCollectResult second = ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(4), M0 + TimeSpan.FromHours(4));
            Assert.IsFalse(second.NewStory);
        }

        [Test]
        public void Collect_PityGuaranteesANonCommonOutcome_OnceTheCounterReachesPityAtMinusOne()
        {
            // meadow: 95 common trinket / 5 non-common story, PityAt 3. A destination's pity counter is
            // persisted on ExpeditionProgress (ExpeditionRules.Collect), independent of who is sent.
            ExpeditionLibraryData data = SyntheticData();
            ExpeditionLibrary library = ExpeditionLibrary.Build(data);
            PlayerSave save = SaveWithBeasts("b1", "b2");
            ExpeditionPityCounter pity = save.Expeditions.PityFor("meadow");
            pity.Misses = data.OutcomeTables[0].PityAt - 1;

            ExpeditionRules.Send(save, library, "meadow", new[] { "b1", "b2" }, T0, M0);
            ExpeditionCollectResult forced = ExpeditionRules.Collect(save, library, "meadow", T0 + TimeSpan.FromHours(2), M0 + TimeSpan.FromHours(2));

            Assert.AreEqual("story", forced.Kind, "pity must force the non-common entry once the counter is at PityAt - 1");
        }
    }
}
