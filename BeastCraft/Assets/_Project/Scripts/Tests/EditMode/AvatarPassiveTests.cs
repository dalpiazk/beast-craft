using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Avatar passives: every trigger and when it must not fire, proc chance, per-battle caps,
    /// internal cooldowns counted in avatar turns, target scopes, level scaling and tier bonus
    /// effects, the avatar's skill book and its equip rules, practice-use counts, determinism, and
    /// that a battle with no passives is unchanged.
    /// </summary>
    public class AvatarPassiveTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Battle start: Aura and BattleStart.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Aura_AppliesAtBattleStart_ToAlliesOnly_AndLastsTheWholeBattle()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 5));
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 0, Damage(10f));
            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { strike }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(300, 20, 100, 0, 0, 10), new HexCoordinate(1, 0),
                                              new SkillLoadout(new[] { strike }));
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(5), MakeAvatar(),
                                                               Loadout(aura));

            Assert.AreEqual(1, result.OpeningPassiveActivations.Count);
            Assert.AreEqual(PassiveTrigger.Aura, result.OpeningPassiveActivations[0].Trigger);
            Assert.Greater(result.Turns.Count, 4, "a battle of several turns");
            Assert.AreEqual(105, player.Stats.Attack, "the aura is a permanent change: no turn reverts it");
            Assert.AreEqual(20, enemy.Stats.Attack, "enemies are not allies");
            Assert.AreEqual(0, player.ActiveStatModifiers.Count, "DurationTurns 0 never enters the timed list");
        }

        [Test]
        public void BattleStart_FiresOnce_AfterEveryAura_KeepingItsDuration()
        {
            PassiveSkillSO start = Passive("start", PassiveTrigger.BattleStart, PassiveTarget.AllAllies, Buff(StatType.Defense, 10, 1));
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 5));
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit enemy = Beast("e", BattleTeam.Enemy);
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            PassiveLoadout passives = Loadout(start, aura);
            BattleUnit avatar = MakeAvatar();

            IReadOnlyList<PassiveActivation> opening = BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            Assert.AreEqual(2, opening.Count);
            Assert.AreEqual("aura", opening[0].Passive.PassiveId, "auras first, whatever the slot order");
            Assert.AreEqual("start", opening[1].Passive.PassiveId);
            Assert.AreEqual(110, player.Stats.Defense);
            Assert.AreEqual(0, BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives).Count, "runs once per loadout");

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives);
            Assert.AreEqual(0, turn.PassiveActivations.Count);
            Assert.AreEqual(100, player.Stats.Defense, "a 1-turn battle-start buff expires on the ally's next turn");
            Assert.AreEqual(105, player.Stats.Attack);
        }

        [Test]
        public void ExecuteTurn_WithoutBeginBattle_RunsTheBattleStartHookOnTheFirstTurn()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 5));
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit enemy = Beast("e", BattleTeam.Enemy);
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            PassiveLoadout passives = Loadout(aura);

            BattleTurnResult first = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, MakeAvatar(), passives);

            Assert.AreEqual(1, first.PassiveActivations.Count);
            Assert.IsTrue(passives.HasBegun);
            Assert.AreEqual(105, player.Stats.Attack);
        }

        // ---------------------------------------------------------------------------------------
        // Defeats and crits.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void EnemyDefeated_FiresPerFallenEnemy_CreditingTheBeastWhoseTurnItIs()
        {
            PassiveSkillSO onKill = Passive("onkill", PassiveTrigger.EnemyDefeated, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 1));
            PassiveSkillSO onLoss = Passive("onloss", PassiveTrigger.AllyDefeated, PassiveTarget.AllAllies, Heal(1f));
            SkillSO sweep = Skill("sweep", SkillTargetShape.AllEnemies, 0, Damage(1000f));
            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { sweep }));
            BattleUnit weak1 = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(10, 1, 1, 0, 0, 10), new HexCoordinate(1, 0));
            BattleUnit weak2 = new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(10, 1, 1, 0, 0, 10), new HexCoordinate(2, 0));
            BattleUnit tough = new BattleUnit("e3", BattleTeam.Enemy, new StatBlock(1000000, 1, 1000, 0, 0, 10), new HexCoordinate(3, 0));
            List<BattleUnit> roster = new List<BattleUnit> { player, weak1, weak2, tough };
            PassiveLoadout passives = Loadout(onKill, onLoss);
            BattleUnit avatar = MakeAvatar();
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives);

            Assert.IsTrue(weak1.IsDefeated && weak2.IsDefeated && !tough.IsDefeated);
            Assert.AreEqual(2, turn.PassiveActivations.Count, "once per fallen enemy; no ally fell");
            Assert.AreEqual(PassiveTrigger.EnemyDefeated, turn.PassiveActivations[0].Trigger);
            Assert.AreSame(player, turn.PassiveActivations[0].TriggeringUnit);
            Assert.AreEqual(102, player.Stats.Attack);

            BattleTurnResult later = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives);
            Assert.AreEqual(0, later.PassiveActivations.Count, "a defeat triggers once, not on every later look");
        }

        [Test]
        public void AllyDefeated_FiresOnAnEnemyTurn_AndHealsTheSurvivors()
        {
            PassiveSkillSO onLoss = Passive("onloss", PassiveTrigger.AllyDefeated, PassiveTarget.AllAllies, Heal(50f));
            SkillSO sweep = Skill("sweep", SkillTargetShape.AllEnemies, 0, Damage(1000f));
            BattleUnit fragile = new BattleUnit("p1", BattleTeam.Player, new StatBlock(10, 1, 1, 0, 0, 10), HexCoordinate.Zero);
            BattleUnit sturdy = new BattleUnit("p2", BattleTeam.Player, new StatBlock(100000, 1, 1000, 0, 0, 10), new HexCoordinate(0, 1));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000, 100, 100, 0, 0, 10), new HexCoordinate(2, 0),
                                              new SkillLoadout(new[] { sweep }));
            List<BattleUnit> roster = new List<BattleUnit> { fragile, sturdy, enemy };
            PassiveLoadout passives = Loadout(onLoss);
            BattleUnit avatar = MakeAvatar();
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, avatar, passives);

            Assert.IsTrue(fragile.IsDefeated);
            Assert.AreEqual(1, turn.PassiveActivations.Count);
            Assert.AreSame(fragile, turn.PassiveActivations[0].TriggeringUnit);
            CollectionAssert.AreEqual(new[] { sturdy }, turn.PassiveActivations[0].Activation.Targets, "a fallen ally is not healed");
        }

        [Test]
        public void TriggeringUnit_ThatIsDefeated_DoesNotFire_AndIsNotCounted()
        {
            PassiveSkillSO onLoss = Passive("onloss", PassiveTrigger.AllyDefeated, PassiveTarget.TriggeringUnit, Heal(50f));
            SkillSO sweep = Skill("sweep", SkillTargetShape.AllEnemies, 0, Damage(1000f));
            BattleUnit fragile = new BattleUnit("p1", BattleTeam.Player, new StatBlock(10, 1, 1, 0, 0, 10), HexCoordinate.Zero);
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000, 100, 100, 0, 0, 10), new HexCoordinate(2, 0),
                                              new SkillLoadout(new[] { sweep }));
            List<BattleUnit> roster = new List<BattleUnit> { fragile, enemy };
            PassiveLoadout passives = Loadout(onLoss);
            BattleUnit avatar = MakeAvatar();
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, avatar, passives);

            Assert.IsTrue(fragile.IsDefeated);
            Assert.AreEqual(0, turn.PassiveActivations.Count);
            Assert.AreEqual(0, passives.Passives[0].TriggerCount);
        }

        [Test]
        public void DefeatsCausedByAPassive_NeverTriggerDefeatPassives()
        {
            PassiveSkillSO opener = Passive("opener", PassiveTrigger.BattleStart, PassiveTarget.AllEnemies, Damage(1000f));
            PassiveSkillSO onKill = Passive("onkill", PassiveTrigger.EnemyDefeated, PassiveTarget.AllAllies, Buff(StatType.Attack, 1));
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit weak = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(1, 1, 0, 0, 0, 10), new HexCoordinate(1, 0));
            BattleUnit tough = new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(1000000, 1, 1000, 0, 0, 10), new HexCoordinate(2, 0));
            List<BattleUnit> roster = new List<BattleUnit> { player, weak, tough };
            PassiveLoadout passives = Loadout(opener, onKill);
            BattleUnit avatar = MakeAvatar(new StatBlock(1, 500, 0, 0, 0, 0));

            IReadOnlyList<PassiveActivation> opening = BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives);

            Assert.IsTrue(weak.IsDefeated, "the opener's damage uses the avatar's Attack");
            Assert.AreEqual(1, opening.Count);
            Assert.AreEqual(0, turn.PassiveActivations.Count);
            Assert.AreEqual(100, player.Stats.Attack);
        }

        [Test]
        public void AllyCrit_FiresOncePerCriticalHit_OnlyForTheBeastsOwnHits()
        {
            PassiveSkillSO onCrit = Passive("oncrit", PassiveTrigger.AllyCrit, PassiveTarget.TriggeringUnit, Buff(StatType.Defense, 1));
            SkillEffect twoHits = Damage(10f);
            twoHits.HitCount = 2;
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 0, twoHits);
            BattleUnit critter = new BattleUnit("p1", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10, 0, 100), HexCoordinate.Zero,
                                                new SkillLoadout(new[] { strike }));
            BattleUnit plain = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10, 0, 0), new HexCoordinate(0, 1),
                                              new SkillLoadout(new[] { strike }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000000, 1, 1000, 0, 0, 10, 0, 100), new HexCoordinate(2, 0),
                                              new SkillLoadout(new[] { strike }));
            SkillSO avatarStrike = Skill("avatar-strike", SkillTargetShape.AllEnemies, 0, Damage(10f));
            BattleUnit avatar = BattleAvatar.Create(new SkillLoadout(new[] { avatarStrike }), new StatBlock(1, 100, 0, 0, 0, 0, 0, 100), null);
            List<BattleUnit> roster = new List<BattleUnit> { critter, plain, enemy };
            PassiveLoadout passives = Loadout(onCrit);
            System.Random rng = new System.Random(11);
            BattleTurnExecutor.BeginBattle(roster, null, rng, avatar, passives);

            BattleTurnResult critterTurn = BattleTurnExecutor.ExecuteTurn(critter, roster, null, rng, avatar, passives);
            Assert.AreEqual(0, critterTurn.AvatarActivations.Count, "the avatar casts on its own turns, not a beast's");
            Assert.AreEqual(2, critterTurn.PassiveActivations.Count, "two crit hits from the beast");
            Assert.AreEqual(102, critter.Stats.Defense);

            BattleTurnResult avatarTurn = BattleTurnExecutor.ExecuteAvatarTurn(avatar, roster, null, rng, passives);
            Assert.AreEqual(1, avatarTurn.AvatarActivations.Count, "the avatar's crit strike landed");
            Assert.IsTrue(avatarTurn.AvatarActivations[0].Hits[0].Roll.IsCrit);
            Assert.AreEqual(0, avatarTurn.PassiveActivations.Count, "the avatar's crit is not an ally crit");
            Assert.AreEqual(102, critter.Stats.Defense);

            BattleTurnResult plainTurn = BattleTurnExecutor.ExecuteTurn(plain, roster, null, rng, avatar, passives);
            Assert.AreEqual(0, plainTurn.PassiveActivations.Count, "no crits, no trigger");

            BattleTurnResult enemyTurn = BattleTurnExecutor.ExecuteTurn(enemy, roster, null, rng, avatar, passives);
            Assert.AreEqual(0, enemyTurn.PassiveActivations.Count, "an enemy's crit is not an ally crit");
        }

        // ---------------------------------------------------------------------------------------
        // Turn start and HP threshold.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AllyTurnStart_FiresOnPlayerBeastTurns_NotEnemyTurns_BeforeItsSkills()
        {
            PassiveSkillSO rally = Passive("rally", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 10));
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 0, Damage(10f));
            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { strike }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000000, 100, 100, 0, 0, 10), new HexCoordinate(1, 0));
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            PassiveLoadout passives = Loadout(rally);
            BattleUnit avatar = MakeAvatar();
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, avatar, passives).PassiveActivations.Count);

            BattleUnit reference = new BattleUnit("r", BattleTeam.Player, new StatBlock(1000, 110, 100, 0, 0, 10), HexCoordinate.Zero);
            int expected = DamageFormula.Compute(reference, enemy, strike, 10f);
            int before = enemy.CurrentHp;
            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives);

            Assert.AreEqual(1, turn.PassiveActivations.Count);
            Assert.AreSame(player, turn.PassiveActivations[0].TriggeringUnit);
            Assert.AreEqual(expected, before - enemy.CurrentHp, "the turn-start buff is in place before the beast's skill lands");
        }

        [Test]
        public void AllyBelowHpPercent_FiresOncePerCrossing_StrictlyBelow_AndRearmsWhenBackAbove()
        {
            PassiveSkillSO rescue = Passive("rescue", PassiveTrigger.AllyBelowHpPercent, PassiveTarget.TriggeringUnit, Buff(StatType.Defense, 1));
            rescue.HpThresholdPercent = 50;
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit other = Beast("p2", BattleTeam.Player);
            BattleUnit enemy = Beast("e", BattleTeam.Enemy);
            List<BattleUnit> roster = new List<BattleUnit> { player, other, enemy };
            PassiveLoadout passives = Loadout(rescue);
            BattleUnit avatar = MakeAvatar();
            BattleTurnExecutor.BeginBattle(roster, null, null, avatar, passives);

            player.CurrentHp = 500;
            Assert.AreEqual(0, EnemyTurnFirings(enemy, roster, avatar, passives), "exactly 50% is not below 50%");

            player.CurrentHp = 499;
            Assert.AreEqual(1, EnemyTurnFirings(enemy, roster, avatar, passives), "the crossing");

            player.CurrentHp = 100;
            Assert.AreEqual(0, EnemyTurnFirings(enemy, roster, avatar, passives), "still below: the same crossing");

            player.CurrentHp = 600;
            Assert.AreEqual(0, EnemyTurnFirings(enemy, roster, avatar, passives), "back above re-arms, fires nothing");

            player.CurrentHp = 400;
            other.CurrentHp = 300;
            Assert.AreEqual(2, EnemyTurnFirings(enemy, roster, avatar, passives), "a new crossing, and another beast's own");
            Assert.AreEqual(102, player.Stats.Defense);
            Assert.AreEqual(101, other.Stats.Defense);
        }

        // ---------------------------------------------------------------------------------------
        // Gating: proc chance, per-battle cap, internal cooldown.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ProcChance_DrawsOncePerAttempt_FromTheBattleRng()
        {
            PassiveSkillSO coin = Passive("coin", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Heal(1f));
            coin.ProcChance = 50;
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit enemy = Beast("e", BattleTeam.Enemy);
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            PassiveLoadout passives = Loadout(coin);
            BattleUnit avatar = MakeAvatar();
            System.Random rng = new System.Random(2024);
            System.Random reference = new System.Random(2024);
            BattleTurnExecutor.BeginBattle(roster, null, rng, avatar, passives);

            int fired = 0;
            int expectedFired = 0;

            for (int i = 0; i < 40; i++)
            {
                bool expected = reference.Next(100) < 50;
                int got = BattleTurnExecutor.ExecuteTurn(player, roster, null, rng, avatar, passives).PassiveActivations.Count;

                Assert.AreEqual(expected ? 1 : 0, got, "attempt " + i);
                fired += got;
                expectedFired += expected ? 1 : 0;
            }

            Assert.Greater(fired, 0);
            Assert.Less(fired, 40);
            Assert.AreEqual(reference.Next(), rng.Next(), "exactly one draw per attempt and nothing else");
        }

        [Test]
        public void ProcChance_Certain_DrawsNothing_AndUnsetReadsAsCertain()
        {
            PassiveSkillSO certain = Passive("certain", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Heal(1f));
            certain.ProcChance = 0;
            BattleUnit player = Beast("p", BattleTeam.Player);
            List<BattleUnit> roster = new List<BattleUnit> { player, Beast("e", BattleTeam.Enemy) };
            PassiveLoadout passives = Loadout(certain);
            System.Random rng = new System.Random(9);
            System.Random reference = new System.Random(9);

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(1, BattleTurnExecutor.ExecuteTurn(player, roster, null, rng, MakeAvatar(), passives).PassiveActivations.Count);
            }

            Assert.AreEqual(100, passives.Passives[0].ProcChance);
            Assert.AreEqual(reference.Next(), rng.Next());
        }

        [Test]
        public void MaxTriggersPerBattle_CapsFirings_ThenThePassiveIsSpent()
        {
            PassiveSkillSO limited = Passive("limited", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 1));
            limited.MaxTriggersPerBattle = 2;
            BattleUnit player = Beast("p", BattleTeam.Player);
            List<BattleUnit> roster = new List<BattleUnit> { player, Beast("e", BattleTeam.Enemy) };
            PassiveLoadout passives = Loadout(limited);
            BattleUnit avatar = MakeAvatar();

            int fired = 0;
            for (int i = 0; i < 5; i++)
            {
                fired += BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives).PassiveActivations.Count;
            }

            Assert.AreEqual(2, fired);
            Assert.IsTrue(passives.Passives[0].IsSpent);
            Assert.AreEqual(102, player.Stats.Attack);
        }

        [Test]
        public void InternalCooldown_CountsAvatarTurns_NotBeastTurns()
        {
            PassiveSkillSO slow = Passive("slow", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 1));
            slow.InternalCooldown = 3;
            BattleUnit player = Beast("p", BattleTeam.Player);
            BattleUnit enemy = Beast("e", BattleTeam.Enemy);
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            PassiveLoadout passives = Loadout(slow);
            BattleUnit avatar = MakeAvatar();

            List<int> pattern = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                // Two player-beast turns and an enemy turn per avatar turn: only the avatar's own
                // turns count the cooldown down, however many beast turns fall between them.
                pattern.Add(BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives).PassiveActivations.Count);
                BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, avatar, passives);
                pattern.Add(BattleTurnExecutor.ExecuteTurn(player, roster, null, null, avatar, passives).PassiveActivations.Count);
                Assert.AreEqual(0, BattleTurnExecutor.ExecuteAvatarTurn(avatar, roster, null, null, passives).PassiveActivations.Count,
                                "an AllyTurnStart passive never fires on the avatar's own turn");
            }

            // Fires, then three avatar turns before it is ready: six beast turns later, not three.
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 0, 0, 0, 1, 0 }, pattern);
            Assert.AreEqual(102, player.Stats.Attack);
        }

        [Test]
        public void TriggersSharedByTwoPassives_AreTriedInSlotOrder()
        {
            PassiveSkillSO first = Passive("first", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 1));
            PassiveSkillSO second = Passive("second", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Buff(StatType.Attack, 1));
            BattleUnit player = Beast("p", BattleTeam.Player);
            List<BattleUnit> roster = new List<BattleUnit> { player, Beast("e", BattleTeam.Enemy) };

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(player, roster, null, null, MakeAvatar(), Loadout(second, first));

            Assert.AreEqual(2, turn.PassiveActivations.Count);
            Assert.AreEqual("second", turn.PassiveActivations[0].Passive.PassiveId);
            Assert.AreEqual(0, turn.PassiveActivations[0].SlotIndex);
            Assert.AreEqual("first", turn.PassiveActivations[1].Passive.PassiveId);
        }

        // ---------------------------------------------------------------------------------------
        // Target scopes.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Scopes_AllAllies_AllEnemies_LowestHpFractionAlly()
        {
            PassiveSkillSO allies = Passive("allies", PassiveTrigger.BattleStart, PassiveTarget.AllAllies, Buff(StatType.Speed, 1));
            PassiveSkillSO enemies = Passive("enemies", PassiveTrigger.BattleStart, PassiveTarget.AllEnemies, Debuff(StatType.Attack, 5));
            PassiveSkillSO lowest = Passive("lowest", PassiveTrigger.BattleStart, PassiveTarget.LowestHpFractionAlly, Heal(1f));
            BattleUnit small = new BattleUnit("p1", BattleTeam.Player, new StatBlock(100, 50, 50, 0, 0, 10), HexCoordinate.Zero);
            BattleUnit big = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 50, 50, 0, 0, 10), new HexCoordinate(0, 1));
            BattleUnit e1 = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(100, 50, 50, 0, 0, 10), new HexCoordinate(2, 0));
            BattleUnit e2 = new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(100, 50, 50, 0, 0, 10), new HexCoordinate(3, 0));
            small.CurrentHp = 60;
            big.CurrentHp = 500;
            List<BattleUnit> roster = new List<BattleUnit> { e2, big, e1, small };

            IReadOnlyList<PassiveActivation> fired = BattleTurnExecutor.BeginBattle(roster, null, null, MakeAvatar(HealerStats), Loadout(allies, enemies, lowest));

            Assert.AreEqual(3, fired.Count);
            CollectionAssert.AreEqual(new[] { small, big }, fired[0].Activation.Targets, "living allies in id order");
            CollectionAssert.AreEqual(new[] { e1, e2 }, fired[1].Activation.Targets);
            Assert.AreEqual(11, small.Stats.Speed);
            Assert.AreEqual(10, e1.Stats.Speed);
            Assert.AreEqual(45, e2.Stats.Attack);
            Assert.AreEqual(50, big.Stats.Attack);
            CollectionAssert.AreEqual(new[] { big }, fired[2].Activation.Targets, "50% is a lower fraction than 60%, though 500 HP is more");
            Assert.AreEqual(501, big.CurrentHp);
            Assert.AreEqual(60, small.CurrentHp);
        }

        // ---------------------------------------------------------------------------------------
        // Level and tier.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PassiveLevel_ScalesEveryMagnitude_WithTheSharedMultiplier()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 10), Heal(10f));
            BattleUnit player = Beast("p", BattleTeam.Player);
            player.CurrentHp = 100;
            List<BattleUnit> roster = new List<BattleUnit> { player, Beast("e", BattleTeam.Enemy) };
            PassiveInstance leveled = new PassiveInstance(aura, 11);

            // Heals are a percent of the caster's SpecialAttack; the avatar is the caster.
            BattleTurnExecutor.BeginBattle(roster, null, null, MakeAvatar(HealerStats), new PassiveLoadout(new[] { leveled }));

            Assert.AreEqual(11, leveled.Level);
            Assert.AreEqual(1.3, leveled.Skill.MagnitudeMultiplier, 1e-9, "3% per level above 1 by default");
            Assert.AreEqual(113, player.Stats.Attack);
            Assert.AreEqual(113, player.CurrentHp);
        }

        [Test]
        public void PassiveTier_AppendsEachPassedGatesBonusEffects()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 10));
            aura.Progression = new SkillProgressionDefinition
            {
                Tiers = new List<SkillTierDefinition>
                {
                    new SkillTierDefinition { ThresholdLevel = 5, RequiredMaterialTier = 1, BonusEffects = new List<SkillEffect> { Buff(StatType.Defense, 7) } },
                },
            };
            BattleUnit tierZero = Beast("p1", BattleTeam.Player);
            BattleUnit tierOne = Beast("p2", BattleTeam.Player);

            BattleTurnExecutor.BeginBattle(new List<BattleUnit> { tierZero }, null, null, MakeAvatar(), new PassiveLoadout(new[] { new PassiveInstance(aura, 1, 0) }));
            BattleTurnExecutor.BeginBattle(new List<BattleUnit> { tierOne }, null, null, MakeAvatar(), new PassiveLoadout(new[] { new PassiveInstance(aura, 1, 1) }));

            Assert.AreEqual(110, tierZero.Stats.Attack);
            Assert.AreEqual(100, tierZero.Stats.Defense);
            Assert.AreEqual(110, tierOne.Stats.Attack);
            Assert.AreEqual(107, tierOne.Stats.Defense);
        }

        [Test]
        public void InstancesOfOnePassive_ShareOneCarrier_ButKeepTheirOwnLevelAndBattleState()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 10));
            PassiveSkillSO other = Passive("other", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 10));
            PassiveInstance first = new PassiveInstance(aura, 1);
            PassiveInstance second = new PassiveInstance(aura, 11);
            PassiveInstance third = new PassiveInstance(other);

            Assert.AreSame(first.Skill.Skill, second.Skill.Skill, "one carrier per passive asset");
            Assert.AreNotSame(first.Skill.Skill, third.Skill.Skill);
            Assert.AreSame(aura.Effects, first.Skill.Skill.Effects);

            // Level lives on the wrapper, battle state on the instance: neither leaks across.
            Assert.AreEqual(1.0, first.Skill.MagnitudeMultiplier, 1e-9);
            Assert.AreEqual(1.3, second.Skill.MagnitudeMultiplier, 1e-9);
            BattleUnit p1 = Beast("p1", BattleTeam.Player);
            BattleUnit p2 = Beast("p2", BattleTeam.Player);
            BattleTurnExecutor.BeginBattle(new List<BattleUnit> { p1 }, null, null, MakeAvatar(), new PassiveLoadout(new[] { first }));
            BattleTurnExecutor.BeginBattle(new List<BattleUnit> { p2 }, null, null, MakeAvatar(), new PassiveLoadout(new[] { second }));

            Assert.AreEqual(110, p1.Stats.Attack);
            Assert.AreEqual(113, p2.Stats.Attack);
            Assert.AreEqual(1, first.TriggerCount);
            Assert.AreEqual(1, second.TriggerCount);
            Assert.AreEqual(0, new PassiveInstance(aura).TriggerCount, "a new battle's instance starts fresh on the shared carrier");
        }

        [Test]
        public void SeededBattleWithPassives_ReplaysIdentically_WhenCarriersAreReused()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.CritChance, 30));
            PassiveSkillSO coin = Passive("coin", PassiveTrigger.AllyCrit, PassiveTarget.AllEnemies, Damage(15f));
            coin.ProcChance = 50;

            string first = ReusedPassiveTrace(aura, coin, 91);
            string second = ReusedPassiveTrace(aura, coin, 91);

            Assert.AreEqual(first, second);
            StringAssert.Contains("coin", first, "the trace exercised a chance passive");
        }

        [Test]
        public void PassiveSkill_ProgressesOnTheSharedRules()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 10));
            SkillMaterialSO material = new SkillMaterialSO();
            material.Tier = 1;
            material.XpValue = 100000;
            _created.Add(material);
            AvatarSkillBook book = new AvatarSkillBook();
            book.Passives.Learn(aura);
            SkillProgress progress = book.Passives.GetProgress("aura");

            SkillProgression.ApplyMaterial(progress, aura.Progression, material);
            Assert.AreEqual(5, progress.Level, "held at the first gate");
            Assert.AreEqual(SkillBreakthroughResult.Success, SkillProgression.TryBreakthrough(progress, aura.Progression, material));

            PassiveInstance instance = PassiveInstance.FromProgress(aura, progress);
            Assert.AreEqual(progress.Level, instance.Level);
            Assert.AreEqual(1, instance.Tier);
        }

        // ---------------------------------------------------------------------------------------
        // The avatar's skill book.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PassiveBook_HasThreeSlots_AndTheSharedEquipRules()
        {
            AvatarSkillBook book = new AvatarSkillBook();

            Assert.AreEqual(3, AvatarSkillBook.PassiveSlotCount);
            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, book.Passives.SlotCount);
            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, book.Passives.Equipped.Length);
            Assert.AreEqual(AvatarSkillBook.ActiveSlotCount, book.Actives.Equipped.Length);

            Assert.AreEqual(SkillEquipResult.UnknownSkill, book.Passives.Equip(0, "a"));
            Assert.IsTrue(book.Passives.Learn("a"));
            Assert.IsFalse(book.Passives.Learn("a"), "re-learning never resets");
            book.Passives.Learn("b");
            book.Passives.Learn("c");
            book.Passives.Learn("d");

            Assert.AreEqual(SkillEquipResult.SlotOutOfRange, book.Passives.Equip(3, "a"));
            Assert.AreEqual(SkillEquipResult.SlotOutOfRange, book.Passives.Equip(-1, "a"));
            Assert.AreEqual(SkillEquipResult.Equipped, book.Passives.Equip(0, "a"));
            Assert.AreEqual(SkillEquipResult.Equipped, book.Passives.Equip(2, "c"));
            Assert.AreEqual(SkillEquipResult.AlreadyEquipped, book.Passives.Equip(1, "a"), "no duplicates");
            Assert.AreEqual(SkillEquipResult.Equipped, book.Passives.Equip(0, "a"), "same slot is a no-op success");
            Assert.IsNull(book.Passives.GetEquipped(1), "empty allowed");
            Assert.IsTrue(book.Passives.SwapSlots(0, 2));
            Assert.AreEqual("c", book.Passives.GetEquipped(0));
            Assert.IsTrue(book.Passives.Unequip(0));
            Assert.IsFalse(book.Passives.Unequip(0));
            Assert.IsFalse(book.Actives.Knows("a"), "the two books are separate id spaces");
        }

        [Test]
        public void CreateFromBook_BuildsLeveledActivesAndPassives_InSlotOrder()
        {
            SkillSO cheer = Skill("cheer", SkillTargetShape.AllAllies, 2, Buff(StatType.Attack, 1));
            PassiveSkillSO first = Passive("first", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 1));
            PassiveSkillSO second = Passive("second", PassiveTrigger.AllyTurnStart, PassiveTarget.AllAllies, Buff(StatType.Attack, 1));
            AvatarSkillBook book = new AvatarSkillBook();
            book.Actives.Learn(cheer);
            book.Actives.Equip(1, "cheer");
            book.Actives.GetProgress("cheer").Level = 4;
            book.Passives.Learn(first);
            book.Passives.Learn(second);
            book.Passives.Learn("unresolvable");
            book.Passives.Equip(0, "second");
            book.Passives.Equip(1, "unresolvable");
            book.Passives.Equip(2, "first");
            book.Passives.GetProgress("first").Level = 7;
            Dictionary<string, PassiveSkillSO> passiveAssets = new Dictionary<string, PassiveSkillSO> { { "first", first }, { "second", second } };

            PassiveLoadout passives;
            BattleUnit avatar = BattleAvatar.Create(book, id => id == "cheer" ? cheer : null, id => passiveAssets.TryGetValue(id, out PassiveSkillSO p) ? p : null,
                                                    new StatBlock(10, 20, 30, 0, 0, 0), null, 3, out passives);

            Assert.AreEqual(BattleTeam.Player, avatar.Team);
            Assert.AreEqual(3, avatar.Level);
            Assert.AreEqual(20, avatar.Stats.Attack);
            Assert.AreEqual(1, avatar.Skills.Count);
            Assert.AreEqual(4, avatar.Skills.GetInstance(0).Level);
            Assert.AreEqual(2, passives.Count, "the unresolvable slot closes up");
            Assert.AreSame(second, passives.Passives[0].Passive);
            Assert.AreSame(first, passives.Passives[1].Passive);
            Assert.AreEqual(7, passives.Passives[1].Level);

            PassiveLoadout none;
            BattleUnit bare = BattleAvatar.Create(null, null, null, default(StatBlock), null, 1, out none);
            Assert.IsNotNull(none);
            Assert.AreEqual(0, none.Count);
            Assert.AreEqual(0, bare.Skills.Count);
        }

        [Test]
        public void UsageCounts_FeedTheAvatarBooksPractice()
        {
            SkillSO cheer = Skill("cheer", SkillTargetShape.AllAllies, 0);
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 0, Damage(20f));
            PassiveSkillSO rally = Passive("rally", PassiveTrigger.AllyTurnStart, PassiveTarget.TriggeringUnit, Heal(1f));
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 1));
            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 100, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { strike }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(400, 10, 100, 0, 0, 10), new HexCoordinate(1, 0));
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };
            BattleUnit avatar = BattleAvatar.Create(new SkillLoadout(new[] { cheer }));

            // The avatar is in the turn order (its own gauge), never in the targeting roster.
            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(new List<BattleUnit>(roster) { avatar }), roster, null, new System.Random(4), avatar,
                                                               Loadout(rally, aura));
            Assert.AreEqual(BattleOutcome.PlayerVictory, result.Outcome);

            int playerTurns = 0;
            int avatarTurns = 0;
            int avatarCasts = 0;
            foreach (BattleTurnResult turn in result.Turns)
            {
                playerTurns += turn.Unit == player ? 1 : 0;
                avatarTurns += turn.Unit == avatar ? 1 : 0;
                avatarCasts += turn.AvatarActivations.Count;
                Assert.IsTrue(turn.Unit == avatar || turn.AvatarActivations.Count == 0, "only the avatar's own turns cast");
            }

            Assert.Greater(avatarTurns, 0);
            Assert.AreEqual(avatarTurns, avatarCasts, "a cooldown-0 active fires on every avatar turn");

            Dictionary<string, int> passiveCounts = BattleSkillUsage.CountPassiveTriggers(result);
            Dictionary<string, int> activeCounts = BattleSkillUsage.CountAvatarActiveUses(result);

            Assert.Greater(playerTurns, 1);
            Assert.AreEqual(playerTurns, passiveCounts["rally"]);
            Assert.AreEqual(1, passiveCounts["aura"], "the opening firing counts");
            Assert.AreEqual(avatarCasts, activeCounts["cheer"]);
            Assert.AreEqual(0, BattleSkillUsage.CountPassiveTriggers(null).Count);

            AvatarSkillBook book = new AvatarSkillBook();
            book.Actives.Learn(cheer);
            book.Passives.Learn(rally);
            book.AwardPractice(activeCounts, id => id == "cheer" ? cheer : null, passiveCounts, id => id == "rally" ? rally : null);

            SkillProgress rallyProgress = book.Passives.GetProgress("rally");
            SkillProgress cheerProgress = book.Actives.GetProgress("cheer");
            Assert.AreEqual(System.Math.Min(playerTurns, SkillProgression.PracticeUseCapPerAward) * SkillProgression.PracticeXpPerUse,
                            SkillProgression.TotalXpToReach(rallyProgress.Level) + rallyProgress.Xp);
            Assert.AreEqual(System.Math.Min(avatarCasts, SkillProgression.PracticeUseCapPerAward) * SkillProgression.PracticeXpPerUse,
                            SkillProgression.TotalXpToReach(cheerProgress.Level) + cheerProgress.Xp);
            Assert.IsNull(book.Passives.GetProgress("aura"), "unknown ids are ignored");
        }

        // ---------------------------------------------------------------------------------------
        // Determinism and the no-passive path.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SeededBattleWithPassives_ReplaysIdentically()
        {
            string first = PassiveBattleTrace(77);
            string second = PassiveBattleTrace(77);

            Assert.AreEqual(first, second);
            StringAssert.Contains("coin", first, "the trace exercised a chance passive");
        }

        [Test]
        public void NoPassives_IsExactlyTheOriginalBattle()
        {
            string legacy = PlainBattleTrace(31, 0);
            string nullLoadout = PlainBattleTrace(31, 1);
            string emptyLoadout = PlainBattleTrace(31, 2);
            string noAvatar = PlainBattleTrace(31, 3);

            Assert.AreEqual(legacy, nullLoadout);
            Assert.AreEqual(legacy, emptyLoadout);
            StringAssert.DoesNotContain("passive", legacy);
            Assert.AreNotEqual(legacy, noAvatar, "sanity: the avatar's actives matter to this battle");
        }

        [Test]
        public void PassivesWithoutAnAvatar_DoNothing()
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.Attack, 5));
            BattleUnit player = Beast("p", BattleTeam.Player);
            List<BattleUnit> roster = new List<BattleUnit> { player, Beast("e", BattleTeam.Enemy) };
            PassiveLoadout passives = Loadout(aura);

            Assert.AreEqual(0, BattleTurnExecutor.BeginBattle(roster, null, null, null, passives).Count);
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(player, roster, null, null, null, passives).PassiveActivations.Count);
            Assert.AreEqual(100, player.Stats.Attack);
            Assert.IsFalse(passives.HasBegun);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        private static int EnemyTurnFirings(BattleUnit enemy, List<BattleUnit> roster, BattleUnit avatar, PassiveLoadout passives)
        {
            return BattleTurnExecutor.ExecuteTurn(enemy, roster, null, null, avatar, passives).PassiveActivations.Count;
        }

        /// <summary>A full seeded battle with every kind of passive, rendered as text: each firing and the final HP.</summary>
        private string PassiveBattleTrace(int seed)
        {
            PassiveSkillSO aura = Passive("aura", PassiveTrigger.Aura, PassiveTarget.AllAllies, Buff(StatType.CritChance, 30));
            PassiveSkillSO coin = Passive("coin", PassiveTrigger.AllyCrit, PassiveTarget.AllEnemies, Damage(15f));
            coin.ProcChance = 50;
            PassiveSkillSO rescue = Passive("rescue", PassiveTrigger.AllyBelowHpPercent, PassiveTarget.LowestHpFractionAlly, Heal(40f));
            rescue.InternalCooldown = 2;

            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 1, Damage(40f));
            SkillSO claw = Skill("claw", SkillTargetShape.AllEnemies, 1, Damage(45f));
            List<BattleUnit> roster = new List<BattleUnit>
            {
                new BattleUnit("p1", BattleTeam.Player, new StatBlock(400, 60, 40, 0, 0, 12), HexCoordinate.Zero, new SkillLoadout(new[] { strike })),
                new BattleUnit("p2", BattleTeam.Player, new StatBlock(300, 70, 30, 0, 0, 9), new HexCoordinate(0, 1), new SkillLoadout(new[] { strike })),
                new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(500, 60, 40, 0, 0, 11), new HexCoordinate(3, 0), new SkillLoadout(new[] { claw })),
                new BattleUnit("e2", BattleTeam.Enemy, new StatBlock(500, 55, 45, 0, 0, 10), new HexCoordinate(3, 1), new SkillLoadout(new[] { claw })),
            };
            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(1, 50, 20, 0, 0, 0), null);

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(seed), avatar,
                                                               Loadout(aura, coin, rescue));

            System.Text.StringBuilder trace = new System.Text.StringBuilder();
            trace.Append(result.Outcome).Append(' ').Append(result.ElapsedTicks).Append('\n');
            AppendPassives(trace, result.OpeningPassiveActivations);
            foreach (BattleTurnResult turn in result.Turns)
            {
                trace.Append(turn.Unit.Id).Append(':');
                AppendPassives(trace, turn.PassiveActivations);
            }

            foreach (BattleUnit unit in roster)
            {
                trace.Append(unit.Id).Append('=').Append(unit.CurrentHp).Append(' ');
            }

            return trace.ToString();
        }

        /// <summary>A seeded battle built from the given passive assets, so repeated calls reuse their cached carriers.</summary>
        private string ReusedPassiveTrace(PassiveSkillSO aura, PassiveSkillSO coin, int seed)
        {
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 1, Damage(40f));
            List<BattleUnit> roster = new List<BattleUnit>
            {
                new BattleUnit("p1", BattleTeam.Player, new StatBlock(400, 60, 40, 0, 0, 12), HexCoordinate.Zero, new SkillLoadout(new[] { strike })),
                new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(600, 60, 40, 0, 0, 11), new HexCoordinate(3, 0), new SkillLoadout(new[] { strike })),
            };
            BattleUnit avatar = BattleAvatar.Create(null, new StatBlock(1, 50, 20, 0, 0, 0), null);

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(seed), avatar, Loadout(aura, coin));

            System.Text.StringBuilder trace = new System.Text.StringBuilder();
            trace.Append(result.Outcome).Append(' ').Append(result.ElapsedTicks).Append('\n');
            AppendPassives(trace, result.OpeningPassiveActivations);
            foreach (BattleTurnResult turn in result.Turns)
            {
                trace.Append(turn.Unit.Id).Append(':');
                AppendPassives(trace, turn.PassiveActivations);
            }

            foreach (BattleUnit unit in roster)
            {
                trace.Append(unit.Id).Append('=').Append(unit.CurrentHp).Append(' ');
            }

            return trace.ToString();
        }

        private static void AppendPassives(System.Text.StringBuilder trace, IReadOnlyList<PassiveActivation> activations)
        {
            foreach (PassiveActivation activation in activations)
            {
                trace.Append(activation.Passive.PassiveId).Append('@').Append(activation.TriggeringUnit == null ? "-" : activation.TriggeringUnit.Id).Append(' ');
            }

            trace.Append('\n');
        }

        /// <summary>
        /// The same seeded battle through the legacy RunBattle (mode 0), with a null passive loadout
        /// (1), with an empty one (2), or with no avatar at all (3).
        /// </summary>
        private string PlainBattleTrace(int seed, int mode)
        {
            SkillSO strike = Skill("strike", SkillTargetShape.AllEnemies, 1, Damage(40f));
            SkillSO boost = Skill("boost", SkillTargetShape.AllAllies, 2, Buff(StatType.Attack, 5, 2));
            List<BattleUnit> roster = new List<BattleUnit>
            {
                new BattleUnit("p1", BattleTeam.Player, new StatBlock(400, 60, 40, 0, 0, 12, 0, 20), HexCoordinate.Zero, new SkillLoadout(new[] { strike })),
                new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(700, 60, 40, 0, 0, 11, 0, 20), new HexCoordinate(3, 0), new SkillLoadout(new[] { strike })),
            };
            BattleUnit avatar = mode == 3 ? null : BattleAvatar.Create(new SkillLoadout(new[] { boost }));
            TurnManager turns = new TurnManager(avatar == null ? roster : new List<BattleUnit>(roster) { avatar });
            System.Random rng = new System.Random(seed);

            BattleResult result;
            switch (mode)
            {
                case 1:
                    result = BattleTurnExecutor.RunBattle(turns, roster, null, rng, avatar, null);
                    break;
                case 2:
                    result = BattleTurnExecutor.RunBattle(turns, roster, null, rng, avatar, new PassiveLoadout(null));
                    break;
                default:
                    result = BattleTurnExecutor.RunBattle(turns, roster, null, rng, avatar);
                    break;
            }

            System.Text.StringBuilder trace = new System.Text.StringBuilder();
            trace.Append(result.Outcome).Append(' ').Append(result.ElapsedTicks).Append(' ').Append(result.ActionCount).Append('\n');
            foreach (BattleTurnResult turn in result.Turns)
            {
                trace.Append(turn.Unit.Id).Append(' ').Append(turn.AvatarActivations.Count);
                trace.Append(turn.PassiveActivations.Count > 0 ? " passive" : string.Empty).Append('\n');
            }

            foreach (BattleUnit unit in roster)
            {
                trace.Append(unit.Id).Append('=').Append(unit.CurrentHp).Append(' ');
            }

            return trace.ToString();
        }

        private static BattleUnit Beast(string id, BattleTeam team)
        {
            return new BattleUnit(id, team, new StatBlock(1000, 100, 100, 0, 0, 10), team == BattleTeam.Player ? HexCoordinate.Zero : new HexCoordinate(4, 0));
        }

        /// <summary>An avatar stat block with SpecialAttack 100, so a heal of magnitude m restores m HP.</summary>
        private static readonly StatBlock HealerStats = new StatBlock(1, 0, 0, 100, 0, 0);

        private static BattleUnit MakeAvatar(StatBlock stats = default(StatBlock))
        {
            return BattleAvatar.Create(null, stats, null);
        }

        private static PassiveLoadout Loadout(params PassiveSkillSO[] passives)
        {
            List<PassiveInstance> instances = new List<PassiveInstance>();
            foreach (PassiveSkillSO passive in passives)
            {
                instances.Add(new PassiveInstance(passive));
            }

            return new PassiveLoadout(instances);
        }

        private static SkillEffect Damage(float power)
        {
            return new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power };
        }

        private static SkillEffect Heal(float amount)
        {
            return new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = amount };
        }

        private static SkillEffect Buff(StatType stat, int amount, int duration = 0)
        {
            return new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = stat, Magnitude = amount, DurationTurns = duration };
        }

        private static SkillEffect Debuff(StatType stat, int amount)
        {
            return new SkillEffect { EffectType = SkillEffectType.DebuffStat, AffectedStat = stat, Magnitude = amount };
        }

        private PassiveSkillSO Passive(string id, PassiveTrigger trigger, PassiveTarget scope, params SkillEffect[] effects)
        {
            PassiveSkillSO passive = new PassiveSkillSO();
            passive.PassiveId = id;
            passive.Trigger = trigger;
            passive.TargetScope = scope;
            passive.Effects = new List<SkillEffect>(effects);
            _created.Add(passive);
            return passive;
        }

        private SkillSO Skill(string id, SkillTargetShape shape, int cooldown, params SkillEffect[] effects)
        {
            SkillSO skill = new SkillSO();
            skill.SkillId = id;
            skill.TargetShape = shape;
            skill.TargetSide = shape == SkillTargetShape.AllAllies ? SkillTargetSide.Ally : SkillTargetSide.Enemy;
            skill.Cooldown = cooldown;
            skill.Effects = new List<SkillEffect>(effects);
            _created.Add(skill);
            return skill;
        }
    }
}
