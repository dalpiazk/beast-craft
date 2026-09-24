using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Skills;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Team bonds: the resolver's three conditions and its tiers, application at battle start to
    /// the right scope (members, the whole team or the non-members, never enemies), self-applied
    /// shields, scaling (per-count) bonds' stacks, cap and per-stack carriers, ordering before the
    /// avatar's passives, once per battle, determinism, and the bond data (validator negative
    /// cases, builder mapping, and the authored bonds' coverage of the roster).
    /// </summary>
    public class TeamBondTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Resolver.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Stance_CountsMembersOfTheStance_AndReachesTheHighestTier()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 5)), Tier(3, Buff(StatType.Defense, 10)));

            List<ActiveTeamBond> two = TeamBondResolver.Resolve(new[] { wall }, Team(Member("a", CombatStance.Vanguard), Member("b", CombatStance.Ranged),
                                                                                     Member("c", CombatStance.Vanguard)));
            List<ActiveTeamBond> three = TeamBondResolver.Resolve(new[] { wall }, Team(Member("a", CombatStance.Vanguard), Member("b", CombatStance.Vanguard),
                                                                                       Member("c", CombatStance.Vanguard), Member("d", CombatStance.Skirmisher)));

            Assert.AreEqual(1, two.Count);
            Assert.AreEqual(1, two[0].Tier);
            Assert.AreEqual(2, two[0].Count);
            CollectionAssert.AreEqual(new[] { 0, 2 }, two[0].Members);
            Assert.AreEqual(2, three[0].Tier, "three Vanguards reach the second tier");
            Assert.AreSame(wall.Tiers[1], three[0].TierDefinition);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, three[0].Members);
        }

        [Test]
        public void ConditionsUnmet_ActivateNothing()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 5)));
            TeamBondSO storm = ElementBond("storm", TeamBondScope.Members, new[] { Element.Air, Element.Lightning }, Tier(2, Buff(StatType.Speed, 5)));
            TeamBondSO duo = SpeciesBond("duo", new[] { "golem", "tarasque" }, Tier(2, Buff(StatType.Attack, 5)));

            List<ActiveTeamBond> active = TeamBondResolver.Resolve(new[] { wall, storm, duo },
                                                                   Team(Member("golem", CombatStance.Vanguard, Element.Earth),
                                                                        Member("griffin", CombatStance.Skirmisher, Element.Air),
                                                                        Member("kirin", CombatStance.Ranged, Element.Light)));

            Assert.IsEmpty(active);
            Assert.IsEmpty(TeamBondResolver.Resolve(new[] { wall }, Team()));
            Assert.IsEmpty(TeamBondResolver.Resolve(null, Team(Member("a", CombatStance.Vanguard))));
        }

        [Test]
        public void Elements_CountsDistinctElementsCovered_NotBeasts()
        {
            TeamBondSO storm = ElementBond("storm", TeamBondScope.Members, new[] { Element.Air, Element.Lightning }, Tier(2, Buff(StatType.Speed, 5)));

            List<ActiveTeamBond> sameElement = TeamBondResolver.Resolve(new[] { storm }, Team(Member("a", CombatStance.Skirmisher, Element.Air),
                                                                                              Member("b", CombatStance.Vanguard, Element.Air)));
            List<ActiveTeamBond> both = TeamBondResolver.Resolve(new[] { storm }, Team(Member("a", CombatStance.Skirmisher, Element.Air),
                                                                                       Member("x", CombatStance.Vanguard, Element.Earth),
                                                                                       Member("b", CombatStance.Skirmisher, Element.Lightning),
                                                                                       Member("c", CombatStance.Ranged, Element.Air)));
            List<ActiveTeamBond> oneDualBeast = TeamBondResolver.Resolve(new[] { storm }, Team(Member("a", CombatStance.Skirmisher, Element.Air, Element.Lightning)));

            Assert.IsEmpty(sameElement, "two Air beasts cover only one of the pair");
            Assert.AreEqual(1, both.Count);
            Assert.AreEqual(2, both[0].Count);
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, both[0].Members, "every beast carrying a set element is a member");
            Assert.IsEmpty(oneDualBeast, "a bond is between beasts: one dual-element beast is not enough");
        }

        [Test]
        public void Species_CountsDistinctListedSpecies_AndOrderFollowsTheBondList()
        {
            TeamBondSO duo = SpeciesBond("duo", new[] { "golem", "tarasque" }, Tier(2, Buff(StatType.Attack, 5)));
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 5)));

            List<ActiveTeamBond> twoGolems = TeamBondResolver.Resolve(new[] { duo }, Team(Member("golem", CombatStance.Vanguard), Member("golem", CombatStance.Vanguard)));
            List<ActiveTeamBond> pair = TeamBondResolver.Resolve(new[] { duo, null, wall }, Team(Member("tarasque", CombatStance.Vanguard), null,
                                                                                                 Member("golem", CombatStance.Vanguard)));

            Assert.IsEmpty(twoGolems, "the same species twice is one species");
            Assert.AreEqual(2, pair.Count);
            Assert.AreSame(duo, pair[0].Bond, "active bonds keep the given order (nulls skipped)");
            Assert.AreSame(wall, pair[1].Bond);
            CollectionAssert.AreEqual(new[] { 0, 2 }, pair[0].Members);
        }

        [Test]
        public void MembersOf_ReadsStanceElementsAndIdFromTheSpecies()
        {
            CreatureSpeciesSO species = new CreatureSpeciesSO();
            _created.Add(species);
            species.SpeciesId = "griffin";
            species.Stance = CombatStance.Skirmisher;
            species.Elements = new[] { Element.Air };

            List<TeamBondMember> members = TeamBondResolver.MembersOf(new[] { species, null });

            Assert.AreEqual("griffin", members[0].SpeciesId);
            Assert.AreEqual(CombatStance.Skirmisher, members[0].Stance);
            CollectionAssert.AreEqual(new[] { Element.Air }, members[0].Elements);
            Assert.IsNull(members[1].SpeciesId);
            Assert.AreEqual(0, members[1].Elements.Count);
        }

        // ---------------------------------------------------------------------------------------
        // Battle start.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void MembersScope_BuffsOnlyTheMembers_AtBattleStart_ForTheWholeBattle()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 20)));
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Ranged, 1);
            BattleUnit c = Unit("p3", BattleTeam.Player, CombatStance.Vanguard, 2);
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, CombatStance.Vanguard, 4);
            List<BattleUnit> team = new List<BattleUnit> { a, b, c };
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { wall }, MembersOf(team), team);

            BattleTurnExecutor.BeginBattle(Roster(team, enemy), null, new System.Random(1), null, null, bonds, out IReadOnlyList<TeamBondActivation> applied);

            Assert.AreEqual(1, applied.Count);
            Assert.AreSame(wall, applied[0].Bond);
            Assert.AreEqual(1, applied[0].Tier);
            CollectionAssert.AreEqual(new[] { a, c }, applied[0].Recipients);
            Assert.AreEqual(120, a.Stats.Defense);
            Assert.AreEqual(100, b.Stats.Defense, "not a member");
            Assert.AreEqual(120, c.Stats.Defense);
            Assert.AreEqual(100, enemy.Stats.Defense, "enemies never get the player's bonds");
            Assert.AreEqual(0, a.ActiveStatModifiers.Count, "DurationTurns 0 is permanent: it never reverts");
        }

        [Test]
        public void TeamScope_BuffsEveryTeamBeast_ButNoEnemy()
        {
            TeamBondSO twilight = ElementBond("twilight", TeamBondScope.Team, new[] { Element.Light, Element.Dark }, Tier(2, Buff(StatType.SpecialDefense, 7)));
            BattleUnit light = Unit("p1", BattleTeam.Player, CombatStance.Ranged, 0, Element.Light);
            BattleUnit dark = Unit("p2", BattleTeam.Player, CombatStance.Ranged, 1, Element.Dark);
            BattleUnit other = Unit("p3", BattleTeam.Player, CombatStance.Vanguard, 2, Element.Earth);
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, CombatStance.Ranged, 4, Element.Light);
            List<BattleUnit> team = new List<BattleUnit> { light, dark, other };
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { twilight }, MembersOf(team), team);

            BattleTurnExecutor.BeginBattle(Roster(team, enemy), null, null, null, null, bonds, out IReadOnlyList<TeamBondActivation> applied);

            CollectionAssert.AreEqual(new[] { light, dark, other }, applied[0].Recipients);
            Assert.AreEqual(107, other.Stats.SpecialDefense, "Team scope reaches non-members too");
            Assert.AreEqual(100, enemy.Stats.SpecialDefense);
        }

        [Test]
        public void Shield_ScalesWithEachRecipientsOwnDefense()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard,
                                         Tier(2, new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 50, DurationTurns = 3 }));
            BattleUnit sturdy = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit soft = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 40, 100, 100, 10), new HexCoordinate(1, 0), null, null, 1,
                                             CombatStance.Vanguard);
            List<BattleUnit> team = new List<BattleUnit> { sturdy, soft };

            BattleTurnExecutor.BeginBattle(team, null, null, null, null, TeamBondLoadout.For(new[] { wall }, MembersOf(team), team),
                                           out IReadOnlyList<TeamBondActivation> _);

            Assert.AreEqual(50, StatusEffects.ShieldPoints(sturdy), "50% of its own Defense 100");
            Assert.AreEqual(20, StatusEffects.ShieldPoints(soft), "50% of its own Defense 40");
        }

        [Test]
        public void Bonds_ApplyBeforeTheAvatarsAuras_AndOnlyOnce()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Attack, 10)));
            PassiveSkillSO aura = new PassiveSkillSO();
            _created.Add(aura);
            aura.PassiveId = "aura";
            aura.Trigger = PassiveTrigger.Aura;
            aura.TargetScope = PassiveTarget.AllAllies;
            aura.Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.Attack, Magnitude = 10, IsPercent = true } };
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1);
            List<BattleUnit> team = new List<BattleUnit> { a, b };
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { wall }, MembersOf(team), team);
            PassiveLoadout passives = new PassiveLoadout(new[] { new PassiveInstance(aura) });
            BattleUnit avatar = BattleAvatar.Create(null, default(StatBlock), null);

            IReadOnlyList<PassiveActivation> opening = BattleTurnExecutor.BeginBattle(team, null, null, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> applied);

            Assert.AreEqual(1, opening.Count);
            Assert.AreEqual(1, applied.Count);
            Assert.AreEqual(121, a.Stats.Attack, "bond first (100 + 10), then the aura's 10% of 110");
            Assert.IsTrue(bonds.HasApplied);

            BattleTurnExecutor.BeginBattle(team, null, null, avatar, passives, bonds, out IReadOnlyList<TeamBondActivation> again);
            Assert.AreEqual(0, again.Count, "a bond loadout applies once");
            Assert.AreEqual(121, a.Stats.Attack);
        }

        [Test]
        public void DefeatedMembers_AreSkipped()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 20)));
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1);
            b.IsDefeated = true;
            List<BattleUnit> team = new List<BattleUnit> { a, b };

            BattleTurnExecutor.BeginBattle(team, null, null, null, null, TeamBondLoadout.For(new[] { wall }, MembersOf(team), team),
                                           out IReadOnlyList<TeamBondActivation> applied);

            CollectionAssert.AreEqual(new[] { a }, applied[0].Recipients);
            Assert.AreEqual(100, b.Stats.Defense);
        }

        [Test]
        public void RunBattle_RecordsBondActivations_AndIsDeterministic_AndDrawsNothingForCertainEffects()
        {
            string first = BondedBattleTrace(11, out BattleResult result);
            string second = BondedBattleTrace(11, out BattleResult _);

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, result.BondActivations.Count);
            Assert.AreEqual("wall", result.BondActivations[0].Bond.BondId);

            TeamBondSO wall = StanceBond("wall2", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 20)));
            List<BattleUnit> team = new List<BattleUnit> { Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0), Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1) };
            System.Random used = new System.Random(3);
            BattleTurnExecutor.BeginBattle(team, null, used, null, null, TeamBondLoadout.For(new[] { wall }, MembersOf(team), team), out IReadOnlyList<TeamBondActivation> _);
            Assert.AreEqual(new System.Random(3).Next(), used.Next(), "a Chance-100 bond draws nothing from the battle rng");
        }

        [Test]
        public void NullOrEmptyBondLoadout_IsTheBondFreeBattle()
        {
            string plain = PlainTrace(21, null);
            string empty = PlainTrace(21, new TeamBondLoadout(null, null));

            Assert.AreEqual(plain, empty);
        }

        // ---------------------------------------------------------------------------------------
        // Scaling (per-count) bonds and the Others scope.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void PerCount_StacksFollowTheCount_AndCapAtMaxCount()
        {
            TeamBondSO line = ScalingBond("line", CombatStance.Vanguard, TeamBondScope.Team, 3, PctBuff(StatType.Defense, 4));

            for (int vanguards = 0; vanguards <= 4; vanguards++)
            {
                List<TeamBondMember> team = new List<TeamBondMember>();
                for (int i = 0; i < 4; i++)
                {
                    team.Add(Member("s" + i, i < vanguards ? CombatStance.Vanguard : CombatStance.Ranged));
                }

                List<ActiveTeamBond> active = TeamBondResolver.Resolve(new[] { line }, team);
                if (vanguards == 0)
                {
                    Assert.IsEmpty(active, "MinCount 1: no member, no bond");
                    continue;
                }

                Assert.AreEqual(1, active.Count);
                Assert.AreEqual(1, active[0].Tier, "a scaling bond has one tier");
                Assert.AreEqual(vanguards, active[0].Count);
                Assert.AreEqual(Math.Min(vanguards, 3), active[0].Stacks, "stacks = count capped at MaxCount");
            }
        }

        [Test]
        public void TieredBond_AlwaysHasOneStack()
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard, Tier(2, Buff(StatType.Defense, 5)), Tier(3, Buff(StatType.Defense, 10)));

            List<ActiveTeamBond> active = TeamBondResolver.Resolve(new[] { wall }, Team(Member("a", CombatStance.Vanguard), Member("b", CombatStance.Vanguard),
                                                                                        Member("c", CombatStance.Vanguard)));

            Assert.AreEqual(2, active[0].Tier);
            Assert.AreEqual(1, active[0].Stacks);
            Assert.AreEqual(1, new ActiveTeamBond(wall, 2, 3, new[] { 0, 1, 2 }).Stacks, "the four-argument constructor takes the bond's own stacks");
        }

        [Test]
        public void PerCount_AppliesMagnitudeTimesStacks_AsOneBuff()
        {
            TeamBondSO line = ScalingBond("line", CombatStance.Vanguard, TeamBondScope.Team, 3, PctBuff(StatType.Defense, 4));
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1);
            BattleUnit c = Unit("p3", BattleTeam.Player, CombatStance.Ranged, 2);
            List<BattleUnit> team = new List<BattleUnit> { a, b, c };

            BattleTurnExecutor.BeginBattle(team, null, null, null, null, TeamBondLoadout.For(new[] { line }, MembersOf(team), team),
                                           out IReadOnlyList<TeamBondActivation> applied);

            Assert.AreEqual(2, applied[0].Stacks);
            Assert.AreEqual(108, a.Stats.Defense, "two stacks of 4%: one 8% buff");
            Assert.AreEqual(108, c.Stats.Defense, "Team scope: the non-member too");
        }

        [Test]
        public void OthersScope_ReachesOnlyTheNonMembers_ButNoEnemy()
        {
            TeamBondSO bulwark = ScalingBond("bulwark", CombatStance.Vanguard, TeamBondScope.Others, 3, PctBuff(StatType.Defense, 4), PctBuff(StatType.SpecialDefense, 4));
            BattleUnit v1 = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit r = Unit("p2", BattleTeam.Player, CombatStance.Ranged, 1);
            BattleUnit v2 = Unit("p3", BattleTeam.Player, CombatStance.Vanguard, 2);
            BattleUnit s = Unit("p4", BattleTeam.Player, CombatStance.Skirmisher, 3);
            BattleUnit enemy = Unit("e1", BattleTeam.Enemy, CombatStance.Ranged, 5);
            List<BattleUnit> team = new List<BattleUnit> { v1, r, v2, s };

            BattleTurnExecutor.BeginBattle(Roster(team, enemy), null, null, null, null, TeamBondLoadout.For(new[] { bulwark }, MembersOf(team), team),
                                           out IReadOnlyList<TeamBondActivation> applied);

            CollectionAssert.AreEqual(new[] { r, s }, applied[0].Recipients, "the non-members, in team order");
            Assert.AreEqual(108, r.Stats.Defense, "two Vanguards: two stacks of 4%");
            Assert.AreEqual(108, s.Stats.SpecialDefense);
            Assert.AreEqual(100, v1.Stats.Defense, "members lend it, they do not get it");
            Assert.AreEqual(100, v2.Stats.Defense);
            Assert.AreEqual(100, enemy.Stats.Defense, "enemies never get the player's bonds");
        }

        [Test]
        public void OthersScope_OnATeamOfOnlyMembers_AppliesNothing()
        {
            TeamBondSO bulwark = ScalingBond("bulwark", CombatStance.Vanguard, TeamBondScope.Others, 3, PctBuff(StatType.Defense, 4));
            BattleUnit a = Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0);
            BattleUnit b = Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1);
            List<BattleUnit> team = new List<BattleUnit> { a, b };
            TeamBondLoadout bonds = TeamBondLoadout.For(new[] { bulwark }, MembersOf(team), team);

            BattleTurnExecutor.BeginBattle(team, null, null, null, null, bonds, out IReadOnlyList<TeamBondActivation> applied);

            Assert.AreEqual(1, bonds.Bonds.Count, "the bond resolves (its condition is met)");
            Assert.AreEqual(0, applied.Count, "but no teammate receives it");
            Assert.AreEqual(100, a.Stats.Defense);
        }

        [Test]
        public void ScaledCarrier_NeverChangesTheAuthoredMagnitudes()
        {
            SkillEffect authored = PctBuff(StatType.Defense, 4);
            TeamBondSO line = ScalingBond("line", CombatStance.Vanguard, TeamBondScope.Team, 3, authored);
            List<BattleUnit> team = new List<BattleUnit>
            {
                Unit("p1", BattleTeam.Player, CombatStance.Vanguard, 0), Unit("p2", BattleTeam.Player, CombatStance.Vanguard, 1),
                Unit("p3", BattleTeam.Player, CombatStance.Vanguard, 2),
            };

            for (int battle = 0; battle < 2; battle++)
            {
                foreach (BattleUnit unit in team)
                {
                    unit.Stats = new StatBlock(1000, 100, 100, 100, 100, 10);
                }

                BattleTurnExecutor.BeginBattle(team, null, null, null, null, TeamBondLoadout.For(new[] { line }, MembersOf(team), team),
                                               out IReadOnlyList<TeamBondActivation> _);
                Assert.AreEqual(112, team[0].Stats.Defense, "three stacks of 4%, battle " + battle);
            }

            Assert.AreEqual(4f, authored.Magnitude, "the tier's own effect keeps its per-stack magnitude");
            Assert.AreEqual(1, line.Tiers[0].Effects.Count);
            Assert.AreSame(authored, line.Tiers[0].Effects[0]);
        }

        [Test]
        public void Carriers_AreCachedPerTierAndStackCount()
        {
            TeamBondSO line = ScalingBond("line", CombatStance.Vanguard, TeamBondScope.Team, 3, PctBuff(StatType.Defense, 4), Buff(StatType.CritChance, 2));
            TeamBondTier tier = line.Tiers[0];

            SkillSO one = TeamBondLoadout.CarrierFor(tier, line, 1);
            SkillSO two = TeamBondLoadout.CarrierFor(tier, line, 2);
            SkillSO three = TeamBondLoadout.CarrierFor(tier, line, 3);

            Assert.AreSame(one, TeamBondLoadout.CarrierFor(tier, line, 1));
            Assert.AreSame(two, TeamBondLoadout.CarrierFor(tier, line, 2));
            Assert.AreNotSame(one, two);
            Assert.AreNotSame(two, three);
            Assert.AreSame(tier.Effects, one.Effects, "one stack shares the authored list");
            Assert.AreNotSame(tier.Effects, two.Effects, "more stacks carry clones");
            Assert.AreEqual(8f, two.Effects[0].Magnitude);
            Assert.AreEqual(12f, three.Effects[0].Magnitude);
            Assert.AreEqual(6f, three.Effects[1].Magnitude);
            Assert.IsTrue(three.Effects[0].IsPercent, "every other field is copied");
            Assert.AreEqual(StatType.CritChance, three.Effects[1].AffectedStat);
            Assert.AreEqual(4f, tier.Effects[0].Magnitude);
            Assert.AreEqual(2f, tier.Effects[1].Magnitude);
        }

        [Test]
        public void Library_EveryBeastIsInExactlyTwoBonds_BesideCombinedArms_AndEveryTierReacts()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            List<TeamBondSO> bonds = BuildBonds(library);

            foreach (TeamBondSO bond in bonds)
            {
                Assert.IsFalse(bond.PerCount, bond.BondId + ": the behaviour bonds replaced the scaling ones");
                foreach (TeamBondTier tier in bond.Tiers)
                {
                    Assert.IsTrue(tier.HasReaction, bond.BondId + " tier " + tier.MinCount + " has no reaction");
                }
            }

            foreach (SpeciesData species in roster.Species)
            {
                BeastRosterValidator.TryParseStance(species.Stance, out CombatStance stance);
                List<Element> elements = new List<Element>();
                foreach (string name in species.Elements)
                {
                    elements.Add(SkillLibraryValidator.ParseOr(name, Element.None));
                }

                TeamBondMember member = new TeamBondMember(species.SpeciesId, stance, elements);
                int count = 0;
                foreach (TeamBondSO bond in bonds)
                {
                    List<int> members = new List<int>();
                    TeamBondResolver.Count(bond, new[] { member }, members);
                    count += bond.Condition != TeamBondCondition.DistinctStances && members.Count > 0 ? 1 : 0;
                }

                Assert.AreEqual(2, count, species.SpeciesId + ": one stance bond and one element bond");
            }
        }

        // ---------------------------------------------------------------------------------------
        // Data: validator, builder and the authored bonds.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Library_EveryRosterSpeciesBelongsToABond_AndEveryElementToAnElementBond()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            List<TeamBondSO> bonds = BuildBonds(library);
            Assert.IsNotEmpty(bonds);

            HashSet<Element> covered = new HashSet<Element>();
            foreach (TeamBondSO bond in bonds)
            {
                if (bond.Condition == TeamBondCondition.Elements)
                {
                    covered.UnionWith(bond.Elements);
                }
            }

            foreach (Element element in Enum.GetValues(typeof(Element)))
            {
                Assert.IsTrue(element == Element.None || covered.Contains(element), element + " is in no element bond.");
            }

            foreach (SpeciesData species in roster.Species)
            {
                BeastRosterValidator.TryParseStance(species.Stance, out CombatStance stance);
                List<Element> elements = new List<Element>();
                foreach (string name in species.Elements)
                {
                    elements.Add(SkillLibraryValidator.ParseOr(name, Element.None));
                }

                TeamBondMember member = new TeamBondMember(species.SpeciesId, stance, elements);
                bool belongs = false;
                foreach (TeamBondSO bond in bonds)
                {
                    List<int> members = new List<int>();
                    TeamBondResolver.Count(bond, new[] { member }, members);
                    belongs |= members.Count > 0;
                }

                Assert.IsTrue(belongs, species.SpeciesId + " belongs to no bond.");
            }
        }

        [Test]
        public void Library_BondIdsArePinned_AndResolveForARealLineup()
        {
            // BondIds are stable keys and must never be renamed after ship; this pins them.
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            CollectionAssert.AreEquivalent(new[] { "guardian", "pack_hunters", "crossfire", "wildfire", "storm_front", "bedrock", "winter_grove", "twilight",
                                                   "combined_arms" },
                                           Array.ConvertAll(library.TeamBonds, b => b.BondId));

            List<ActiveTeamBond> active = TeamBondResolver.Resolve(BuildBonds(library),
                                                                   Team(Member("golem", CombatStance.Vanguard, Element.Earth),
                                                                        Member("tarasque", CombatStance.Vanguard, Element.Metal),
                                                                        Member("griffin", CombatStance.Skirmisher, Element.Air),
                                                                        Member("thunderbird", CombatStance.Skirmisher, Element.Lightning)));
            List<string> ids = active.ConvertAll(a => a.Bond.BondId);
            CollectionAssert.AreEqual(new[] { "guardian", "pack_hunters", "bedrock" }, ids, "two stances only: no combined_arms");

            List<ActiveTeamBond> mixed = TeamBondResolver.Resolve(BuildBonds(library),
                                                                  Team(Member("golem", CombatStance.Vanguard, Element.Earth),
                                                                       Member("kirin", CombatStance.Ranged, Element.Light),
                                                                       Member("basilisk", CombatStance.Ranged, Element.Dark),
                                                                       Member("griffin", CombatStance.Skirmisher, Element.Air)));
            CollectionAssert.AreEqual(new[] { "crossfire", "twilight", "combined_arms" }, mixed.ConvertAll(a => a.Bond.BondId));
        }

        [Test]
        public void Builder_MapsEveryBondField()
        {
            TeamBondData data = new TeamBondData
            {
                BondId = "storm",
                DisplayName = "Storm",
                Description = "d",
                Condition = "Elements",
                Elements = new[] { "Air", "Lightning" },
                Scope = "Team",
                Tiers = new[]
                {
                    new TeamBondTierData { MinCount = 2, Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Speed", Magnitude = 5, IsPercent = true } } }
                }
            };
            TeamBondSO bond = new TeamBondSO();
            _created.Add(bond);

            SkillLibraryBuilder.ApplyTeamBond(data, bond);

            Assert.AreEqual("storm", bond.BondId);
            Assert.AreEqual(TeamBondCondition.Elements, bond.Condition);
            Assert.AreEqual(TeamBondScope.Team, bond.Scope);
            CollectionAssert.AreEqual(new[] { Element.Air, Element.Lightning }, bond.Elements);
            Assert.AreEqual(1, bond.Tiers.Count);
            Assert.AreEqual(2, bond.Tiers[0].MinCount);
            Assert.AreEqual(StatType.Speed, bond.Tiers[0].Effects[0].AffectedStat);
            Assert.IsTrue(bond.Tiers[0].Effects[0].IsPercent);
        }

        [Test]
        public void Builder_MapsTheScalingFields_AndClearsMaxCountOnATieredBond()
        {
            TeamBondData data = new TeamBondData
            {
                BondId = "line",
                DisplayName = "Line",
                Description = "d",
                Condition = "Stance",
                Stance = "Vanguard",
                Scope = "Others",
                PerCount = true,
                MaxCount = 3,
                Tiers = new[] { new TeamBondTierData { MinCount = 1, Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Defense", Magnitude = 4, IsPercent = true } } } }
            };
            TeamBondSO bond = new TeamBondSO();
            _created.Add(bond);

            SkillLibraryBuilder.ApplyTeamBond(data, bond);

            Assert.IsTrue(bond.PerCount);
            Assert.AreEqual(3, bond.MaxCount);
            Assert.AreEqual(TeamBondScope.Others, bond.Scope);
            Assert.AreEqual(1, bond.Tiers[0].MinCount);

            data.PerCount = false;
            SkillLibraryBuilder.ApplyTeamBond(data, bond);
            Assert.IsFalse(bond.PerCount);
            Assert.AreEqual(0, bond.MaxCount);
        }

        [TestCase("TwoTiers", "has exactly one tier")]
        [TestCase("MinCountZero", "must be from 1 up to its MaxCount")]
        [TestCase("MinCountAboveMax", "must be from 1 up to its MaxCount")]
        [TestCase("MaxCountZero", "MaxCount 0 must be at least 1")]
        [TestCase("MaxCountAboveSet", "at most the set's size (2)")]
        [TestCase("PercentOverCap", "over the cap of 20% of the stat")]
        [TestCase("CritOverCap", "over the cap of 15 CritChance")]
        [TestCase("MoveOverCap", "over the cap of 1 MoveRange")]
        [TestCase("ShieldOverCap", "over the cap of a shield of 60% of Defense")]
        [TestCase("FlatAttack", "not a flat Attack buff")]
        [TestCase("MaxCountOnTiered", "is only for a PerCount (scaling) bond")]
        public void Validator_RejectsBadScalingBonds(string fault, string fragment)
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData bond = new TeamBondData
            {
                BondId = "test_line",
                DisplayName = "Test",
                Description = "d",
                Condition = "Stance",
                Stance = "Vanguard",
                Scope = "Others",
                PerCount = true,
                MaxCount = 3,
                Tiers = new[] { new TeamBondTierData { MinCount = 1, Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Defense", Magnitude = 4, IsPercent = true } } } }
            };
            EffectData effect = bond.Tiers[0].Effects[0];

            switch (fault)
            {
                case "TwoTiers":
                    bond.Tiers = new[] { bond.Tiers[0], new TeamBondTierData { MinCount = 2, Effects = bond.Tiers[0].Effects } };
                    break;
                case "MinCountZero":
                    bond.Tiers[0].MinCount = 0;
                    break;
                case "MinCountAboveMax":
                    bond.Tiers[0].MinCount = 4;
                    break;
                case "MaxCountZero":
                    bond.MaxCount = 0;
                    break;
                case "MaxCountAboveSet":
                    bond.Condition = "Elements";
                    bond.Stance = null;
                    bond.Elements = new[] { "Air", "Lightning" };
                    break;
                case "PercentOverCap":
                    effect.Magnitude = 7;
                    break;
                case "CritOverCap":
                    effect.AffectedStat = "CritChance";
                    effect.IsPercent = false;
                    effect.Magnitude = 6;
                    break;
                case "MoveOverCap":
                    effect.AffectedStat = "MoveRange";
                    effect.IsPercent = false;
                    effect.Magnitude = 1;
                    break;
                case "ShieldOverCap":
                    effect.EffectType = "ApplyStatus";
                    effect.Status = "Shield";
                    effect.DurationTurns = 3;
                    effect.Magnitude = 25;
                    break;
                case "FlatAttack":
                    effect.AffectedStat = "Attack";
                    effect.IsPercent = false;
                    effect.Magnitude = 2;
                    break;
                case "MaxCountOnTiered":
                    bond.PerCount = false;
                    bond.Tiers[0].MinCount = 2;
                    break;
            }

            library.TeamBonds = new List<TeamBondData>(library.TeamBonds) { bond }.ToArray();
            List<string> errors = SkillLibraryValidator.Validate(library);

            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "Expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        [Test]
        public void Validator_AcceptsAScalingBondAtItsCaps()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData bond = new TeamBondData
            {
                BondId = "test_line",
                DisplayName = "Test",
                Description = "d",
                Condition = "Stance",
                Stance = "Ranged",
                Scope = "Team",
                PerCount = true,
                MaxCount = 3,
                Tiers = new[]
                {
                    new TeamBondTierData
                    {
                        MinCount = 1,
                        Effects = new[]
                        {
                            new EffectData { EffectType = "BuffStat", AffectedStat = "Speed", Magnitude = 6, IsPercent = true },
                            new EffectData { EffectType = "BuffStat", AffectedStat = "CritChance", Magnitude = 5 },
                            new EffectData { EffectType = "ApplyStatus", Status = "Shield", Magnitude = 20, DurationTurns = 3 },
                        }
                    }
                }
            };
            library.TeamBonds = new List<TeamBondData>(library.TeamBonds) { bond }.ToArray();

            Assert.IsEmpty(SkillLibraryValidator.Validate(library, BeastRosterTests.LoadRoster()));
        }

        [TestCase("Condition", "Friendship", "is not a TeamBondCondition name")]
        [TestCase("Scope", "Everyone", "is not a TeamBondScope name")]
        [TestCase("OneElement", null, "needs at least two entries")]
        [TestCase("DuplicateElement", null, "lists 'Air' twice")]
        [TestCase("NoneElement", null, "None is not allowed")]
        [TestCase("TierTooLow", null, "must rise strictly")]
        [TestCase("TierAboveSet", null, "up to the set's size (2)")]
        [TestCase("TiersNotRising", null, "must rise strictly")]
        [TestCase("NoTiers", null, "has no Tiers")]
        [TestCase("DamageEffect", null, "only BuffStat or a Shield status")]
        [TestCase("DebuffEffect", null, "only BuffStat or a Shield status")]
        [TestCase("StunEffect", null, "only BuffStat or a Shield status")]
        [TestCase("LowChance", null, "Chance must be 100")]
        [TestCase("HpBuff", null, "an HP buff raises only the maximum")]
        [TestCase("DuplicateId", null, "duplicate id")]
        [TestCase("StanceWithElements", null, "a Stance bond lists no Elements or Species")]
        public void Validator_RejectsBadBonds(string fault, string value, string fragment)
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData bond = new TeamBondData
            {
                BondId = "test_bond",
                DisplayName = "Test",
                Description = "d",
                Condition = "Elements",
                Elements = new[] { "Air", "Lightning" },
                Tiers = new[] { new TeamBondTierData { MinCount = 2, Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Speed", Magnitude = 5 } } } }
            };
            EffectData effect = bond.Tiers[0].Effects[0];

            switch (fault)
            {
                case "Condition":
                    bond.Condition = value;
                    break;
                case "Scope":
                    bond.Scope = value;
                    break;
                case "OneElement":
                    bond.Elements = new[] { "Air" };
                    break;
                case "DuplicateElement":
                    bond.Elements = new[] { "Air", "Air", "Fire" };
                    break;
                case "NoneElement":
                    bond.Elements = new[] { "Air", "None" };
                    break;
                case "TierTooLow":
                    bond.Tiers[0].MinCount = 1;
                    break;
                case "TierAboveSet":
                    bond.Tiers[0].MinCount = 3;
                    break;
                case "TiersNotRising":
                    bond.Condition = "Stance";
                    bond.Elements = new string[0];
                    bond.Stance = "Vanguard";
                    bond.Tiers = new[] { bond.Tiers[0], new TeamBondTierData { MinCount = 2, Effects = bond.Tiers[0].Effects } };
                    break;
                case "NoTiers":
                    bond.Tiers = new TeamBondTierData[0];
                    break;
                case "DamageEffect":
                    effect.EffectType = "Damage";
                    effect.Magnitude = 50;
                    break;
                case "DebuffEffect":
                    effect.EffectType = "DebuffStat";
                    break;
                case "StunEffect":
                    effect.EffectType = "ApplyStatus";
                    effect.Status = "Stun";
                    effect.DurationTurns = 1;
                    break;
                case "LowChance":
                    effect.Chance = 50;
                    break;
                case "HpBuff":
                    effect.AffectedStat = "HP";
                    break;
                case "DuplicateId":
                    bond.BondId = library.BeastSkills[0].SkillId;
                    break;
                case "StanceWithElements":
                    bond.Condition = "Stance";
                    bond.Stance = "Ranged";
                    break;
            }

            List<TeamBondData> bonds = new List<TeamBondData>(library.TeamBonds) { bond };
            library.TeamBonds = bonds.ToArray();
            List<string> errors = SkillLibraryValidator.Validate(library);

            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "Expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsASpeciesBondNamingAnUnknownSpecies_GivenTheRoster()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            TeamBondData bond = new TeamBondData
            {
                BondId = "odd_couple",
                DisplayName = "Odd Couple",
                Description = "d",
                Condition = "Species",
                Species = new[] { "golem", "unicorn" },
                Tiers = new[] { new TeamBondTierData { MinCount = 2, Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 5 } } } }
            };
            library.TeamBonds = new List<TeamBondData>(library.TeamBonds) { bond }.ToArray();

            List<string> errors = SkillLibraryValidator.Validate(library, BeastRosterTests.LoadRoster());

            Assert.IsTrue(errors.Exists(e => e.Contains("'unicorn' is not a species in the roster")), string.Join("\n", errors));
            Assert.IsEmpty(SkillLibraryValidator.Validate(SkillLibraryTests.LoadLibrary(), BeastRosterTests.LoadRoster()));
        }

        [Test]
        public void Validator_AcceptsALibraryWithNoBonds()
        {
            SkillLibraryData library = SkillLibraryTests.LoadLibrary();
            library.TeamBonds = new TeamBondData[0];

            Assert.IsEmpty(SkillLibraryValidator.Validate(library, BeastRosterTests.LoadRoster()));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        /// <summary>A small battle with a shield-wall bond and damage rolls, as a trace of every turn.</summary>
        private string BondedBattleTrace(int seed, out BattleResult result)
        {
            TeamBondSO wall = StanceBond("wall", CombatStance.Vanguard,
                                         Tier(2, new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 40, DurationTurns = 3 },
                                              Buff(StatType.CritChance, 10)));
            List<BattleUnit> team;
            List<BattleUnit> roster = SmallBattle(out team);
            result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(seed), null, null,
                                                  TeamBondLoadout.For(new[] { wall }, MembersOf(team), team));
            return Trace(result, roster);
        }

        private string PlainTrace(int seed, TeamBondLoadout bonds)
        {
            List<BattleUnit> roster = SmallBattle(out List<BattleUnit> _);
            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(roster), roster, null, new System.Random(seed), null, null, bonds);
            return Trace(result, roster);
        }

        private List<BattleUnit> SmallBattle(out List<BattleUnit> team)
        {
            SkillSO hit = new SkillSO();
            _created.Add(hit);
            hit.SkillId = "hit";
            hit.TargetShape = SkillTargetShape.AllEnemies;
            hit.Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 60f } };
            BattleUnit a = new BattleUnit("p1", BattleTeam.Player, new StatBlock(400, 100, 100, 0, 0, 10, 0, 20), HexCoordinate.Zero, new SkillLoadout(new[] { hit }), null, 1,
                                          CombatStance.Vanguard);
            BattleUnit b = new BattleUnit("p2", BattleTeam.Player, new StatBlock(400, 100, 100, 0, 0, 12, 0, 20), new HexCoordinate(0, 1), new SkillLoadout(new[] { hit }), null,
                                          1, CombatStance.Vanguard);
            BattleUnit e = new BattleUnit("e1", BattleTeam.Enemy, new StatBlock(1500, 100, 100, 0, 0, 11, 0, 20), new HexCoordinate(3, 0), new SkillLoadout(new[] { hit }));
            team = new List<BattleUnit> { a, b };
            return new List<BattleUnit> { a, b, e };
        }

        private static string Trace(BattleResult result, List<BattleUnit> roster)
        {
            System.Text.StringBuilder trace = new System.Text.StringBuilder(result.Outcome + " " + result.ElapsedTicks + " " + result.ActionCount + ":");
            foreach (BattleUnit unit in roster)
            {
                trace.Append(' ').Append(unit.Id).Append('=').Append(unit.CurrentHp);
            }

            return trace.ToString();
        }

        private List<TeamBondSO> BuildBonds(SkillLibraryData library)
        {
            List<TeamBondSO> bonds = new List<TeamBondSO>();
            foreach (TeamBondData data in library.TeamBonds)
            {
                TeamBondSO bond = new TeamBondSO();
                _created.Add(bond);
                SkillLibraryBuilder.ApplyTeamBond(data, bond);
                bonds.Add(bond);
            }

            return bonds;
        }

        private static List<BattleUnit> Roster(List<BattleUnit> team, params BattleUnit[] enemies)
        {
            List<BattleUnit> roster = new List<BattleUnit>(team);
            roster.AddRange(enemies);
            return roster;
        }

        private static List<TeamBondMember> MembersOf(List<BattleUnit> team)
        {
            // Battle units carry no species id; stance and elements are what these bonds read.
            return team.ConvertAll(u => new TeamBondMember(u.Id, u.Stance, u.Elements));
        }

        private static BattleUnit Unit(string id, BattleTeam team, CombatStance stance, int column, Element element = Element.None)
        {
            return new BattleUnit(id, team, new StatBlock(1000, 100, 100, 100, 100, 10), new HexCoordinate(column, 0), null,
                                  element == Element.None ? null : new[] { element }, 1, stance);
        }

        private static List<TeamBondMember> Team(params TeamBondMember[] members)
        {
            return new List<TeamBondMember>(members);
        }

        private static TeamBondMember Member(string speciesId, CombatStance stance, params Element[] elements)
        {
            return new TeamBondMember(speciesId, stance, elements);
        }

        private static TeamBondTier Tier(int minCount, params SkillEffect[] effects)
        {
            return new TeamBondTier { MinCount = minCount, Effects = new List<SkillEffect>(effects) };
        }

        private static SkillEffect Buff(StatType stat, int amount)
        {
            return new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = stat, Magnitude = amount };
        }

        private static SkillEffect PctBuff(StatType stat, int percent)
        {
            return new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = stat, Magnitude = percent, IsPercent = true };
        }

        private TeamBondSO ScalingBond(string id, CombatStance stance, TeamBondScope scope, int maxCount, params SkillEffect[] perStack)
        {
            TeamBondSO bond = NewBond(id, scope, new[] { Tier(1, perStack) });
            bond.Condition = TeamBondCondition.Stance;
            bond.Stance = stance;
            bond.PerCount = true;
            bond.MaxCount = maxCount;
            return bond;
        }

        private TeamBondSO StanceBond(string id, CombatStance stance, params TeamBondTier[] tiers)
        {
            TeamBondSO bond = NewBond(id, TeamBondScope.Members, tiers);
            bond.Condition = TeamBondCondition.Stance;
            bond.Stance = stance;
            return bond;
        }

        private TeamBondSO ElementBond(string id, TeamBondScope scope, Element[] elements, params TeamBondTier[] tiers)
        {
            TeamBondSO bond = NewBond(id, scope, tiers);
            bond.Condition = TeamBondCondition.Elements;
            bond.Elements = new List<Element>(elements);
            return bond;
        }

        private TeamBondSO SpeciesBond(string id, string[] species, params TeamBondTier[] tiers)
        {
            TeamBondSO bond = NewBond(id, TeamBondScope.Members, tiers);
            bond.Condition = TeamBondCondition.Species;
            bond.SpeciesIds = new List<string>(species);
            return bond;
        }

        private TeamBondSO NewBond(string id, TeamBondScope scope, TeamBondTier[] tiers)
        {
            TeamBondSO bond = new TeamBondSO();
            _created.Add(bond);
            bond.BondId = id;
            bond.DisplayName = id;
            bond.Scope = scope;
            bond.Tiers = new List<TeamBondTier>(tiers);
            return bond;
        }
    }
}
