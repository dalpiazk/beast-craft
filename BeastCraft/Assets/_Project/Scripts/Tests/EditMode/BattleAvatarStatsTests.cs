using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    public class BattleAvatarStatsTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        [Test]
        public void Create_WithGear_AppliesFlatThenSummedPercent()
        {
            StatBlock baseStats = new StatBlock(100, 10, 5, 0, 0, 0);
            AvatarGearSO blade = Gear(AvatarGearSlot.Weapon,
                new StatModifier { Stat = StatType.Attack, FlatBonus = 5 },
                new StatModifier { Stat = StatType.Attack, PercentBonus = 0.1f });
            AvatarGearSO ring = Gear(AvatarGearSlot.Trinket,
                new StatModifier { Stat = StatType.Attack, PercentBonus = 0.1f },
                new StatModifier { Stat = StatType.HP, FlatBonus = 20 });

            BattleUnit avatar = BattleAvatar.Create(null, baseStats, new[] { blade, ring });

            // (10 + 5) * (1 + 0.1 + 0.1) = 18.
            Assert.AreEqual(18, avatar.Stats.Attack);
            Assert.AreEqual(120, avatar.Stats.Hp);
            Assert.AreEqual(120, avatar.CurrentHp);
            Assert.AreEqual(5, avatar.Stats.Defense);
            Assert.AreEqual(BattleAvatar.DefaultId, avatar.Id);
            Assert.AreEqual(BattleTeam.Player, avatar.Team);
            Assert.AreEqual(HexCoordinate.Zero, avatar.Position);
            Assert.IsFalse(avatar.IsDefeated);
        }

        [Test]
        public void Create_SameSlotTwice_BothCount()
        {
            AvatarGearSO first = Gear(AvatarGearSlot.Armor, new StatModifier { Stat = StatType.Defense, FlatBonus = 3 });
            AvatarGearSO second = Gear(AvatarGearSlot.Armor, new StatModifier { Stat = StatType.Defense, FlatBonus = 4 });

            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(10, 0, 0, 0, 0, 0), new[] { first, second });

            Assert.AreEqual(7, avatar.Stats.Defense);
        }

        [Test]
        public void Create_NullGearListEntriesAndModifiers_AreSkipped()
        {
            StatBlock baseStats = new StatBlock(50, 8, 0, 0, 0, 0);
            AvatarGearSO nullList = Gear(AvatarGearSlot.Armor);
            nullList.Modifiers = null;
            AvatarGearSO nullModifier = Gear(AvatarGearSlot.Weapon, null, new StatModifier { Stat = StatType.Attack, FlatBonus = 2 });

            BattleUnit fromNullList = BattleAvatar.Create(null, baseStats, null);
            BattleUnit fromNullEntries = BattleAvatar.Create(null, baseStats, new[] { null, nullList, nullModifier });

            Assert.AreEqual(baseStats, fromNullList.Stats);
            Assert.AreEqual(50, fromNullEntries.Stats.Hp);
            Assert.AreEqual(10, fromNullEntries.Stats.Attack);
        }

        [Test]
        public void Create_NoGear_KeepsBaseStats()
        {
            StatBlock baseStats = new StatBlock(40, 6, 7, 8, 9, 10, 2);

            BattleUnit avatar = BattleAvatar.Create(null, baseStats, new AvatarGearSO[0]);

            Assert.AreEqual(baseStats, avatar.Stats);
        }

        [Test]
        public void Create_ZeroBaseNoGear_ClampsHpToOne()
        {
            BattleUnit avatar = BattleAvatar.Create(null, default(StatBlock), null);

            Assert.AreEqual(1, avatar.Stats.Hp);
            Assert.AreEqual(1, avatar.CurrentHp);
            Assert.AreEqual(0, avatar.Stats.Attack);
        }

        [Test]
        public void Create_FromAvatarStatsAsset_UsesItsBase()
        {
            AvatarStatsSO profile = ScriptableObject.CreateInstance<AvatarStatsSO>();
            profile.BaseStats = new StatBlock(30, 4, 0, 0, 0, 0);
            _created.Add(profile);
            AvatarGearSO trinket = Gear(AvatarGearSlot.Trinket, new StatModifier { Stat = StatType.HP, PercentBonus = 0.5f });

            BattleUnit avatar = BattleAvatar.Create(null, profile.BaseStats, new[] { trinket });

            Assert.AreEqual(45, avatar.Stats.Hp);
            Assert.AreEqual(4, avatar.Stats.Attack);
        }

        [Test]
        public void Create_DoesNotMutateGearModifiers()
        {
            StatModifier modifier = new StatModifier { Stat = StatType.Attack, FlatBonus = 5, PercentBonus = 0.5f };
            AvatarGearSO gear = Gear(AvatarGearSlot.Weapon, modifier);

            BattleAvatar.Create(null, new StatBlock(10, 10, 0, 0, 0, 0), new[] { gear });

            Assert.AreEqual(1, gear.Modifiers.Count);
            Assert.AreEqual(5, modifier.FlatBonus);
            Assert.AreEqual(0.5f, modifier.PercentBonus);
        }

        [Test]
        public void LegacyCreate_IsStillAllZeroAndUnclamped()
        {
            SkillLoadout loadout = new SkillLoadout(null);

            BattleUnit avatar = BattleAvatar.Create(loadout, "commander");

            Assert.AreEqual("commander", avatar.Id);
            Assert.AreEqual(BattleTeam.Player, avatar.Team);
            Assert.AreEqual(default(StatBlock), avatar.Stats);
            Assert.AreEqual(0, avatar.CurrentHp);
            Assert.AreEqual(HexCoordinate.Zero, avatar.Position);
            Assert.AreSame(loadout, avatar.Skills);
            Assert.AreEqual(1, avatar.Level);
            Assert.IsFalse(avatar.IsDefeated);
        }

        [Test]
        public void Create_Level_DefaultsToOneAndIsRecorded()
        {
            StatBlock baseStats = new StatBlock(40, 6, 0, 0, 0, 0);

            Assert.AreEqual(1, BattleAvatar.Create(null, baseStats, null).Level);
            Assert.AreEqual(30, BattleAvatar.Create(null, baseStats, null, BattleAvatar.DefaultId, 30).Level);
            Assert.AreEqual(1, BattleAvatar.Create(null, baseStats, null, BattleAvatar.DefaultId, 0).Level);
        }

        [Test]
        public void StatfulAvatar_IsNeverHitOrDefeated_InABattleItsSideLoses()
        {
            SkillSO sweep = ScriptableObject.CreateInstance<SkillSO>();
            sweep.TargetShape = SkillTargetShape.AllEnemies;
            sweep.TargetSide = SkillTargetSide.Enemy;
            sweep.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 1000f });
            _created.Add(sweep);

            BattleUnit player = new BattleUnit("p1", BattleTeam.Player, new StatBlock(10, 0, 0, 0, 0, 1), new HexCoordinate(0, 1));
            // Attack 10 against Defense 0 (no mitigation) at power 1000 is 100 damage: a one-shot.
            BattleUnit enemy = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(10, 10, 0, 0, 0, 5), new HexCoordinate(0, -1),
                                              new SkillLoadout(new[] { sweep }));
            AvatarGearSO armor = Gear(AvatarGearSlot.Armor, new StatModifier { Stat = StatType.HP, FlatBonus = 10 });
            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(40, 0, 0, 0, 0, 0), new[] { armor });
            BattleUnit[] roster = { player, enemy };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(1), avatar);

            Assert.IsTrue(player.IsDefeated);
            Assert.AreEqual(BattleOutcome.EnemyVictory, result.Outcome);
            Assert.AreEqual(50, avatar.CurrentHp);
            Assert.IsFalse(avatar.IsDefeated);
        }

        private AvatarGearSO Gear(AvatarGearSlot slot, params StatModifier[] modifiers)
        {
            AvatarGearSO gear = ScriptableObject.CreateInstance<AvatarGearSO>();
            gear.Slot = slot;
            gear.Modifiers.AddRange(modifiers);
            _created.Add(gear);
            return gear;
        }
    }
}
