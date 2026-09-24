using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Economy;
using BeastCraft.Save;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Consumables: the authored <c>Data/Items/consumable-library.json</c> and
    /// <see cref="ConsumableLibraryValidator"/>'s rules, the pack (<see cref="ConsumableInventory"/>)
    /// and <see cref="ConsumableLoadout"/>'s checks. The battle flow (applied, spent once on any
    /// outcome) is in <see cref="BattleSessionTests"/>.
    /// </summary>
    public class ConsumableTests
    {
        internal static ConsumableLibraryData LoadConsumables()
        {
            return EncounterContentTests.Load<ConsumableLibraryData>(ConsumableLibraryData.ProjectRelativePath);
        }

        [Test]
        public void AuthoredLibrary_IsValid()
        {
            ConsumableLibraryData data = LoadConsumables();
            List<string> errors = ConsumableLibraryValidator.Validate(data);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            ConsumableLibrary library = ConsumableLibrary.Build(data);
            Assert.AreEqual(5, library.All.Count);
            ConsumableSO fury = library.Get("fury_draught");
            Assert.AreEqual(ConsumableRecipients.Team, fury.Recipients);
            Assert.AreEqual(2, fury.Effects.Count);
            Assert.AreEqual(SkillEffectType.BuffStat, fury.Effects[0].EffectType);
            Assert.IsTrue(fury.Effects[0].IsPercent);
            Assert.AreEqual(ConsumableRecipients.Enemies, library.Get("venom_flask").Recipients);
            Assert.AreSame(fury, library.Get("fury_draught"));
            Assert.IsNull(library.Get("nope"));
        }

        [Test]
        public void Validator_AllowsOnlyTimedBuffsForTheTeamAndDebuffsForEnemies()
        {
            Assert.IsNotEmpty(ConsumableLibraryValidator.Validate(null));
            ConsumableLibraryData data = new ConsumableLibraryData
            {
                SchemaVersion = 1,
                Consumables = new[]
                {
                    Item("healer", "Team", new EffectData { EffectType = "Heal", Magnitude = 20, DurationTurns = 1 }),
                    Item("team_debuff", "Team", new EffectData { EffectType = "DebuffStat", AffectedStat = "Attack", Magnitude = 5, DurationTurns = 2, IsPercent = true }),
                    Item("enemy_shield", "Enemies", new EffectData { EffectType = "ApplyStatus", Status = "Shield", Magnitude = 30, DurationTurns = 2 }),
                    Item("forever", "Team", new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 5, DurationTurns = 0, IsPercent = true }),
                    Item("mover", "Team", new EffectData { EffectType = "BuffStat", AffectedStat = "MoveRange", Magnitude = 1, DurationTurns = 2, IsPercent = true }),
                    Item("flat_attack", "Team", new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 5, DurationTurns = 2 }),
                    Item("nobody", "Allies", new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 5, DurationTurns = 2, IsPercent = true }),
                    Item("ok", "Enemies", new EffectData { EffectType = "ApplyStatus", Status = "DamageOverTime", Magnitude = 8, DurationTurns = 3 }),
                    Item("ok", "Team", new EffectData { EffectType = "BuffStat", AffectedStat = "CritChance", Magnitude = 5, DurationTurns = 3 }),
                    Item("cleanser", "Team", new EffectData { EffectType = "Cleanse" }),
                    Item("enemy_cleanse", "Enemies", new EffectData { EffectType = "Cleanse" }),
                    Item("timed_cleanse", "Team", new EffectData { EffectType = "Cleanse", DurationTurns = 2 })
                }
            };

            List<string> errors = ConsumableLibraryValidator.Validate(data);
            string all = string.Join("\n", errors);

            foreach (string id in new[] { "healer", "team_debuff", "enemy_shield", "forever", "mover", "flat_attack", "nobody", "enemy_cleanse", "timed_cleanse" })
            {
                StringAssert.Contains("'" + id + "'", all);
            }

            StringAssert.DoesNotContain("'cleanser'", all, "an instant team Cleanse is allowed");
            StringAssert.Contains("used twice", all);
            Assert.AreEqual(10, errors.Count, all);
        }

        [Test]
        public void Inventory_StacksUpToTheMax_AndRemovesOnlyWhatIsHeld()
        {
            PlayerSave save = PlayerSave.CreateNew();

            Assert.IsTrue(ConsumableInventory.TryAdd(save, "fury_draught", 3, 5));
            Assert.IsFalse(ConsumableInventory.TryAdd(save, "fury_draught", 3, 5), "stack full");
            Assert.AreEqual(3, ConsumableInventory.Quantity(save, "fury_draught"));
            Assert.IsTrue(ConsumableInventory.TryAdd(save, "fury_draught", 2, 5));
            Assert.IsFalse(ConsumableInventory.TryRemove(save, "fury_draught", 6));
            Assert.IsTrue(ConsumableInventory.TryRemove(save, "fury_draught", 5));
            Assert.IsEmpty(save.Consumables, "an emptied stack is dropped");
            Assert.IsFalse(ConsumableInventory.TryRemove(save, "fury_draught", 1));
            Assert.IsFalse(ConsumableInventory.TryAdd(save, "fury_draught", 0, 5));
            Assert.IsFalse(ConsumableInventory.TryAdd(null, "fury_draught", 1, 5));
        }

        [Test]
        public void Check_RefusesMoreThanOne_UnknownOrUnheld()
        {
            ConsumableLibrary library = ConsumableLibrary.Build(LoadConsumables());
            PlayerSave save = PlayerSave.CreateNew();
            ConsumableInventory.TryAdd(save, "fury_draught", 1, 5);
            ConsumableInventory.TryAdd(save, "iron_tonic", 1, 5);
            List<string> errors = new List<string>();

            Assert.IsTrue(ConsumableLoadout.Check(save, new List<string>(), library.Get, errors));
            Assert.IsTrue(ConsumableLoadout.Check(save, new List<string> { "fury_draught" }, library.Get, errors));
            Assert.IsFalse(ConsumableLoadout.Check(save, new List<string> { "fury_draught", "iron_tonic" }, library.Get, errors));
            Assert.IsFalse(ConsumableLoadout.Check(save, new List<string> { "smoke_bomb" }, library.Get, errors), "not held");
            Assert.IsFalse(ConsumableLoadout.Check(save, new List<string> { "mystery" }, library.Get, errors));
            Assert.AreEqual(3, errors.Count, string.Join("\n", errors));
        }

        private static ConsumableData Item(string id, string recipients, EffectData effect)
        {
            return new ConsumableData { ConsumableId = id, DisplayName = id, Recipients = recipients, Effects = new[] { effect } };
        }
    }
}
