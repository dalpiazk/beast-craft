using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The element multiplier as applied by <see cref="SkillEffectApplier"/>, on top of
    /// <see cref="DamageFormula"/>. Every unit here has 20 in each of the four combat stats, so the
    /// mitigation <c>A / (A + D)</c> is exactly 1/2: a neutral hit of power <c>P</c> is
    /// <c>P / 100 * 20 * 1/2 = P / 10</c> — power 80 deals 8, power 50 deals 5 — and the element
    /// multiplier scales that. (The units are level 10; level plays no part in the formula.)
    /// </summary>
    public class SkillEffectApplierElementTests
    {
        private const int Level = 10;
        private const int CombatStat = 20;

        private readonly List<SkillSO> _created = new List<SkillSO>();

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
        public void Damage_StrongMatchup_IsDoubled()
        {
            BattleUnit target = Unit("target", 100, Element.Nature);

            Fire(Skill(Element.Fire, SkillEffectType.Damage, 80f), target);

            // Base 8, x2.
            Assert.AreEqual(84, target.CurrentHp);
        }

        [Test]
        public void Damage_WeakMatchup_IsHalvedAndTruncated()
        {
            BattleUnit target = Unit("target", 100, Element.Water);

            Fire(Skill(Element.Fire, SkillEffectType.Damage, 50f), target);

            // Base 5 * 0.5 = 2.5, truncated to 2.
            Assert.AreEqual(98, target.CurrentHp);
        }

        [Test]
        public void Damage_DualElementDefender_UsesProduct()
        {
            BattleUnit target = Unit("target", 100, Element.Nature, Element.Metal);

            Fire(Skill(Element.Fire, SkillEffectType.Damage, 80f), target);

            // Base 8, x2 x2.
            Assert.AreEqual(68, target.CurrentHp);
        }

        [Test]
        public void Damage_NeutralSkill_IsUnscaled()
        {
            BattleUnit target = Unit("target", 100, Element.Nature);

            Fire(Skill(Element.None, SkillEffectType.Damage, 80f), target);

            Assert.AreEqual(92, target.CurrentHp);
        }

        [Test]
        public void Damage_UnalignedTarget_IsUnscaled()
        {
            BattleUnit target = Unit("target", 100);

            Fire(Skill(Element.Fire, SkillEffectType.Damage, 80f), target);

            Assert.AreEqual(92, target.CurrentHp);
        }

        [Test]
        public void Damage_AmplifiedOverkill_ClampsAtZeroAndDefeats()
        {
            BattleUnit target = Unit("target", 15, Element.Nature);

            // 16 damage against 15 HP.
            Fire(Skill(Element.Fire, SkillEffectType.Damage, 80f), target);

            Assert.AreEqual(0, target.CurrentHp);
            Assert.IsTrue(target.IsDefeated);
        }

        [Test]
        public void Heal_IsNotScaledByElement()
        {
            BattleUnit target = Unit("target", 100, Element.Nature);
            target.CurrentHp = 50;

            Fire(Skill(Element.Fire, SkillEffectType.Heal, 10f), target);

            Assert.AreEqual(60, target.CurrentHp);
        }

        [Test]
        public void Buff_IsNotScaledByElement()
        {
            BattleUnit target = Unit("target", 100, Element.Nature);
            SkillSO skill = Skill(Element.Fire, SkillEffectType.BuffStat, 10f);
            skill.Effects[0].AffectedStat = StatType.Attack;

            Fire(skill, target);

            Assert.AreEqual(30, target.Stats.Attack);
        }

        [Test]
        public void CasterElements_DoNotAffectDamage()
        {
            BattleUnit target = Unit("target", 100, Element.Nature);
            BattleUnit fireCaster = Unit("caster", 100, Element.Fire);

            // The skill is neutral; the caster being Fire must not make it a Fire hit.
            SkillEffectApplier.Apply(
                new SkillActivation(Skill(Element.None, SkillEffectType.Damage, 80f), new[] { target }),
                fireCaster);

            Assert.AreEqual(92, target.CurrentHp);
        }

        [Test]
        public void BattleUnit_Elements_DefaultsToEmptyAndIsCopied()
        {
            Assert.AreEqual(0, Unit("plain", 10).Elements.Count);

            Element[] source = { Element.Fire };
            BattleUnit unit = new BattleUnit("u", BattleTeam.Enemy, new StatBlock(10, 0, 0, 0, 0, 0), HexCoordinate.Zero, null, source);
            source[0] = Element.Water;

            Assert.AreEqual(Element.Fire, unit.Elements[0]);
        }

        private SkillSO Skill(Element element, SkillEffectType type, float magnitude)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            skill.Element = element;
            skill.Effects.Add(new SkillEffect { EffectType = type, Magnitude = magnitude });
            _created.Add(skill);
            return skill;
        }

        private static BattleUnit Unit(string id, int hp, params Element[] elements)
        {
            StatBlock stats = new StatBlock(hp, CombatStat, CombatStat, CombatStat, CombatStat, 0);
            return new BattleUnit(id, BattleTeam.Enemy, stats, HexCoordinate.Zero, null, elements, Level);
        }

        private static void Fire(SkillSO skill, BattleUnit target)
        {
            BattleUnit caster = Unit("caster", 100);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster);
        }
    }
}
