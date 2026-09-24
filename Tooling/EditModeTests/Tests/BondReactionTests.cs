using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Behaviour bonds (<see cref="BondReaction"/>): the guardian intercept (adjacent, unstunned,
    /// cooldown and cap, never an area skill), ally-crit follow-ups (no self-trigger, no re-fire off
    /// a reaction's own crit), the HP-threshold latch and its re-arm, a turn-start cleanse that lets
    /// a stunned ally act, silent reaction defeats for the avatar's passives, determinism and
    /// byte-identical draws without a reacting bond, the <c>DistinctStances</c> condition, the
    /// <c>Cleanse</c> effect, and the reaction data (builder and validator).
    /// </summary>
    public class BondReactionTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Guardian intercept.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Intercept_AnAdjacentGuardianTakesTheHit_ThenWaitsOutItsCooldown()
        {
            Guardian(out BattleUnit ranged, out BattleUnit guardian, out BattleUnit enemy, out List<BattleUnit> roster, out TeamBondLoadout bonds);
            int rangedHp = ranged.CurrentHp;
            int guardianHp = guardian.CurrentHp;

            BattleTurnResult first = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds);

            Assert.AreEqual(rangedHp, ranged.CurrentHp, "the aimed-at beast is untouched");
            Assert.Less(guardian.CurrentHp + StatusEffects.ShieldPoints(guardian), guardianHp + 15, "the guardian took the hit (behind its own small shield)");
            Assert.AreEqual(1, first.BondReactions.Count);
            Assert.AreSame(guardian, first.BondReactions[0].Reactor);
            Assert.AreSame(ranged, first.BondReactions[0].InterceptedFrom);
            Assert.AreEqual(BondTrigger.EnemyTargetsAlly, first.BondReactions[0].Trigger);
            CollectionAssert.AreEqual(new[] { guardian }, first.SkillOutcomes[0].Activation.Targets, "the enemy's activation was swapped onto the guardian");

            BattleTurnResult second = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds);
            Assert.AreEqual(0, second.BondReactions.Count, "cooldown 1: not again until the guardian's own turn");
            Assert.Less(ranged.CurrentHp, rangedHp);

            BattleTurnExecutor.ExecuteTurn(guardian, roster, null, null, null, null, bonds);
            BattleTurnResult third = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds);
            Assert.AreEqual(1, third.BondReactions.Count, "its own turn ticked the cooldown off");
        }

        [Test]
        public void Intercept_NeedsAnAdjacentUnstunnedGuardian_AndRespectsItsCap()
        {
            Guardian(out BattleUnit ranged, out BattleUnit guardian, out BattleUnit enemy, out List<BattleUnit> roster, out TeamBondLoadout bonds);
            Stun(guardian);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds).BondReactions.Count, "a stunned guardian cannot step in");

            Guardian(out ranged, out guardian, out enemy, out roster, out bonds);
            guardian.Position = new HexCoordinate(-3, 0);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds).BondReactions.Count, "too far away");

            Guardian(out ranged, out guardian, out enemy, out roster, out bonds, cooldown: 0, maxPerMember: 1);
            Assert.AreEqual(1, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds).BondReactions.Count);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds).BondReactions.Count, "MaxPerMember 1");
        }

        [Test]
        public void Intercept_NeverAnAreaSkill_AndNeverGuardsAnotherMember()
        {
            Guardian(out BattleUnit ranged, out BattleUnit guardian, out BattleUnit enemy, out List<BattleUnit> roster, out TeamBondLoadout bonds);
            enemy.Skills = new SkillLoadout(new[] { Skill("sweep", SkillTargetShape.AllEnemies, 8, Damage(40f)) });
            int rangedHp = ranged.CurrentHp;

            BattleTurnResult sweep = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds);

            Assert.AreEqual(0, sweep.BondReactions.Count);
            Assert.Less(ranged.CurrentHp, rangedHp, "an area skill still lands on everyone");

            // A second Vanguard (a member) is aimed at: NonMembers means the guardian lets it be.
            Guardian(out ranged, out guardian, out enemy, out roster, out bonds, aimAtMember: true);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, null, null, bonds).BondReactions.Count);
        }

        // ---------------------------------------------------------------------------------------
        // Crit follow-ups.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AllyCrit_AnotherMemberFollowsUp_ButNeverItsOwnCrit_AndAReactionsCritTriggersNothing()
        {
            SkillSO jab = Skill("jab", SkillTargetShape.SingleTarget, 5, Damage(10f));
            BattleUnit s1 = new BattleUnit("p1", BattleTeam.Player, new StatBlock(1000, 100, 100, 50, 100, 10, 0, 100), new HexCoordinate(0, 0),
                                           new SkillLoadout(new[] { jab }), null, 1, CombatStance.Skirmisher);
            BattleUnit s2 = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 100, 50, 100, 10, 0, 100), new HexCoordinate(0, 1),
                                           new SkillLoadout(new[] { jab }), null, 1, CombatStance.Skirmisher);
            BattleUnit enemy = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100000, 10, 100, 10, 100, 10), new HexCoordinate(3, 0));
            List<BattleUnit> team = new List<BattleUnit> { s1, s2 };
            List<BattleUnit> roster = new List<BattleUnit> { s1, s2, enemy };
            TeamBondSO pack = Bond("pack", TeamBondCondition.Stance, CombatStance.Skirmisher, 2, new BondReaction
            {
                Trigger = BondTrigger.AllyCrit,
                Target = BondReactionTarget.TriggerTarget,
                Range = 4,
                Cooldown = 1,
                Effects = new List<SkillEffect> { Damage(45f) },
            });
            TeamBondLoadout bonds = Begin(pack, team, roster);
            System.Random rng = new System.Random(5);

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(s1, roster, null, rng, null, null, bonds);

            Assert.IsTrue(turn.SkillOutcomes[0].Activation.Hits[0].Roll.IsCrit, "crit chance 100");
            Assert.AreEqual(1, turn.BondReactions.Count, "s2 follows up; its own (certain) crit fires nothing back");
            Assert.AreSame(s2, turn.BondReactions[0].Reactor);
            Assert.AreSame(s1, turn.BondReactions[0].TriggeringUnit);
            Assert.IsTrue(turn.BondReactions[0].Activation.Hits[0].Roll.IsCrit);

            // With only the critting beast in the bond, nobody may react to its crit.
            s2.IsDefeated = true;
            BattleTurnExecutor.ExecuteTurn(s1, roster, null, rng, null, null, bonds);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(s1, roster, null, rng, null, null, bonds).BondReactions.Count, "no self-trigger");
        }

        // ---------------------------------------------------------------------------------------
        // HP threshold latch.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void BelowHpPercent_OneAttemptPerCrossing_ReArmedAtOrAboveTheThreshold()
        {
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1);
            BattleUnit idle = Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 5);
            List<BattleUnit> team = new List<BattleUnit> { a, b };
            List<BattleUnit> roster = new List<BattleUnit> { a, b, idle };
            TeamBondSO rock = Bond("rock", TeamBondCondition.Stance, CombatStance.Vanguard, 2, new BondReaction
            {
                Trigger = BondTrigger.AllyBelowHpPercent,
                Target = BondReactionTarget.TriggeringAlly,
                HpThresholdPercent = 50,
                ReactorOrder = BondReactorOrder.HealthiestFirst,
                Effects = new List<SkillEffect> { Shield(10) },
            });
            TeamBondLoadout bonds = Begin(rock, team, roster);

            a.CurrentHp = 400;
            BattleTurnResult first = BattleTurnExecutor.ExecuteTurn(idle, roster, null, null, null, null, bonds);
            BattleTurnResult latched = BattleTurnExecutor.ExecuteTurn(idle, roster, null, null, null, null, bonds);
            a.CurrentHp = 500;
            BattleTurnResult reArmed = BattleTurnExecutor.ExecuteTurn(idle, roster, null, null, null, null, bonds);
            a.CurrentHp = 499;
            BattleTurnResult again = BattleTurnExecutor.ExecuteTurn(idle, roster, null, null, null, null, bonds);

            Assert.AreEqual(1, first.BondReactions.Count);
            Assert.AreSame(b, first.BondReactions[0].Reactor, "the healthiest member reacts");
            Assert.AreSame(a, first.BondReactions[0].TriggeringUnit);
            Assert.AreEqual(0, latched.BondReactions.Count, "still below: no second attempt");
            Assert.AreEqual(0, reArmed.BondReactions.Count, "at the threshold: re-armed, nothing fires");
            Assert.AreEqual(1, again.BondReactions.Count, "a new crossing");
        }

        // ---------------------------------------------------------------------------------------
        // Turn-start cleanse.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void TurnStartCleanse_LetsAStunnedAllyAct()
        {
            SkillSO jab = Skill("jab", SkillTargetShape.SingleTarget, 5, Damage(10f));
            BattleUnit light = new BattleUnit("p1", BattleTeam.Player, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(0, 0),
                                              new SkillLoadout(new[] { jab }), new[] { Element.Light }, 1, CombatStance.Ranged);
            BattleUnit dark = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(0, 1), null,
                                             new[] { Element.Dark }, 1, CombatStance.Ranged);
            BattleUnit enemy = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100000, 10, 100, 10, 100, 10), new HexCoordinate(3, 0));
            List<BattleUnit> team = new List<BattleUnit> { light, dark };
            List<BattleUnit> roster = new List<BattleUnit> { light, dark, enemy };
            TeamBondSO twilight = Bond("twilight", TeamBondCondition.Elements, CombatStance.Vanguard, 2, new BondReaction
            {
                Trigger = BondTrigger.AllyTurnStartAfflicted,
                Target = BondReactionTarget.TriggeringAlly,
                MaxPerMember = 2,
                Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.Cleanse } },
            });
            twilight.Elements = new List<Element> { Element.Light, Element.Dark };
            TeamBondLoadout bonds = Begin(twilight, team, roster);

            Stun(light);
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(light, roster, null, null, null, null, bonds);

            Assert.IsFalse(turn.Stunned, "cleansed before its statuses ticked");
            Assert.AreEqual(BattleSkillStatus.Fired, turn.SkillOutcomes[0].Status);
            Assert.AreEqual(1, turn.BondReactions.Count);
            Assert.AreSame(dark, turn.BondReactions[0].Reactor, "the stunned beast cannot react; its partner does");

            // Without the bond the same stun skips the turn.
            Stun(light);
            Assert.IsTrue(BattleTurnExecutor.ExecuteTurn(light, roster, null, null, null, null, null).Stunned);
        }

        [Test]
        public void Cleanse_RemovesStunAndDamageOverTime_ButKeepsShields()
        {
            BattleUnit unit = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            Stun(unit);
            SkillEffectApplier.Apply(new SkillActivation(Skill("burn", SkillTargetShape.Self, 1,
                                                               new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 20, DurationTurns = 3 },
                                                               Shield(30)), new[] { unit }), unit);
            Assert.AreEqual(3, unit.Statuses.Count);

            SkillEffectApplier.Apply(new SkillActivation(Skill("clean", SkillTargetShape.Self, 1, new SkillEffect { EffectType = SkillEffectType.Cleanse }), new[] { unit }),
                                     unit);

            Assert.AreEqual(1, unit.Statuses.Count);
            Assert.AreEqual(StatusType.Shield, unit.Statuses[0].Type);
            Assert.IsFalse(StatusEffects.IsAfflicted(unit));
        }

        // ---------------------------------------------------------------------------------------
        // No chaining into the avatar's passives.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ReactionDefeat_FiresNoEnemyDefeatedPassive()
        {
            PassiveSkillSO onKill = new PassiveSkillSO();
            _created.Add(onKill);
            onKill.PassiveId = "onkill";
            onKill.Trigger = PassiveTrigger.EnemyDefeated;
            onKill.TargetScope = PassiveTarget.AllAllies;
            onKill.Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.Attack, Magnitude = 1 } };
            PassiveLoadout passives = new PassiveLoadout(new[] { new PassiveInstance(onKill) });
            BattleUnit avatar = BattleAvatar.Create(null, default(StatBlock), null);

            SkillSO jab = Skill("jab", SkillTargetShape.SingleTarget, 5, Damage(10f));
            BattleUnit hitter = new BattleUnit("p1", BattleTeam.Player, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(0, 0),
                                               new SkillLoadout(new[] { jab }), null, 1, CombatStance.Vanguard);
            BattleUnit tough = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100000, 10, 100, 10, 100, 10), new HexCoordinate(1, 0));
            BattleUnit frail = new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(1, 1, 1, 1, 1, 10), new HexCoordinate(0, 3));
            List<BattleUnit> team = new List<BattleUnit> { hitter };
            List<BattleUnit> roster = new List<BattleUnit> { hitter, tough, frail };
            TeamBondSO blast = Bond("blast", TeamBondCondition.Stance, CombatStance.Vanguard, 1, new BondReaction
            {
                Trigger = BondTrigger.MemberHit,
                Target = BondReactionTarget.EnemiesNearReactor,
                Range = 8,
                Cooldown = 1,
                Effects = new List<SkillEffect> { Damage(60f) },
            });
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { blast }, MembersOf(team), team);
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> _);

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(hitter, roster, null, null, avatar, passives, bonds);
            BattleTurnResult later = BattleTurnExecutor.ExecuteTurn(tough, roster, null, null, avatar, passives, bonds);

            Assert.AreEqual(1, turn.BondReactions.Count);
            Assert.IsTrue(frail.IsDefeated, "the reaction's own hit felled it");
            Assert.AreEqual(0, turn.PassiveActivations.Count, "no chaining: a reaction's defeat fires no passive");
            Assert.AreEqual(0, later.PassiveActivations.Count, "...not on a later look either");
        }

        // ---------------------------------------------------------------------------------------
        // Determinism and the draw-for-draw guarantee.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void BattleStartOnlyBonds_PlayEveryTurnAndDrawExactlyAsWithoutBonds()
        {
            string withBonds = StatBondTrace(31, true, out int nextWith);
            string without = StatBondTrace(31, false, out int nextWithout);

            Assert.AreEqual(without, withBonds);
            Assert.AreEqual(nextWithout, nextWith, "the battle rng is left in the same state");
        }

        [Test]
        public void ReactingBattle_IsDeterministic()
        {
            Assert.AreEqual(ReactingTrace(41), ReactingTrace(41));
        }

        // ---------------------------------------------------------------------------------------
        // DistinctStances.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void DistinctStances_CountsStancesFielded_AndEveryBeastIsAMember()
        {
            TeamBondSO arms = Bond("arms", TeamBondCondition.DistinctStances, CombatStance.Vanguard, 3, new BondReaction());
            arms.Tiers[0].Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.Attack, Magnitude = 1 } };

            List<ActiveTeamBond> two = TeamBondResolver.Resolve(new[] { arms }, new List<TeamBondMember>
            {
                new TeamBondMember("a", CombatStance.Vanguard, null), new TeamBondMember("b", CombatStance.Vanguard, null), new TeamBondMember("c", CombatStance.Ranged, null),
            });
            List<TeamBondMember> mixed = new List<TeamBondMember>
            {
                new TeamBondMember("a", CombatStance.Vanguard, null), new TeamBondMember("b", CombatStance.Skirmisher, null),
                new TeamBondMember("c", CombatStance.Ranged, null), new TeamBondMember("d", CombatStance.Ranged, null),
            };
            List<ActiveTeamBond> three = TeamBondResolver.Resolve(new[] { arms }, mixed);

            Assert.IsEmpty(two, "two stances do not reach MinCount 3");
            Assert.AreEqual(1, three.Count);
            Assert.AreEqual(3, three[0].Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, three[0].Members);
        }

        // ---------------------------------------------------------------------------------------
        // Data.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Builder_MapsEveryReactionField()
        {
            TeamBondData data = ValidReactionBond();
            data.Tiers[0].Reaction.TriggerFilter = "Members";
            data.Tiers[0].Reaction.ReactorOrder = "HealthiestFirst";
            data.Tiers[0].Reaction.MaxPerMember = 2;
            data.Tiers[0].Reaction.MaxPerTriggerUnit = 3;
            data.Tiers[0].Reaction.MaxPerBattle = 4;
            TeamBondSO bond = new TeamBondSO();
            _created.Add(bond);

            SkillLibraryBuilder.ApplyTeamBond(data, bond);
            BondReaction r = bond.Tiers[0].Reaction;

            Assert.IsTrue(bond.Tiers[0].HasReaction);
            Assert.AreEqual(BondTrigger.AllyHitByEnemy, r.Trigger);
            Assert.AreEqual(BondAction.Apply, r.Action);
            Assert.AreEqual(BondReactionTarget.Attacker, r.Target);
            Assert.AreEqual(BondTriggerFilter.Members, r.TriggerFilter);
            Assert.AreEqual(BondReactorOrder.HealthiestFirst, r.ReactorOrder);
            Assert.AreEqual(40, r.Chance);
            Assert.AreEqual(1, r.Cooldown);
            Assert.AreEqual(3, r.Range);
            Assert.AreEqual(2, r.MaxPerMember);
            Assert.AreEqual(3, r.MaxPerTriggerUnit);
            Assert.AreEqual(4, r.MaxPerBattle);
            Assert.AreEqual(40f, r.Effects[0].Magnitude);
            Assert.AreEqual(0, bond.Tiers[0].Effects.Count, "a reaction-only tier");

            data.Tiers[0].Reaction = null;
            SkillLibraryBuilder.ApplyTeamBond(data, bond);
            Assert.IsFalse(bond.Tiers[0].HasReaction, "no reaction data: an inert reaction");
        }

        [Test]
        public void Validator_AcceptsAReactionOnlyTier_AndADistinctStancesBond()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData arms = new TeamBondData
            {
                BondId = "test_arms",
                DisplayName = "Arms",
                Description = "d",
                Condition = "DistinctStances",
                Scope = "Members",
                Tiers = new[]
                {
                    new TeamBondTierData
                    {
                        MinCount = 3,
                        Reaction = new BondReactionData
                        {
                            Trigger = "AllyDefeated",
                            Target = "Team",
                            MaxPerBattle = 2,
                            Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 15, IsPercent = true, DurationTurns = 3 } },
                        },
                    },
                },
            };
            library.TeamBonds = new List<TeamBondData>(library.TeamBonds) { ValidReactionBond(), arms }.ToArray();

            Assert.IsEmpty(SkillLibraryValidator.Validate(library, BeastRosterTests.LoadRoster()));
        }

        [TestCase("BadTrigger", "is not a BondTrigger name")]
        [TestCase("NoCooldownOnHit", "needs a Cooldown of at least 1")]
        [TestCase("TooStrong", "damage power is at most 60")]
        [TestCase("StunTooLikely", "lands at most 50%")]
        [TestCase("StunTooLong", "lasts exactly 1 turn")]
        [TestCase("InterceptOnHit", "Intercept is the EnemyTargetsAlly reaction")]
        [TestCase("WrongSide", "is for the team, but this reaction lands on enemies")]
        [TestCase("NoRadius", "EnemiesNearReactor needs a Range")]
        [TestCase("NoAttacker", "has no Attacker to land on")]
        [TestCase("NoEffects", "has no effects")]
        [TestCase("ChanceZero", "Chance is 0")]
        [TestCase("Knockback", "cannot knock back")]
        [TestCase("OnScaling", "a PerCount (scaling) bond has no Reaction")]
        [TestCase("DistinctWithStance", "a DistinctStances bond lists no Elements, Species or Stance")]
        public void Validator_RejectsBadReactions(string fault, string fragment)
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData bond = ValidReactionBond();
            BondReactionData r = bond.Tiers[0].Reaction;
            EffectData effect = r.Effects[0];

            switch (fault)
            {
                case "BadTrigger":
                    r.Trigger = "WhenItRains";
                    break;
                case "NoCooldownOnHit":
                    r.Cooldown = 0;
                    break;
                case "TooStrong":
                    effect.Magnitude = 61;
                    break;
                case "StunTooLikely":
                    r.Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Stun", DurationTurns = 1, Chance = 60 } };
                    r.Chance = 100;
                    break;
                case "StunTooLong":
                    r.Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Stun", DurationTurns = 2, Chance = 20 } };
                    break;
                case "InterceptOnHit":
                    r.Action = "Intercept";
                    break;
                case "WrongSide":
                    effect.EffectType = "Heal";
                    break;
                case "NoRadius":
                    r.Target = "EnemiesNearReactor";
                    r.Range = 0;
                    break;
                case "NoAttacker":
                    r.Trigger = "MemberHit";
                    break;
                case "NoEffects":
                    r.Effects = new EffectData[0];
                    break;
                case "ChanceZero":
                    r.Chance = 0;
                    break;
                case "Knockback":
                    r.Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Knockback", Magnitude = 1 } };
                    break;
                case "OnScaling":
                    bond.Condition = "Stance";
                    bond.Elements = new string[0];
                    bond.Stance = "Ranged";
                    bond.PerCount = true;
                    bond.MaxCount = 3;
                    bond.Tiers[0].MinCount = 1;
                    bond.Tiers[0].Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "CritChance", Magnitude = 3 } };
                    break;
                case "DistinctWithStance":
                    bond.Condition = "DistinctStances";
                    bond.Elements = new string[0];
                    bond.Stance = "Ranged";
                    break;
            }

            library.TeamBonds = new List<TeamBondData>(library.TeamBonds) { bond }.ToArray();
            List<string> errors = SkillLibraryValidator.Validate(library);

            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "Expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        /// <summary>A Ranged beast with a Vanguard guardian next to it (and, optionally, a second Vanguard as the target), and an enemy that aims at the weakest.</summary>
        private void Guardian(out BattleUnit ranged, out BattleUnit guardian, out BattleUnit enemy, out List<BattleUnit> roster, out TeamBondLoadout bonds,
                              int cooldown = 1, int maxPerMember = 0, bool aimAtMember = false)
        {
            ranged = new BattleUnit("p1", BattleTeam.Player, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(1, 0), null, null, 1,
                                    aimAtMember ? CombatStance.Vanguard : CombatStance.Ranged);
            ranged.CurrentHp = 900;
            guardian = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(1, 1), null, null, 1, CombatStance.Vanguard);
            SkillSO bite = Skill("bite", SkillTargetShape.SingleTarget, 5, Damage(60f));
            bite.TargetingCriterion = SkillTargetingCriterion.CurrentHp;
            bite.TargetingOrder = SkillTargetingOrder.Lowest;
            enemy = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100000, 100, 100, 100, 100, 10), new HexCoordinate(3, 0), new SkillLoadout(new[] { bite }));
            List<BattleUnit> team = new List<BattleUnit> { ranged, guardian };
            roster = new List<BattleUnit> { ranged, guardian, enemy };
            TeamBondSO guard = Bond("guardian", TeamBondCondition.Stance, CombatStance.Vanguard, 1, new BondReaction
            {
                Trigger = BondTrigger.EnemyTargetsAlly,
                Action = BondAction.Intercept,
                Target = BondReactionTarget.Self,
                TriggerFilter = BondTriggerFilter.NonMembers,
                Range = 1,
                Cooldown = cooldown,
                MaxPerMember = maxPerMember,
                Effects = new List<SkillEffect> { Shield(15) },
            });
            bonds = Begin(guard, team, roster);
        }

        private string StatBondTrace(int seed, bool passBonds, out int next)
        {
            SkillSO hit = Skill("hit", SkillTargetShape.AllEnemies, 1, Damage(60f));
            BattleUnit a = new BattleUnit("p1", BattleTeam.Player, new StatBlock(400, 100, 100, 0, 0, 10, 0, 20), HexCoordinate.Zero, new SkillLoadout(new[] { hit }), null, 1,
                                          CombatStance.Vanguard);
            BattleUnit b = new BattleUnit("p2", BattleTeam.Player, new StatBlock(400, 100, 100, 0, 0, 12, 0, 20), new HexCoordinate(0, 1), new SkillLoadout(new[] { hit }),
                                          null, 1, CombatStance.Vanguard);
            BattleUnit e = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(1500, 100, 100, 0, 0, 11, 0, 20), new HexCoordinate(3, 0), new SkillLoadout(new[] { hit }));
            List<BattleUnit> team = new List<BattleUnit> { a, b };
            List<BattleUnit> roster = new List<BattleUnit> { a, b, e };
            TeamBondSO wall = Bond("wall", TeamBondCondition.Stance, CombatStance.Vanguard, 2, new BondReaction());
            wall.Tiers[0].Effects = new List<SkillEffect> { Shield(40), new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.CritChance, Magnitude = 10 } };
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { wall }, MembersOf(team), team);
            System.Random rng = new System.Random(seed);
            BattleTurnExecutor.BeginBattle(roster, null, rng, null, null, bonds, out IReadOnlyList<TeamBondActivation> _);
            TurnManager turns = new TurnManager(roster);
            System.Text.StringBuilder trace = new System.Text.StringBuilder();

            for (int t = 0; t < 60 && !(e.IsDefeated || (a.IsDefeated && b.IsDefeated)); t++)
            {
                BattleUnit current = turns.CurrentUnit;
                BattleTurnExecutor.ExecuteTurn(current, roster, null, rng, null, null, passBonds ? bonds : null);
                trace.Append(current.Id).Append(':').Append(a.CurrentHp).Append('/').Append(b.CurrentHp).Append('/').Append(e.CurrentHp).Append(' ');
                turns.AdvanceTurn();
            }

            next = rng.Next();
            return trace.ToString();
        }

        private string ReactingTrace(int seed)
        {
            SkillSO hit = Skill("hit", SkillTargetShape.SingleTarget, 1, Damage(60f));
            BattleUnit a = new BattleUnit("p1", BattleTeam.Player, new StatBlock(600, 100, 100, 0, 0, 10, 0, 30), HexCoordinate.Zero, new SkillLoadout(new[] { hit }), null, 1,
                                          CombatStance.Skirmisher);
            BattleUnit b = new BattleUnit("p2", BattleTeam.Player, new StatBlock(600, 100, 100, 0, 0, 12, 0, 30), new HexCoordinate(0, 1), new SkillLoadout(new[] { hit }),
                                          null, 1, CombatStance.Skirmisher);
            BattleUnit e = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(3000, 100, 100, 0, 0, 11, 0, 20), new HexCoordinate(1, 0), new SkillLoadout(new[] { hit }));
            List<BattleUnit> team = new List<BattleUnit> { a, b };
            List<BattleUnit> roster = new List<BattleUnit> { a, b, e };
            TeamBondSO pack = Bond("pack", TeamBondCondition.Stance, CombatStance.Skirmisher, 2, new BondReaction
            {
                Trigger = BondTrigger.AllyCrit,
                Target = BondReactionTarget.TriggerTarget,
                Chance = 50,
                Cooldown = 1,
                Effects = new List<SkillEffect> { Damage(45f) },
            });
            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(seed), null, null,
                                                               TeamBondLoadout.For(new[] { pack }, MembersOf(team), team));
            int reactions = 0;
            foreach (BattleTurnResult turn in result.Turns)
            {
                reactions += turn.BondReactions.Count;
            }

            return result.Outcome + " " + result.ElapsedTicks + " " + reactions + " " + a.CurrentHp + "/" + b.CurrentHp + "/" + e.CurrentHp;
        }

        private TeamBondLoadout Begin(TeamBondSO bond, List<BattleUnit> team, List<BattleUnit> roster)
        {
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { bond }, MembersOf(team), team);
            BattleTurnExecutor.BeginBattle(roster, null, null, null, null, bonds, out IReadOnlyList<TeamBondActivation> _);
            return bonds;
        }

        private void Stun(BattleUnit unit)
        {
            SkillSO stun = Skill("stun", SkillTargetShape.Self, 1, new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Stun, DurationTurns = 1 });
            SkillEffectApplier.Apply(new SkillActivation(stun, new[] { unit }), unit);
        }

        private static TeamBondData ValidReactionBond()
        {
            return new TeamBondData
            {
                BondId = "test_crossfire",
                DisplayName = "Test",
                Description = "d",
                Condition = "Elements",
                Elements = new[] { "Air", "Lightning" },
                Tiers = new[]
                {
                    new TeamBondTierData
                    {
                        MinCount = 2,
                        Reaction = new BondReactionData
                        {
                            Trigger = "AllyHitByEnemy",
                            Target = "Attacker",
                            Chance = 40,
                            Cooldown = 1,
                            Range = 3,
                            Effects = new[] { new EffectData { EffectType = "Damage", Magnitude = 40 } },
                        },
                    },
                },
            };
        }

        private static List<TeamBondMember> MembersOf(List<BattleUnit> team)
        {
            return team.ConvertAll(u => new TeamBondMember(u.Id, u.Stance, u.Elements));
        }

        private static BattleUnit Unit(string id, BattleTeam team, CombatStance stance, int column)
        {
            return new BattleUnit(id, team, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(column, 0), null, null, 1, stance);
        }

        private static SkillEffect Damage(float power)
        {
            return new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power };
        }

        private static SkillEffect Shield(int percent)
        {
            return new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = percent, DurationTurns = 2 };
        }

        private TeamBondSO Bond(string id, TeamBondCondition condition, CombatStance stance, int minCount, BondReaction reaction)
        {
            TeamBondSO bond = new TeamBondSO();
            _created.Add(bond);
            bond.BondId = id;
            bond.DisplayName = id;
            bond.Condition = condition;
            bond.Stance = stance;
            bond.Tiers = new List<TeamBondTier> { new TeamBondTier { MinCount = minCount, Effects = new List<SkillEffect>(), Reaction = reaction } };
            return bond;
        }

        private SkillSO Skill(string id, SkillTargetShape shape, int range, params SkillEffect[] effects)
        {
            SkillSO skill = new SkillSO();
            _created.Add(skill);
            skill.SkillId = id;
            skill.TargetShape = shape;
            skill.TargetSide = shape == SkillTargetShape.Self || shape == SkillTargetShape.AllAllies ? SkillTargetSide.Ally : SkillTargetSide.Enemy;
            skill.Range = range;
            skill.Cooldown = 1;
            skill.Effects = new List<SkillEffect>(effects);
            return skill;
        }
    }
}
