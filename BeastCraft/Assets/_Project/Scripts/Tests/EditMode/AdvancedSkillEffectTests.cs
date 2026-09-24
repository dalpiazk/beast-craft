using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The non-status advanced effects: stacking and percent stat changes, multi-hit, execute,
    /// <see cref="SkillTargetingCriterion.HpFraction"/>, and the skill-level limits
    /// <see cref="SkillSO.MaxUsesPerBattle"/> and <see cref="SkillSO.InitialCooldown"/>.
    /// </summary>
    public class AdvancedSkillEffectTests
    {
        private readonly List<SkillSO> _created = new List<SkillSO>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ------------------------------------------------------------------ stacking stat changes

        [Test]
        public void Debuff_DefaultCap_ReapplicationRefreshes()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 50);
            SkillSO skill = Skill(Debuff(StatType.Attack, 10f, 3, 1));

            Fire(skill, target);
            SkillEffectApplier.TickModifiers(target);
            Fire(skill, target);

            Assert.AreEqual(40, target.Stats.Attack, "one copy, not two");
            Assert.AreEqual(1, target.ActiveStatModifiers.Count);
            Assert.AreEqual(3, target.ActiveStatModifiers[0].RemainingTurns, "a full, fresh duration");
        }

        [Test]
        public void Debuff_StacksUpToTheCap_EachOnItsOwnClock()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 100);
            SkillSO skill = Skill(Debuff(StatType.Attack, 10f, 3, 3));

            Fire(skill, target);
            SkillEffectApplier.TickModifiers(target);
            Fire(skill, target);
            Fire(skill, target);

            Assert.AreEqual(70, target.Stats.Attack);

            Fire(skill, target);

            Assert.AreEqual(70, target.Stats.Attack, "the cap replaced the stack with the fewest turns left");
            Assert.AreEqual(3, target.ActiveStatModifiers.Count);
            foreach (ActiveStatModifier modifier in target.ActiveStatModifiers)
            {
                Assert.AreEqual(3, modifier.RemainingTurns, "the older, shorter stack was the one replaced");
            }

            SkillEffectApplier.TickModifiers(target);
            SkillEffectApplier.TickModifiers(target);
            SkillEffectApplier.TickModifiers(target);

            Assert.AreEqual(100, target.Stats.Attack, "every stack reverted exactly");
        }

        [Test]
        public void Debuff_StacksExpireIndependently()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 100);
            SkillSO skill = Skill(Debuff(StatType.Attack, 10f, 2, 2));

            Fire(skill, target);
            SkillEffectApplier.TickModifiers(target);
            Fire(skill, target);
            Assert.AreEqual(80, target.Stats.Attack);

            SkillEffectApplier.TickModifiers(target);
            Assert.AreEqual(90, target.Stats.Attack, "the first stack ran out on its own clock");

            SkillEffectApplier.TickModifiers(target);
            Assert.AreEqual(100, target.Stats.Attack);
        }

        [Test]
        public void DifferentEffects_OnTheSameStat_NeverShareACap()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 100);

            Fire(Skill(Debuff(StatType.Attack, 10f, 3, 1)), target);
            Fire(Skill(Debuff(StatType.Attack, 5f, 3, 1)), target);

            Assert.AreEqual(85, target.Stats.Attack);
            Assert.AreEqual(2, target.ActiveStatModifiers.Count);
        }

        [Test]
        public void InstantStatChange_IsUnaffectedByTheCap()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 100);
            SkillSO skill = Skill(Debuff(StatType.Attack, 10f, 0, 1));

            Fire(skill, target);
            Fire(skill, target);

            Assert.AreEqual(80, target.Stats.Attack, "a permanent change has nothing to cap or refresh");
        }

        [Test]
        public void PercentDebuff_IsAPercentOfTheCurrentStat_AndRevertsExactly()
        {
            BattleUnit target = Unit("e1", BattleTeam.Enemy, attack: 55);
            SkillEffect effect = Debuff(StatType.Attack, 20f, 1, 1);
            effect.IsPercent = true;

            Fire(Skill(effect), target);
            Assert.AreEqual(44, target.Stats.Attack, "20% of 55 = 11");

            SkillEffectApplier.TickModifiers(target);
            Assert.AreEqual(55, target.Stats.Attack);
        }

        [Test]
        public void PercentBuff_StacksCompoundOnTheCurrentValue()
        {
            BattleUnit target = Unit("p1", BattleTeam.Player, attack: 100);
            SkillEffect effect = new SkillEffect
            {
                EffectType = SkillEffectType.BuffStat,
                AffectedStat = StatType.Attack,
                Magnitude = 10f,
                DurationTurns = 2,
                MaxStacks = 2,
                IsPercent = true
            };

            Fire(Skill(effect), target);
            Fire(Skill(effect), target);

            Assert.AreEqual(121, target.Stats.Attack, "100 + 10, then 110 + 11");
        }

        // ------------------------------------------------------------------ multi-hit

        [Test]
        public void MultiHit_EachHitRollsItsOwnCritAndVariance()
        {
            BattleUnit caster = new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 100, 10, 10, 10, 10, 0, 50), HexCoordinate.Zero);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, hp: 100000, defense: 100);
            SkillEffect effect = new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 60f, HitCount = 6 };
            SkillSO skill = Skill(effect);
            SkillActivation activation = new SkillActivation(skill, new[] { target });

            SkillEffectApplier.Apply(activation, caster, new System.Random(21));

            Assert.AreEqual(6, activation.Hits.Count);
            System.Random reference = new System.Random(21);
            BattleUnit twin = Unit("e1", BattleTeam.Enemy, hp: 100000, defense: 100);
            int total = 0;
            bool sawCrit = false;
            bool sawNormal = false;

            for (int i = 0; i < 6; i++)
            {
                DamageRoll expected = DamageFormula.Roll(caster, twin, skill, 60f, reference);
                Assert.AreEqual(expected.IsCrit, activation.Hits[i].Roll.IsCrit, "hit " + i);
                Assert.AreEqual(expected.VariancePercent, activation.Hits[i].Roll.VariancePercent, "hit " + i);
                Assert.AreEqual(expected.Amount, activation.Hits[i].Roll.Amount, "hit " + i);
                total += expected.Amount;
                sawCrit |= expected.IsCrit;
                sawNormal |= !expected.IsCrit;
            }

            Assert.AreEqual(100000 - total, target.CurrentHp);
            Assert.IsTrue(sawCrit && sawNormal, "test setup: seed 21 mixes crits and normal hits");
        }

        [Test]
        public void MultiHit_StopsOnDefeat_AndDrawsNothingForTheHitsNotTaken()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, hp: 5);
            SkillActivation activation = new SkillActivation(Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 100f, HitCount = 4 }),
                                                             new[] { target });
            System.Random rng = new System.Random(8);

            SkillEffectApplier.Apply(activation, caster, rng);

            Assert.IsTrue(target.IsDefeated);
            Assert.AreEqual(1, activation.Hits.Count);
            System.Random reference = new System.Random(8);
            reference.Next(100);
            reference.Next(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent + 1);
            Assert.AreEqual(reference.Next(), rng.Next(), "two draws, for the one hit");
        }

        [Test]
        public void MultiHit_ZeroOrNegativeReadsAsOne()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, attack: 100);
            BattleUnit target = Unit("e1", BattleTeam.Enemy, hp: 1000);
            SkillActivation activation = new SkillActivation(Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 100f, HitCount = 0 }),
                                                             new[] { target });

            SkillEffectApplier.Apply(activation, caster);

            Assert.AreEqual(1, activation.Hits.Count);
        }

        // ------------------------------------------------------------------ execute

        [Test]
        public void ExecuteMultiplier_IsLinearInMissingHp()
        {
            Assert.AreEqual(1.0, DamageFormula.GetExecuteMultiplier(100, 100, 100));
            Assert.AreEqual(1.5, DamageFormula.GetExecuteMultiplier(100, 50, 100));
            Assert.AreEqual(2.0, DamageFormula.GetExecuteMultiplier(100, 0, 100));
            Assert.AreEqual(1.375, DamageFormula.GetExecuteMultiplier(50, 25, 100));
            Assert.AreEqual(1.0, DamageFormula.GetExecuteMultiplier(0, 10, 100), "no bonus");
            Assert.AreEqual(1.0, DamageFormula.GetExecuteMultiplier(100, 0, 0), "no maximum");
        }

        [Test]
        public void Execute_ScalesTheHitBeforeTruncation()
        {
            BattleUnit caster = Unit("p1", BattleTeam.Player, attack: 100);
            BattleUnit full = Unit("e1", BattleTeam.Enemy, hp: 200, defense: 100);
            BattleUnit half = Unit("e2", BattleTeam.Enemy, hp: 200, defense: 100);
            half.CurrentHp = 100;
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 100f, ExecuteBonusPercent = 100 });

            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { full, half }), caster);

            Assert.AreEqual(150, full.CurrentHp, "base 50 at full health");
            Assert.AreEqual(25, half.CurrentHp, "50 x 1.5 at half health");
            Assert.AreEqual(41, DamageFormula.Compute(55f, 100, 100, 1f, 100, false, 1.5), "27.5 x 1.5 = 41.25, truncated once");
        }

        [Test]
        public void BonusMultiplierOfOne_IsBitExactWithThePlainFormula()
        {
            for (int power = 1; power < 300; power += 7)
            {
                Assert.AreEqual(DamageFormula.Compute(power, 123, 77, 1.5f, 93, true), DamageFormula.Compute(power, 123, 77, 1.5f, 93, true, 1.0));
            }
        }

        // ------------------------------------------------------------------ HpFraction targeting

        [Test]
        public void HpFraction_PicksTheWorstOffAlly_NotTheLowestHp()
        {
            BattleUnit healer = Unit("p0", BattleTeam.Player, hp: 100);
            BattleUnit a = Hurt(Unit("p1", BattleTeam.Player, hp: 100), 50);
            BattleUnit b = Hurt(Unit("p2", BattleTeam.Player, hp: 40), 30);
            BattleUnit c = Hurt(Unit("p3", BattleTeam.Player, hp: 1000), 400);
            List<BattleUnit> all = new List<BattleUnit> { healer, a, b, c };
            SkillSO heal = HealSkill(SkillTargetingCriterion.HpFraction, SkillTargetingOrder.Lowest);

            Assert.AreSame(c, SkillTargetResolver.ResolveTargets(heal, healer, all, null, null)[0], "0.4 is the lowest fraction");

            heal.TargetingCriterion = SkillTargetingCriterion.CurrentHp;
            Assert.AreSame(b, SkillTargetResolver.ResolveTargets(heal, healer, all, null, null)[0], "CurrentHp would pick 30 HP");

            heal.TargetingCriterion = SkillTargetingCriterion.HpFraction;
            heal.TargetingOrder = SkillTargetingOrder.Highest;
            Assert.AreSame(healer, SkillTargetResolver.ResolveTargets(heal, healer, all, null, null)[0], "the healer is at full health");
        }

        [Test]
        public void HpFraction_TiesBreakOnId_AndHealLandsThere()
        {
            BattleUnit healer = Unit("p0", BattleTeam.Player, hp: 100);
            BattleUnit later = Hurt(Unit("p9", BattleTeam.Player, hp: 100), 50);
            BattleUnit earlier = Hurt(Unit("p5", BattleTeam.Player, hp: 40), 20);
            List<BattleUnit> all = new List<BattleUnit> { later, healer, earlier };
            SkillSO heal = HealSkill(SkillTargetingCriterion.HpFraction, SkillTargetingOrder.Lowest);

            IReadOnlyList<BattleUnit> targets = SkillTargetResolver.ResolveTargets(heal, healer, all, null, null);
            SkillEffectApplier.Apply(new SkillActivation(heal, targets), healer);

            Assert.AreSame(earlier, targets[0], "1/2 and 20/40 tie exactly; p5 sorts first");
            Assert.AreEqual(30, earlier.CurrentHp);
        }

        [Test]
        public void HpFraction_IsExactWhereFloatsWouldBlur()
        {
            BattleUnit healer = Unit("p0", BattleTeam.Player, hp: 100);
            BattleUnit a = Hurt(Unit("p1", BattleTeam.Player, hp: 999999), 333333);
            BattleUnit b = Hurt(Unit("p2", BattleTeam.Player, hp: 1000000), 333334);
            List<BattleUnit> all = new List<BattleUnit> { healer, b, a };

            Assert.AreSame(a, SkillTargetResolver.ResolveTargets(HealSkill(SkillTargetingCriterion.HpFraction, SkillTargetingOrder.Lowest), healer, all,
                                                                 null, null)[0]);
        }

        // ------------------------------------------------------------------ skill-level limits

        [Test]
        public void MaxUsesPerBattle_SpendsTheSlot()
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            skill.Cooldown = 0;
            skill.MaxUsesPerBattle = 2;
            SkillLoadout loadout = new SkillLoadout(new[] { skill });

            for (int i = 0; i < 2; i++)
            {
                IReadOnlyList<int> ready = loadout.Tick();
                Assert.AreEqual(1, ready.Count, "use " + (i + 1));
                loadout.MarkFired(0);
            }

            Assert.IsTrue(loadout.IsSpent(0));
            Assert.AreEqual(2, loadout.UsesThisBattle(0));
            Assert.AreEqual(0, loadout.Tick().Count);
            Assert.AreEqual(0, loadout.Tick().Count, "never ready again");
        }

        [Test]
        public void MaxUsesPerBattle_ZeroIsUnlimited_AndTheExecutorHonoursIt()
        {
            SkillSO limited = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            limited.Cooldown = 1;
            limited.MaxUsesPerBattle = 1;
            limited.TargetShape = SkillTargetShape.AllEnemies;
            SkillSO unlimited = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            unlimited.Cooldown = 1;
            unlimited.TargetShape = SkillTargetShape.AllEnemies;

            BattleUnit caster = new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 10, 10, 10, 10, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { limited, unlimited }));
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, hp: 10000);
            List<BattleUnit> all = new List<BattleUnit> { caster, enemy };

            Assert.AreEqual(2, BattleTurnExecutor.ExecuteTurn(caster, all, null, null, null).SkillOutcomes.Count);
            Assert.AreEqual(1, BattleTurnExecutor.ExecuteTurn(caster, all, null, null, null).SkillOutcomes.Count);
            Assert.AreEqual(1, BattleTurnExecutor.ExecuteTurn(caster, all, null, null, null).SkillOutcomes.Count);
            Assert.IsFalse(caster.Skills.IsSpent(1));
        }

        [Test]
        public void InitialCooldown_DefaultKeepsTheOrdinaryCooldown()
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            skill.Cooldown = 3;
            SkillLoadout loadout = new SkillLoadout(new[] { skill });

            Assert.AreEqual(SkillSO.UseCooldownAsInitial, skill.InitialCooldown);
            Assert.AreEqual(3, loadout.RemainingCooldown(0));
            Assert.AreEqual(0, loadout.Tick().Count);
            Assert.AreEqual(0, loadout.Tick().Count);
            Assert.AreEqual(1, loadout.Tick().Count, "third turn, as before the field existed");
        }

        [Test]
        public void InitialCooldown_Zero_FiresOnTheFirstTurn_ThenUsesTheCooldown()
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            skill.Cooldown = 3;
            skill.InitialCooldown = 0;
            SkillLoadout loadout = new SkillLoadout(new[] { skill });

            Assert.AreEqual(1, loadout.Tick().Count, "first turn");
            loadout.MarkFired(0);
            Assert.AreEqual(3, loadout.RemainingCooldown(0));
            Assert.AreEqual(0, loadout.Tick().Count);
            Assert.AreEqual(0, loadout.Tick().Count);
            Assert.AreEqual(1, loadout.Tick().Count);
        }

        [Test]
        public void InitialCooldown_IsTakenAsAuthored_EvenAboveTheCooldown()
        {
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            skill.Cooldown = 1;
            skill.InitialCooldown = 4;

            Assert.AreEqual(4, new SkillLoadout(new[] { skill }).RemainingCooldown(0));
        }

        // ------------------------------------------------------------------ heal scaling

        [Test]
        public void Heal_IsAPercentOfTheCastersSpecialAttack_NotTheTargets()
        {
            BattleUnit caster = new BattleUnit("p0", BattleTeam.Player, new StatBlock(100, 10, 10, 80, 10, 10), HexCoordinate.Zero);
            BattleUnit target = Hurt(new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 10, 10, 0, 10, 10), HexCoordinate.Zero), 40);
            SkillSO heal = Skill(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 25f });

            SkillEffectApplier.Apply(new SkillActivation(heal, new[] { target }), caster);

            Assert.AreEqual(60, target.CurrentHp, "25% of the caster's SpecialAttack 80 is 20");
        }

        [Test]
        public void HealAmount_RoundsAndScalesWithHealScale_AndANullCasterHealsNothing()
        {
            BattleUnit caster = new BattleUnit("p0", BattleTeam.Player, new StatBlock(100, 10, 10, 15, 10, 10), HexCoordinate.Zero);

            Assert.AreEqual((int)System.Math.Round(10.0 * 15 * SkillEffectApplier.HealScale / 100.0, System.MidpointRounding.AwayFromZero),
                            SkillEffectApplier.GetHealAmount(caster, 10f));
            Assert.AreEqual(2, SkillEffectApplier.GetHealAmount(caster, 10f), "1.5 rounds half away from zero to 2 at HealScale 1");
            Assert.AreEqual(1, SkillEffectApplier.GetHealAmount(caster, 9f), "1.35 rounds to 1");
            Assert.AreEqual(0, SkillEffectApplier.GetHealAmount(null, 10f));
        }

        [Test]
        public void Heal_ReadsTheCastersLiveSpecialAttack()
        {
            BattleUnit caster = new BattleUnit("p0", BattleTeam.Player, new StatBlock(100, 10, 10, 50, 10, 10), HexCoordinate.Zero);
            BattleUnit target = Hurt(new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 10, 10, 10, 10, 10), HexCoordinate.Zero), 10);
            SkillSO heal = Skill(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 20f });
            SkillSO buff = Skill(new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.SpecialAttack, Magnitude = 50f, DurationTurns = 2 });

            SkillEffectApplier.Apply(new SkillActivation(heal, new[] { target }), caster);
            Assert.AreEqual(20, target.CurrentHp, "20% of 50");

            SkillEffectApplier.Apply(new SkillActivation(buff, new[] { caster }), caster);
            SkillEffectApplier.Apply(new SkillActivation(heal, new[] { target }), caster);
            Assert.AreEqual(40, target.CurrentHp, "20% of the buffed 100");
        }

        // ------------------------------------------------------------------ helpers

        private static SkillEffect Debuff(StatType stat, float magnitude, int duration, int maxStacks)
        {
            return new SkillEffect
            {
                EffectType = SkillEffectType.DebuffStat,
                AffectedStat = stat,
                Magnitude = magnitude,
                DurationTurns = duration,
                MaxStacks = maxStacks
            };
        }

        private SkillSO HealSkill(SkillTargetingCriterion criterion, SkillTargetingOrder order)
        {
            // 100% of the healer's SpecialAttack (10 in Unit below): a 10 HP heal.
            SkillSO skill = Skill(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 100f });
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.TargetSide = SkillTargetSide.Ally;
            skill.TargetingCriterion = criterion;
            skill.TargetingOrder = order;
            skill.Range = 10;
            return skill;
        }

        private static BattleUnit Hurt(BattleUnit unit, int currentHp)
        {
            unit.CurrentHp = currentHp;
            return unit;
        }

        private static void Fire(SkillSO skill, BattleUnit target)
        {
            BattleUnit caster = Unit("caster", target.Team == BattleTeam.Player ? BattleTeam.Enemy : BattleTeam.Player);
            SkillEffectApplier.Apply(new SkillActivation(skill, new[] { target }), caster);
        }

        private SkillSO Skill(params SkillEffect[] effects)
        {
            SkillSO skill = new SkillSO();
            skill.Effects.AddRange(effects);
            _created.Add(skill);
            return skill;
        }

        private static BattleUnit Unit(string id, BattleTeam team, int hp = 100, int attack = 10, int defense = 10)
        {
            return new BattleUnit(id, team, new StatBlock(hp, attack, defense, 10, 10, 10), HexCoordinate.Zero);
        }
    }
}
