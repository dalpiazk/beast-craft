using System;
using System.Collections;
using System.Reflection;
using BeastCraft.Progression;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="MaterialInventory"/>: stacks, consuming, cleared cells and pity counters, and that
    /// it stays save data <c>JsonUtility</c> can write (serializable, no dictionaries).
    /// </summary>
    public class MaterialInventoryTests
    {
        [Test]
        public void Add_Stacks_AndTryConsume_RemovesOnlyWhatIsHeld()
        {
            MaterialInventory inventory = new MaterialInventory();

            Assert.IsTrue(inventory.Add("shard", 2));
            Assert.IsTrue(inventory.Add("shard", 3));
            Assert.IsTrue(inventory.Add("crystal", 1));
            Assert.AreEqual(5, inventory.GetCount("shard"));
            Assert.AreEqual(2, inventory.Materials.Count);

            Assert.IsFalse(inventory.TryConsume("shard", 6), "not enough");
            Assert.AreEqual(5, inventory.GetCount("shard"));
            Assert.IsTrue(inventory.TryConsume("shard", 5));
            Assert.AreEqual(0, inventory.GetCount("shard"));
            Assert.AreEqual(1, inventory.Materials.Count, "a spent stack is removed");
            Assert.IsFalse(inventory.TryConsume("shard", 1));
        }

        [Test]
        public void BadInput_ChangesNothing()
        {
            MaterialInventory inventory = new MaterialInventory();

            Assert.IsFalse(inventory.Add(null, 1));
            Assert.IsFalse(inventory.Add("", 1));
            Assert.IsFalse(inventory.Add("shard", 0));
            Assert.IsFalse(inventory.Add("shard", -3));
            Assert.IsFalse(inventory.TryConsume("shard", 0));
            Assert.AreEqual(0, inventory.GetCount(null));
            Assert.AreEqual(0, inventory.Materials.Count);
            Assert.IsFalse(inventory.MarkCleared(null, 1));

            inventory.SetPity("", 1, 4);
            Assert.AreEqual(0, inventory.Pity.Count);
        }

        [Test]
        public void MarkCleared_IsTrueOnlyTheFirstTimePerShapeAndBand()
        {
            MaterialInventory inventory = new MaterialInventory();

            Assert.IsTrue(inventory.MarkCleared("solo", 1));
            Assert.IsFalse(inventory.MarkCleared("solo", 1));
            Assert.IsTrue(inventory.HasCleared("solo", 1));
            Assert.IsFalse(inventory.HasCleared("solo", 21));
            Assert.IsTrue(inventory.MarkCleared("solo", 21));
            Assert.IsTrue(inventory.MarkCleared("horde", 1));
        }

        [Test]
        public void Pity_DefaultsToZero_AndIsPerShapeAndTier()
        {
            MaterialInventory inventory = new MaterialInventory();

            Assert.AreEqual(0, inventory.GetPity("solo", 1));
            inventory.SetPity("solo", 1, 4);
            inventory.SetPity("solo", 2, 9);
            inventory.SetPity("solo", 1, 5);
            inventory.SetPity("horde", 1, -2);

            Assert.AreEqual(5, inventory.GetPity("solo", 1));
            Assert.AreEqual(9, inventory.GetPity("solo", 2));
            Assert.AreEqual(0, inventory.GetPity("horde", 1), "negative reads as 0");
            Assert.AreEqual(3, inventory.Pity.Count);
        }

        [Test]
        public void SaveData_IsSerializableWithNoDictionaries()
        {
            foreach (Type type in new[] { typeof(MaterialInventory), typeof(MaterialStack), typeof(ClearedCell), typeof(PityCounter) })
            {
                Assert.IsTrue(type.IsDefined(typeof(SerializableAttribute), false), type.Name + " is [Serializable]");
                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    Assert.IsFalse(typeof(IDictionary).IsAssignableFrom(field.FieldType), type.Name + "." + field.Name + " must not be a dictionary (JsonUtility drops it)");
                }
            }
        }

        [Test]
        public void SaveData_ReadsBackFromJson()
        {
            const string json = "{\"Materials\":[{\"MaterialId\":\"shard\",\"Quantity\":3}]," +
                                "\"ClearedCells\":[{\"Shape\":\"solo\",\"BandMinLevel\":21}]," +
                                "\"Pity\":[{\"Shape\":\"horde\",\"Tier\":2,\"Misses\":6}]}";

            MaterialInventory inventory = FieldJson.FromJson<MaterialInventory>(json);

            Assert.AreEqual(3, inventory.GetCount("shard"));
            Assert.IsTrue(inventory.HasCleared("solo", 21));
            Assert.AreEqual(6, inventory.GetPity("horde", 2));
        }
    }
}
