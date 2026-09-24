using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Save;
using BeastCraft.Session;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Gear in the save: the inventory, <see cref="GearRules"/>' equip rules, the gear checks in
    /// <see cref="SaveValidator"/>, and the schema 1 to 2 migration that introduced it.
    /// </summary>
    public class PlayerGearTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();
        private BattleContent _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = new BattleContent(null, null, null, null,
                new[] { Gear("fang_blade", GearSlot.WeaponOrCore, 1), Gear("bark_shell", GearSlot.ArmorOrShell, 1), Gear("elder_charm", GearSlot.Accessory, 20) },
                new[] { AvatarGear("oak_staff", AvatarGearSlot.Weapon), AvatarGear("wool_cloak", AvatarGearSlot.Armor) });
        }

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        [Test]
        public void Inventory_GeneratesUniqueInstanceIds()
        {
            GearInventory inventory = new GearInventory();

            string a = inventory.AddBeastGear("fang_blade");
            string b = inventory.AddBeastGear("fang_blade");
            string c = inventory.AddAvatarGear("oak_staff");
            inventory.BeastGear.Add(new OwnedGear("gear4", "bark_shell"));
            string d = inventory.AddBeastGear("bark_shell");

            Assert.AreEqual("gear1", a);
            Assert.AreEqual("gear2", b);
            Assert.AreEqual("gear3", c);
            Assert.AreEqual("gear5", d, "skips an id already taken");
            Assert.IsNull(inventory.AddBeastGear(""));
            Assert.AreEqual("fang_blade", inventory.FindBeastGear(b).GearId);
            Assert.IsNull(inventory.FindBeastGear(c), "avatar gear is not beast gear");
            Assert.IsTrue(inventory.ContainsInstance(c));
        }

        [Test]
        public void EquipBeastGear_EnforcesSlotLevelAndSingleOwner()
        {
            PlayerSave save = SaveWithTwoBeasts();
            string blade = save.Gear.AddBeastGear("fang_blade");
            string blade2 = save.Gear.AddBeastGear("fang_blade");
            string charm = save.Gear.AddBeastGear("elder_charm");
            string ghost = save.Gear.AddBeastGear("ghost_gear");
            string staff = save.Gear.AddAvatarGear("oak_staff");

            Assert.AreEqual(GearEquipResult.SlotMismatch, GearRules.EquipBeastGear(save, "b1", GearSlot.Accessory, blade, _catalog));
            Assert.AreEqual(GearEquipResult.LevelTooLow, GearRules.EquipBeastGear(save, "b1", GearSlot.Accessory, charm, _catalog), "b1 is level 10, the charm needs 20");
            Assert.AreEqual(GearEquipResult.UnknownGear, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, ghost, _catalog));
            Assert.AreEqual(GearEquipResult.UnknownInstance, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, staff, _catalog), "avatar gear");
            Assert.AreEqual(GearEquipResult.UnknownInstance, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, "nope", _catalog));
            Assert.AreEqual(GearEquipResult.UnknownOwner, GearRules.EquipBeastGear(save, "nobody", GearSlot.WeaponOrCore, blade, _catalog));

            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, blade, _catalog));
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, blade, _catalog), "re-equipping is a no-op");
            Assert.AreEqual(GearEquipResult.EquippedElsewhere, GearRules.EquipBeastGear(save, "b2", GearSlot.WeaponOrCore, blade, _catalog));
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "b2", GearSlot.Accessory, charm, _catalog), "b2 is level 25");

            Assert.AreEqual("b1", GearRules.FindBeastGearHolder(save, blade));
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "b1", GearSlot.WeaponOrCore, blade2, _catalog), "replaces what the slot held");
            Assert.AreEqual(blade2, GearRules.GetSlot(save.FindBeast("b1").EquippedGear, (int)GearSlot.WeaponOrCore));
            Assert.IsNull(GearRules.FindBeastGearHolder(save, blade), "the replaced instance is back in the inventory");
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipBeastGear(save, "b2", GearSlot.WeaponOrCore, blade, _catalog));

            Assert.IsTrue(GearRules.UnequipBeastGear(save, "b2", GearSlot.WeaponOrCore));
            Assert.IsFalse(GearRules.UnequipBeastGear(save, "b2", GearSlot.WeaponOrCore));
            Assert.AreEqual(0, GearRules.CountBeastGearWorn(save, blade));
            List<SaveIssue> issues = SaveValidator.Validate(save, null, _catalog);
            Assert.AreEqual(1, issues.Count, "only the unknown ghost gear in the inventory:\n" + string.Join("\n", issues));
            Assert.AreEqual(SaveIssueKind.UnknownGear, issues[0].Kind);
        }

        [Test]
        public void EquipAvatarGear_EnforcesSlotAndInventory()
        {
            PlayerSave save = SaveWithTwoBeasts();
            string staff = save.Gear.AddAvatarGear("oak_staff");
            string cloak = save.Gear.AddAvatarGear("wool_cloak");
            string blade = save.Gear.AddBeastGear("fang_blade");

            Assert.AreEqual(GearEquipResult.SlotMismatch, GearRules.EquipAvatarGear(save, AvatarGearSlot.Armor, staff, _catalog));
            Assert.AreEqual(GearEquipResult.UnknownInstance, GearRules.EquipAvatarGear(save, AvatarGearSlot.Weapon, blade, _catalog), "beast gear");
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipAvatarGear(save, AvatarGearSlot.Weapon, staff, _catalog));
            Assert.AreEqual(GearEquipResult.Equipped, GearRules.EquipAvatarGear(save, AvatarGearSlot.Armor, cloak, _catalog));
            Assert.AreEqual(cloak, GearRules.GetSlot(save.AvatarEquippedGear, (int)AvatarGearSlot.Armor));
            Assert.IsTrue(GearRules.UnequipAvatarGear(save, AvatarGearSlot.Armor));
            Assert.IsFalse(GearRules.UnequipAvatarGear(save, AvatarGearSlot.Trinket));
            Assert.AreEqual(GearEquipResult.UnknownOwner, GearRules.EquipAvatarGear(null, AvatarGearSlot.Weapon, staff, _catalog));
        }

        [Test]
        public void Validate_ReportsGearProblems()
        {
            PlayerSave save = SaveWithTwoBeasts();
            string blade = save.Gear.AddBeastGear("fang_blade");
            string charm = save.Gear.AddBeastGear("elder_charm");
            save.Gear.AddBeastGear("ghost_gear");
            save.Gear.BeastGear.Add(new OwnedGear(blade, "bark_shell"));
            string staff = save.Gear.AddAvatarGear("oak_staff");

            save.FindBeast("b1").EquippedGear[(int)GearSlot.WeaponOrCore] = blade;
            save.FindBeast("b2").EquippedGear[(int)GearSlot.WeaponOrCore] = blade;
            save.FindBeast("b1").EquippedGear[(int)GearSlot.Accessory] = charm;
            save.FindBeast("b2").EquippedGear[(int)GearSlot.ArmorOrShell] = "missing";
            save.AvatarEquippedGear[(int)AvatarGearSlot.Armor] = staff;

            List<SaveIssue> issues = SaveValidator.Validate(save, null, _catalog);

            AssertIssue(issues, SaveIssueKind.UnknownGear, "ghost_gear", "Gear.BeastGear[2].GearId");
            AssertIssue(issues, SaveIssueKind.DuplicateGearInstance, blade, "Gear.BeastGear[3].InstanceId");
            AssertIssue(issues, SaveIssueKind.GearLevelTooLow, charm, "Beasts[0].EquippedGear[2]");
            AssertIssue(issues, SaveIssueKind.DoubleEquippedGear, blade, "Beasts[1].EquippedGear[0]");
            AssertIssue(issues, SaveIssueKind.UnknownGearInstance, "missing", "Beasts[1].EquippedGear[1]");
            AssertIssue(issues, SaveIssueKind.GearSlotMismatch, staff, "AvatarEquippedGear[1]");
            Assert.AreEqual(6, issues.Count, string.Join("\n", issues));

            List<SaveIssue> structural = SaveValidator.Validate(save, null, null);
            Assert.AreEqual(3, structural.Count, "without a gear catalog: duplicate instance, double-equipped, unknown instance\n" + string.Join("\n", structural));
        }

        [Test]
        public void Migration_V1ToV2_AddsEmptyGear_AndRoundTrips()
        {
            const string v1 = "{\"SchemaVersion\":1," +
                              "\"Beasts\":[{\"BeastId\":\"b1\",\"Progress\":{\"SpeciesId\":\"emberfox\",\"Level\":7,\"Xp\":12}," +
                              "\"Skills\":{\"Known\":[{\"SkillId\":\"ember_bite\",\"Level\":2,\"Xp\":5,\"Tier\":0}],\"Equipped\":[\"ember_bite\",\"\",\"\"]}}]," +
                              "\"Avatar\":{\"Level\":3,\"Xp\":40}," +
                              "\"Materials\":{\"Materials\":[{\"MaterialId\":\"essence_shard\",\"Quantity\":2}],\"ClearedCells\":[],\"Pity\":[]}}";
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), null, null, PlayerSave.CurrentSchemaVersion, _catalog);

            SaveLoadResult migrated = serializer.Deserialize(v1);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(migrated.Migrated);
            Assert.AreEqual(1, migrated.SourceVersion);
            Assert.AreEqual(PlayerSave.CurrentSchemaVersion, migrated.Save.SchemaVersion);
            Assert.IsEmpty(migrated.Issues, string.Join("\n", migrated.Issues));
            Assert.IsEmpty(migrated.Save.Gear.BeastGear);
            Assert.IsEmpty(migrated.Save.Gear.AvatarGear);
            Assert.AreEqual(GearRules.AvatarSlotCount, migrated.Save.AvatarEquippedGear.Length);
            OwnedBeast beast = migrated.Save.FindBeast("b1");
            Assert.AreEqual(GearRules.BeastSlotCount, beast.EquippedGear.Length);
            Assert.AreEqual(7, beast.Progress.Level);
            Assert.AreEqual(12, beast.Progress.Xp);
            Assert.AreEqual("ember_bite", beast.Skills.GetEquipped(0));
            Assert.AreEqual(3, migrated.Save.Avatar.Level);
            Assert.AreEqual(2, migrated.Save.Materials.GetCount("essence_shard"));

            string v2 = serializer.Serialize(migrated.Save);
            StringAssert.Contains("\"SchemaVersion\":" + PlayerSave.CurrentSchemaVersion, v2);
            SaveLoadResult reloaded = serializer.Deserialize(v2);
            Assert.IsTrue(reloaded.Success, reloaded.Error);
            Assert.IsFalse(reloaded.Migrated);
            Assert.AreEqual(v2, serializer.Serialize(reloaded.Save));
        }

        [Test]
        public void EquippedGear_RoundTrips()
        {
            PlayerSave save = SaveWithTwoBeasts();
            string blade = save.Gear.AddBeastGear("fang_blade");
            string staff = save.Gear.AddAvatarGear("oak_staff");
            GearRules.EquipBeastGear(save, "b2", GearSlot.WeaponOrCore, blade, _catalog);
            GearRules.EquipAvatarGear(save, AvatarGearSlot.Weapon, staff, _catalog);
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer(), null, null, PlayerSave.CurrentSchemaVersion, _catalog);

            SaveLoadResult loaded = serializer.Deserialize(serializer.Serialize(save));

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsEmpty(loaded.Issues, string.Join("\n", loaded.Issues));
            Assert.AreEqual(blade, GearRules.GetSlot(loaded.Save.FindBeast("b2").EquippedGear, (int)GearSlot.WeaponOrCore));
            Assert.IsNull(GearRules.GetSlot(loaded.Save.FindBeast("b1").EquippedGear, (int)GearSlot.WeaponOrCore));
            Assert.AreEqual(staff, GearRules.GetSlot(loaded.Save.AvatarEquippedGear, (int)AvatarGearSlot.Weapon));
            Assert.AreEqual("fang_blade", loaded.Save.Gear.FindBeastGear(blade).GearId);
            Assert.AreEqual(save.Gear.NextInstanceNumber, loaded.Save.Gear.NextInstanceNumber);
        }

        private static PlayerSave SaveWithTwoBeasts()
        {
            PlayerSave save = PlayerSave.CreateNew();
            save.Beasts.Add(OwnedBeast.Create("b1", "emberfox", 10));
            save.Beasts.Add(OwnedBeast.Create("b2", "tidepup", 25));
            return save;
        }

        private GearSO Gear(string id, GearSlot slot, int minimumLevel)
        {
            GearSO gear = new GearSO();
            gear.GearId = id;
            gear.Slot = slot;
            gear.MinimumLevel = minimumLevel;
            gear.Modifiers.Add(new StatModifier { Stat = StatType.Attack, FlatBonus = 10 });
            _created.Add(gear);
            return gear;
        }

        private AvatarGearSO AvatarGear(string id, AvatarGearSlot slot)
        {
            AvatarGearSO gear = new AvatarGearSO();
            gear.AvatarGearId = id;
            gear.Slot = slot;
            _created.Add(gear);
            return gear;
        }

        private static void AssertIssue(List<SaveIssue> issues, SaveIssueKind kind, string id, string path)
        {
            bool found = issues.Exists(i => i.Kind == kind && i.Id == id && i.Path == path);
            Assert.IsTrue(found, "Expected " + kind + " for '" + id + "' at " + path + ", got:\n" + string.Join("\n", issues));
        }
    }
}
