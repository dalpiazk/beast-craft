using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The validator's checks on the schema 11 and 12 fields: the crash refund record, the "new" markers' seen list and
    /// the post-game difficulty preference.
    /// </summary>
    public class SaveValidatorFollowUpTests
    {
        private static List<SaveIssue> Issues(PlayerSave save, string pathPrefix, ISaveEconomyCatalog economy = null)
        {
            return SaveValidator.Validate(save, null, null, economy).FindAll(i => i.Path.StartsWith(pathPrefix, System.StringComparison.Ordinal));
        }

        [Test]
        public void ACleanSave_HasNoIssuesInTheNewFields()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.PendingBattleConsumables.Add("fury_draught");
            save.PendingBattleConsumables.Add("fury_draught");
            save.Seen.Gear.Add("g1");
            save.Seen.Looks.Add("hat/crown");
            save.Campaign.PreferredDifficulty = RunDifficulty.Hard;

            Assert.IsEmpty(SaveValidator.Validate(save, null, null, new EconomySaveTests.FakeEconomy()), "a repeated refund entry is two units owed, not an error");
        }

        [Test]
        public void TheRefundRecord_ReportsEmptyAndUnknownEntries()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.PendingBattleConsumables.Add("fury_draught");
            save.PendingBattleConsumables.Add(null);
            save.PendingBattleConsumables.Add(string.Empty);
            save.PendingBattleConsumables.Add("mystery");

            List<SaveIssue> withCatalog = Issues(save, "PendingBattleConsumables", new EconomySaveTests.FakeEconomy());
            Assert.AreEqual(3, withCatalog.Count, string.Join("\n", withCatalog));
            Assert.AreEqual(2, withCatalog.FindAll(i => i.Kind == SaveIssueKind.InvalidValue).Count);
            Assert.AreEqual("PendingBattleConsumables[3]", withCatalog.Find(i => i.Kind == SaveIssueKind.UnknownConsumable).Path);

            Assert.AreEqual(2, Issues(save, "PendingBattleConsumables").Count, "without a catalog only the empty entries");
        }

        [Test]
        public void TheSeenList_ReportsEmptyAndDuplicateEntries()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Seen.Gear.AddRange(new[] { "g1", null, "g1" });
            save.Seen.Looks.AddRange(new[] { "hat/crown", string.Empty, "hat/crown", "hat/cap" });

            List<SaveIssue> gear = Issues(save, "Seen.Gear");
            List<SaveIssue> looks = Issues(save, "Seen.Looks");

            CollectionAssert.AreEqual(new[] { "Seen.Gear[1]", "Seen.Gear[2]" }, gear.ConvertAll(i => i.Path));
            CollectionAssert.AreEqual(new[] { "Seen.Looks[1]", "Seen.Looks[2]" }, looks.ConvertAll(i => i.Path));
            Assert.IsTrue(gear.TrueForAll(i => i.Kind == SaveIssueKind.InvalidValue));
        }

        [Test]
        public void ThePreferredDifficulty_MustBeADefinedValue()
        {
            PlayerSave save = PlayerSave.CreateNew();
            Assert.IsEmpty(Issues(save, "Campaign.PreferredDifficulty"));

            save.Campaign.PreferredDifficulty = (RunDifficulty)7;
            List<SaveIssue> issues = Issues(save, "Campaign.PreferredDifficulty");

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SaveIssueKind.InvalidValue, issues[0].Kind);

            save.EnsureInitialized();
            Assert.AreEqual(RunDifficulty.Normal, save.Campaign.PreferredDifficulty, "loading repairs it to Normal");
            Assert.IsEmpty(Issues(save, "Campaign.PreferredDifficulty"));
        }
    }
}
