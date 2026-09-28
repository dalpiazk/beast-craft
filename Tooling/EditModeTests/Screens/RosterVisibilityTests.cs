using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The roster-visibility view-models (the Theorycrafter's screens): the roster, the beast detail
    /// screen's stats and derived numbers (each checked against the Core rule it reads, never a copy
    /// of it), the skill swap and upgrade flows, gear on and off, the targeting-rule text for every
    /// skill (a snapshot), the element chart, the encounter preview's level gap and matchups, and the
    /// battle log's damage breakdown multiplying back to every recorded hit across seeds.
    /// </summary>
    public class RosterVisibilityTests
    {
        private const int MapSeed = 424242;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(int level = 1)
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => MapSeed);
            session.StartWith(TestSaves.SixStarters(Content, level));
            return session;
        }

        // ------------------------------------------------------------------ roster

        [Test]
        public void Roster_ListsTheOwnedBeasts_ThenASilhouettePerSpeciesNotFound_AndSorts()
        {
            GameSession session = NewSession();
            session.Save.FindBeast("b2").Progress.Level = 9;
            RosterViewModel roster = new RosterViewModel(session);

            Assert.AreEqual(TestSaves.SixSpecies.Length, roster.Owned.Count);
            Assert.AreEqual(Content.Species.Count, roster.Owned.Count + roster.Silhouettes.Count, "every species is either owned or a silhouette");
            Assert.IsTrue(roster.Silhouettes.TrueForAll(s => s.Silhouette && s.BeastId == null && s.Hint == RosterViewModel.SilhouetteHint && s.Name == "???"));
            Assert.IsFalse(roster.Silhouettes.Exists(s => Array.IndexOf(TestSaves.SixSpecies, s.SpeciesId) >= 0));
            CollectionAssert.AreEqual(TestSaves.SixSpecies, roster.Owned.ConvertAll(e => e.SpeciesId), "joined order by default");
            Assert.IsTrue(roster.Owned.TrueForAll(e => e.ArtKey != null && e.Element != Element.None));

            roster.SortBy(RosterSort.Level);
            Assert.AreEqual("b2", roster.Owned[0].BeastId, "highest level first");
            roster.SortBy(RosterSort.Name);
            List<string> names = roster.Owned.ConvertAll(e => e.Name);
            CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), names);
            roster.SortBy(RosterSort.Element);
            CollectionAssert.IsOrdered(roster.Owned.ConvertAll(e => (int)e.Element));
            roster.NextSort();
            Assert.AreEqual(RosterSort.Stance, roster.Sort);
            CollectionAssert.IsOrdered(roster.Owned.ConvertAll(e => (int)e.Stance));
            roster.NextSort();
            Assert.AreEqual(RosterSort.Joined, roster.Sort, "the sort button cycles");
        }

        // ------------------------------------------------------------------ stats and derived numbers

        [Test]
        public void Detail_Stats_AreTheCoresStats_WithTheGearsShareBrokenOut()
        {
            GameSession session = NewSession(12);
            string instance = session.Save.Gear.AddBeastGear("fang_t1_rare");
            BeastDetailViewModel detail = new BeastDetailViewModel(session, "b1");
            Assert.IsTrue(detail.EquipGear(GearSlot.WeaponOrCore, instance, out string message), message);

            OwnedBeast beast = session.Save.FindBeast("b1");
            CreatureSpeciesSO species = Content.Battle.GetSpecies(beast.Progress.SpeciesId);
            StatBlock baseStats = StatCalculator.GetBaseStatsAtLevel(species, 12);
            StatBlock total = StatCalculator.ComputeStats(species, 12, new[] { Content.Battle.GetGear("fang_t1_rare") });
            Assert.AreEqual(7, detail.Stats.Count, "HP, Atk, Def, SpA, SpD, Speed, Crit");
            foreach (StatLine line in detail.Stats)
            {
                Assert.AreEqual(baseStats.GetStat(line.Stat), line.Base, line.Label);
                Assert.AreEqual(total.GetStat(line.Stat), line.Total, line.Label);
                Assert.AreEqual(line.Total - line.Base, line.Gear, line.Label);
            }

            Assert.Greater(detail.Stats.Find(l => l.Stat == StatType.Attack).Gear, 0, "the fang adds Attack");
            Assert.AreEqual(0, detail.Stats.Find(l => l.Stat == StatType.Speed).Gear);
            Assert.AreEqual(1, detail.GearContributions.Count);
            CollectionAssert.AreEqual(new[] { "+1 Atk", "+3.58% Atk" }, detail.GearContributions[0].Bonuses);
        }

        [Test]
        public void Detail_DerivedNumbers_ReadTheCoreFormulas()
        {
            GameSession session = NewSession(20);
            BeastDetailViewModel detail = new BeastDetailViewModel(session, "b3");
            StatBlock total = detail.Total;

            // ATB: turns per 100 ticks is the fill rate over the threshold, relative to the team and the average enemy.
            Assert.AreEqual(TurnManager.FillRateForSpeed(total.Speed) * 100.0 / TurnManager.ActionThreshold, detail.TurnsPer100Ticks, 1e-12);
            TurnRateView self = detail.TurnRates.Find(r => r.IsSelf);
            Assert.AreEqual(1.0, self.Relative, 1e-12);
            Assert.AreEqual(TurnManager.FillRateForSpeed(total.Speed), self.FillRate);
            Assert.AreEqual(GameSession.PartySize - 1, detail.TurnRates.Count(r => !r.IsSelf && !r.IsEnemy), "the rest of the party");
            foreach (TurnRateView rate in detail.TurnRates.Where(r => !r.IsEnemy))
            {
                Assert.AreEqual(DerivedStats.TurnsPer100Ticks(rate.Speed), rate.TurnsPer100Ticks, 1e-12);
                Assert.AreEqual(rate.TurnsPer100Ticks / detail.TurnsPer100Ticks, rate.Relative, 1e-12);
            }

            TurnRateView enemy = detail.TurnRates.Single(r => r.IsEnemy);
            List<double> enemyTurns = new List<double>();
            foreach (Encounters.EnemyData data in Content.Enemies.Enemies)
            {
                enemyTurns.Add(DerivedStats.TurnsPer100Ticks(Content.Enemies.Species(data.EnemyId, Element.None).GetStatAtLevel(StatType.Speed, 20)));
            }

            Assert.AreEqual(enemyTurns.Average(), enemy.TurnsPer100Ticks, 1e-12, "the level-matched average enemy");

            // Element chart both ways.
            Assert.AreEqual(10, detail.Matchups.Count);
            foreach (ElementMatchView match in detail.Matchups)
            {
                Assert.AreEqual(ElementChart.GetMultiplier(detail.AttackElement, match.Element), match.Dealt, match.Element.ToString());
                Assert.AreEqual(ElementChart.GetMultiplier(match.Element, detail.Elements), match.Taken, match.Element.ToString());
            }

            // Crits and the level-gap curve.
            Assert.AreEqual(DamageFormula.ClampCritChance(total.CritChance), detail.CritChance);
            Assert.AreEqual(DamageFormula.GetCritMultiplier(0f), detail.CritMultiplier);
            Assert.AreEqual(11, detail.LevelGap.Count, "-5..+5");
            foreach (LevelGapPoint point in detail.LevelGap)
            {
                Assert.AreEqual(20 + point.Gap, point.EnemyLevel);
                Assert.AreEqual(DamageFormula.GetLevelMultiplier(20, point.EnemyLevel), point.Dealt, 0.0);
                Assert.AreEqual(DamageFormula.GetLevelMultiplier(point.EnemyLevel, 20), point.Taken, 0.0);
            }

            Assert.AreEqual(1.0, detail.LevelGap.Single(p => p.Gap == 0).Dealt, 0.0);
            Assert.IsNotEmpty(detail.StanceBehaviour, "the stance's glossary definition");
        }

        [TestCase(37, 100)]
        [TestCase(81, 144)]
        [TestCase(250, 49)]
        public void TurnsPer100Ticks_IsWhatTheTurnManagerActuallyGives(int speedA, int speedB)
        {
            BattleUnit a = new BattleUnit("a", BattleTeam.Player, new StatBlock(10, 1, 1, 1, 1, speedA), HexCoordinate.Zero);
            BattleUnit b = new BattleUnit("b", BattleTeam.Enemy, new StatBlock(10, 1, 1, 1, 1, speedB), HexCoordinate.Zero);
            TurnManager turns = new TurnManager(new[] { a, b });
            int countA = 0;
            int countB = 0;
            while (turns.ElapsedTicks < 200000)
            {
                if (turns.CurrentUnit == a)
                {
                    countA++;
                }
                else
                {
                    countB++;
                }

                turns.AdvanceTurn();
            }

            double ticks = turns.ElapsedTicks;
            Assert.AreEqual(DerivedStats.TurnsPer100Ticks(speedA) * ticks / 100.0, countA, 1.5);
            Assert.AreEqual(DerivedStats.TurnsPer100Ticks(speedB) * ticks / 100.0, countB, 1.5);
        }

        // ------------------------------------------------------------------ skills: swap and upgrade

        [Test]
        public void Skills_SwapUnderTheSkillBooksRules_AndAutosave()
        {
            GameSession session = NewSession(30);
            OwnedBeast beast = session.Save.FindBeast("b1");
            BeastDetailViewModel detail = new BeastDetailViewModel(session, "b1");
            SkillEntryView tome = detail.Learnable.Find(e => !e.Known);
            Assert.IsNotNull(tome, "the species' kit lists skills beyond its loadout");
            beast.Skills.Learn(tome.SkillId); // as a skill tome teaches it
            detail.Refresh();
            string first = detail.Slots[0].SkillId;
            string second = detail.Slots[1].SkillId;
            Assert.AreEqual(3, detail.Slots.Count);
            Assert.IsTrue(detail.Slots.TrueForAll(s => s.Card != null && s.Progress != null));
            int saves = session.AutosaveCount;

            Assert.IsTrue(detail.EquipSkill(0, second, out string message), message);
            Assert.AreEqual(second, beast.Skills.GetEquipped(0), "an equipped skill swaps places");
            Assert.AreEqual(first, beast.Skills.GetEquipped(1));
            Assert.AreEqual(saves + 1, session.AutosaveCount);
            Assert.AreEqual(AutosaveReason.BeastEdit, session.LastAutosaveReason);

            SkillEntryView spare = detail.Learnable.Find(e => e.Known && e.EquippedSlot < 0);
            Assert.IsNotNull(spare, "a learned skill that is not equipped");
            Assert.IsTrue(detail.EquipSkill(2, spare.SkillId, out message), message);
            Assert.AreEqual(spare.SkillId, beast.Skills.GetEquipped(2));

            SkillEntryView locked = detail.Learnable.Find(e => !e.Known);
            Assert.IsNotNull(locked, "a skill not learned yet");
            Assert.Greater(locked.LearnLevel, 1);
            Assert.IsFalse(detail.EquipSkill(1, locked.SkillId, out message));
            Assert.AreEqual("Not learned yet.", message);

            Assert.IsTrue(detail.UnequipSkill(2, out message), message);
            Assert.IsTrue(detail.UnequipSkill(1, out message), message);
            Assert.IsFalse(detail.UnequipSkill(0, out message), "the last skill stays");
            Assert.IsNotNull(beast.Skills.GetEquipped(0));
        }

        [Test]
        public void Skills_TrainWithMaterials_ThenBreakThroughAtTheTierGate()
        {
            GameSession session = NewSession(10);
            BeastDetailViewModel detail = new BeastDetailViewModel(session, "b1");
            string skillId = detail.Slots[0].SkillId;
            SkillProgress progress = session.Save.FindBeast("b1").Skills.GetProgress(skillId);
            List<SkillMaterialSO> materials = BeastDetailViewModel.MaterialDefinitions(Content);
            Assert.AreEqual(Content.SkillLibrary.Materials.Length, materials.Count);
            SkillMaterialSO shard = materials.Find(m => m.Tier == 1);
            SkillMaterialSO crystal = materials.Find(m => m.Tier == 2);

            // Nothing held: nothing to do.
            Assert.IsTrue(detail.Materials(skillId).TrueForAll(m => !m.CanTrain && !m.CanBreakthrough && m.Owned == 0));
            Assert.IsFalse(detail.Train(skillId, shard.MaterialId, out string message));

            session.Save.Materials.Add(shard.MaterialId, 60);
            session.Save.Materials.Add(crystal.MaterialId, 1);
            SkillProgressView before = detail.Slots[0].Progress;
            Assert.AreEqual(1, before.Level);
            Assert.AreEqual(SkillProgression.XpToNextLevel(1), before.XpToNext);
            Assert.AreEqual(5, before.NextGateLevel);
            Assert.AreEqual(1, before.NextGateMaterialTier);

            // Train to the first gate: each shard spent, its XP through SkillProgression.ApplyMaterial.
            int saves = session.AutosaveCount;
            SkillProgress mirror = new SkillProgress(skillId);
            SkillSO skill = Content.Battle.GetSkill(skillId);
            while (!detail.Slots[0].Progress.AwaitingBreakthrough)
            {
                int held = session.Save.Materials.GetCount(shard.MaterialId);
                Assert.IsTrue(detail.Train(skillId, shard.MaterialId, out message), message);
                SkillProgression.ApplyMaterial(mirror, skill.Progression, shard);
                Assert.AreEqual(held - 1, session.Save.Materials.GetCount(shard.MaterialId), "one spent");
                Assert.AreEqual(mirror.Level, progress.Level);
                Assert.AreEqual(mirror.Xp, progress.Xp);
            }

            Assert.Greater(session.AutosaveCount, saves);
            Assert.AreEqual(5, progress.Level, "stopped at the tier's cap");
            Assert.IsFalse(detail.Train(skillId, shard.MaterialId, out message), "no XP at the gate: a breakthrough is due");

            // The gate: a tier 1 material or better, spent on success.
            MaterialOptionView option = detail.Materials(skillId).Find(m => m.MaterialId == shard.MaterialId);
            Assert.IsTrue(option.CanBreakthrough);
            int shards = session.Save.Materials.GetCount(shard.MaterialId);
            Assert.IsTrue(detail.Breakthrough(skillId, shard.MaterialId, out message), message);
            Assert.AreEqual(1, progress.Tier);
            Assert.AreEqual(shards - 1, session.Save.Materials.GetCount(shard.MaterialId));
            Assert.AreEqual(10, detail.Slots[0].Progress.LevelCap);
            Assert.AreEqual(2, detail.Slots[0].Progress.NextGateMaterialTier, "the next gate needs a crystal");

            // Up to the second gate, where a shard is refused and the crystal opens it.
            while (!detail.Slots[0].Progress.AwaitingBreakthrough)
            {
                Assert.IsTrue(detail.Train(skillId, shard.MaterialId, out message), message);
            }

            option = detail.Materials(skillId).Find(m => m.MaterialId == shard.MaterialId);
            Assert.IsFalse(option.CanBreakthrough);
            StringAssert.Contains("tier 2", option.Reason);
            Assert.IsFalse(detail.Breakthrough(skillId, shard.MaterialId, out message));
            Assert.IsTrue(detail.Breakthrough(skillId, crystal.MaterialId, out message), message);
            Assert.AreEqual(2, progress.Tier);
            Assert.AreEqual(0, session.Save.Materials.GetCount(crystal.MaterialId));
            Assert.Greater(detail.Slots[0].Progress.PowerMultiplier, 1.0, "levels grow its power");
        }

        [Test]
        public void Gear_GoesOnAndOff_UnderGearRules()
        {
            GameSession session = NewSession(3);
            string fang = session.Save.Gear.AddBeastGear("fang_t1");
            string barding = session.Save.Gear.AddBeastGear("barding_t1");
            BeastDetailViewModel b1 = new BeastDetailViewModel(session, "b1");
            GearSlotView weapon = b1.Gear.Find(g => g.Slot == GearSlot.WeaponOrCore);
            Assert.IsNull(weapon.Worn);
            Assert.IsTrue(weapon.Options.Exists(o => o.InstanceId == fang && o.Equippable));
            Assert.IsFalse(weapon.Options.Exists(o => o.InstanceId == barding), "only the slot's gear is offered");

            Assert.IsFalse(b1.EquipGear(GearSlot.ArmorOrShell, fang, out string message), "wrong slot");
            Assert.IsTrue(b1.EquipGear(GearSlot.WeaponOrCore, fang, out message), message);
            Assert.AreEqual(fang, b1.Gear.Find(g => g.Slot == GearSlot.WeaponOrCore).Worn.InstanceId);

            BeastDetailViewModel b2 = new BeastDetailViewModel(session, "b2");
            GearView elsewhere = b2.Gear.Find(g => g.Slot == GearSlot.WeaponOrCore).Options.Find(o => o.InstanceId == fang);
            Assert.IsFalse(elsewhere.Equippable);
            StringAssert.StartsWith("Worn by", elsewhere.Reason);
            Assert.IsFalse(b2.EquipGear(GearSlot.WeaponOrCore, fang, out message), "worn by another beast");

            Assert.IsTrue(b1.UnequipGear(GearSlot.WeaponOrCore, out message), message);
            Assert.IsNull(b1.Gear.Find(g => g.Slot == GearSlot.WeaponOrCore).Worn);
            Assert.IsFalse(b1.UnequipGear(GearSlot.WeaponOrCore, out message));
            Assert.IsTrue(b2.EquipGear(GearSlot.WeaponOrCore, fang, out message), message);
        }

        [Test]
        public void Detail_Bonds_ListThePartners_AndLooks_ShowWhatIsWorn()
        {
            GameSession session = NewSession(5);
            BeastDetailViewModel detail = new BeastDetailViewModel(session, "b1");
            Assert.IsNotEmpty(detail.Bonds, "every beast takes part in some bond");
            foreach (BondView bond in detail.Bonds)
            {
                Assert.IsNotEmpty(bond.Condition);
                Assert.IsNotEmpty(bond.TierCounts);
                Assert.IsFalse(bond.Partners.Contains(detail.Name), "never its own partner");
            }

            Assert.IsTrue(detail.Bonds.Exists(b => b.Partners.Count > 0), "partners among the six");
            Assert.IsNotEmpty(detail.Looks, "its look categories");
            Assert.IsTrue(detail.Looks.TrueForAll(l => !string.IsNullOrEmpty(l.Category) && !string.IsNullOrEmpty(l.Option)));
        }

        // ------------------------------------------------------------------ targeting rules

        [Test]
        public void TargetingRules_EverySkill_IsSpeltOutFromItsData_Snapshot()
        {
            StringBuilder snapshot = new StringBuilder();
            List<KeyValuePair<string, SkillSO>> skills = AllSkills();
            Assert.Greater(skills.Count, 60);
            foreach (KeyValuePair<string, SkillSO> pair in skills)
            {
                SkillSO skill = pair.Value;
                string rule = SkillCard.TargetingRuleText(skill);
                string taunt = SkillCard.TauntRuleText(skill);
                Assert.IsNotEmpty(rule, skill.SkillId);
                Assert.IsTrue(rule.EndsWith(".", StringComparison.Ordinal), skill.SkillId + ": " + rule);
                Assert.IsNotEmpty(taunt, skill.SkillId);
                AssertAccurate(skill, rule, taunt);
                Assert.AreEqual(rule, SkillCard.Of(skill, Content.Glossary).TargetingRule, "the card carries it");
                snapshot.Append(pair.Key).Append(": ").Append(rule).Append(" | ").Append(taunt).Append('\n');
            }

            GoldenFiles.AssertMatches("targeting-rules.golden.txt", snapshot.ToString());
        }

        private static void AssertAccurate(SkillSO skill, string rule, string taunt)
        {
            string at = skill.SkillId + ": " + rule;
            string hexes = skill.Range == 1 ? "1 hex" : skill.Range.ToString(CultureInfo.InvariantCulture) + " hexes";
            string side = skill.TargetSide == SkillTargetSide.Ally ? "ally" : "enemy";
            switch (skill.TargetShape)
            {
                case SkillTargetShape.Self:
                    Assert.AreEqual("Affects only itself.", rule, at);
                    break;
                case SkillTargetShape.AllEnemies:
                    StringAssert.Contains("every enemy on the field", rule, at);
                    break;
                case SkillTargetShape.AllAllies:
                    StringAssert.Contains("every ally on the field", rule, at);
                    break;
                case SkillTargetShape.AreaBurst:
                case SkillTargetShape.Cross:
                    StringAssert.Contains("every " + side, rule, at);
                    StringAssert.Contains(hexes, rule, at);
                    break;
                default:
                    StringAssert.Contains(" " + side, rule, at);
                    StringAssert.Contains("within " + hexes, rule, at);
                    string order = skill.TargetingOrder == SkillTargetingOrder.Lowest ? "lowest" : "highest";
                    switch (skill.TargetingCriterion)
                    {
                        case SkillTargetingCriterion.Random:
                            StringAssert.Contains("a random " + side, rule, at);
                            break;
                        case SkillTargetingCriterion.HpFraction:
                            StringAssert.Contains(order + " HP%", rule, at);
                            break;
                        case SkillTargetingCriterion.CurrentHp:
                            StringAssert.Contains(skill.TargetingOrder == SkillTargetingOrder.Lowest ? "least HP left" : "most HP left", rule, at);
                            break;
                        case SkillTargetingCriterion.Distance:
                            StringAssert.Contains(skill.TargetingOrder == SkillTargetingOrder.Lowest ? "nearest" : "farthest", rule, at);
                            break;
                        case SkillTargetingCriterion.Stat:
                            StringAssert.Contains(order + " ", rule, at);
                            break;
                    }

                    break;
            }

            bool redirected = skill.TargetSide == SkillTargetSide.Enemy && (skill.TargetShape == SkillTargetShape.SingleTarget || skill.TargetShape == SkillTargetShape.Line);
            Assert.AreEqual(redirected, taunt.StartsWith("Taunt overrides", StringComparison.Ordinal), skill.SkillId + ": " + taunt);
        }

        [Test]
        public void TargetingRules_ReadTheCriterionOrderAndRange()
        {
            SkillSO skill = new SkillSO
            {
                SkillId = "probe",
                TargetShape = SkillTargetShape.SingleTarget,
                TargetSide = SkillTargetSide.Enemy,
                TargetingCriterion = SkillTargetingCriterion.HpFraction,
                TargetingOrder = SkillTargetingOrder.Lowest,
                Range = 3
            };
            Assert.AreEqual("Targets the enemy with the lowest HP% within 3 hexes.", SkillCard.TargetingRuleText(skill));
            skill.TargetSide = SkillTargetSide.Ally;
            skill.TargetingCriterion = SkillTargetingCriterion.Stat;
            skill.TargetingStat = StatType.HP;
            skill.Range = 1;
            Assert.AreEqual("Targets the ally (itself included) with the lowest max HP within 1 hex.", SkillCard.TargetingRuleText(skill));
            skill.TargetSide = SkillTargetSide.Enemy;
            skill.TargetShape = SkillTargetShape.Line;
            skill.TargetingCriterion = SkillTargetingCriterion.Distance;
            skill.TargetingOrder = SkillTargetingOrder.Highest;
            skill.Range = 4;
            Assert.AreEqual("Picks the farthest enemy within 4 hexes, then strikes a straight line 4 hexes long toward it, hitting every enemy on the line.",
                            SkillCard.TargetingRuleText(skill));
            StringAssert.StartsWith("Taunt overrides", SkillCard.TauntRuleText(skill));
        }

        /// <summary>Every beast skill, avatar active and enemy kit skill, labelled (an enemy's by "enemy/skill").</summary>
        private static List<KeyValuePair<string, SkillSO>> AllSkills()
        {
            List<KeyValuePair<string, SkillSO>> skills = new List<KeyValuePair<string, SkillSO>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillData data in Content.SkillLibrary.BeastSkills.Concat(Content.SkillLibrary.AvatarActives))
            {
                SkillSO skill = Content.Battle.GetSkill(data.SkillId);
                if (skill != null && seen.Add(skill.SkillId))
                {
                    skills.Add(new KeyValuePair<string, SkillSO>(skill.SkillId, skill));
                }
            }

            foreach (Encounters.EnemyData enemy in Content.Enemies.Enemies)
            {
                foreach (SkillSO skill in Content.Enemies.Kit(enemy.EnemyId, Element.None))
                {
                    string label = enemy.EnemyId + "/" + skill.SkillId;
                    if (seen.Add(label))
                    {
                        skills.Add(new KeyValuePair<string, SkillSO>(label, skill));
                    }
                }
            }

            return skills;
        }

        // ------------------------------------------------------------------ element chart

        [Test]
        public void ElementChart_IsTheCoresChart_WithTheTeamHighlighted()
        {
            ElementChartViewModel chart = new ElementChartViewModel(new[] { Element.Fire, Element.None, Element.Water });
            Assert.AreEqual(10, chart.Rows.Count);
            CollectionAssert.AreEquivalent(new[] { Element.Fire, Element.Water }, chart.TeamElements);
            int strong = 0;
            int mild = 0;
            int weak = 0;
            foreach (List<ElementCell> row in chart.Rows)
            {
                Assert.AreEqual(10, row.Count);
                foreach (ElementCell cell in row)
                {
                    float expected = ElementChart.GetMultiplier(cell.Attack, cell.Defend);
                    Assert.AreEqual(expected, cell.Multiplier, cell.Attack + " on " + cell.Defend);
                    MatchupKind kind = expected == ElementChart.Strong ? MatchupKind.Strong
                        : expected == ElementChart.Mild ? MatchupKind.Mild
                        : expected == ElementChart.Weak ? MatchupKind.Weak : MatchupKind.Neutral;
                    Assert.AreEqual(kind, cell.Kind);
                    Assert.AreEqual(chart.TeamElements.Contains(cell.Attack) || chart.TeamElements.Contains(cell.Defend), cell.Highlighted);
                    strong += kind == MatchupKind.Strong ? 1 : 0;
                    mild += kind == MatchupKind.Mild ? 1 : 0;
                    weak += kind == MatchupKind.Weak ? 1 : 0;
                }
            }

            Assert.Greater(strong, 0);
            Assert.Greater(mild, 0);
            Assert.Greater(weak, 0);
            Assert.AreEqual(ElementChart.Strong, chart.Cell(Element.Fire, Element.Nature).Multiplier);
            Assert.AreEqual(MatchupKind.Mild, chart.Cell(Element.Light, Element.Water).Kind);
            Assert.AreEqual(MatchupKind.Weak, chart.Cell(Element.Fire, Element.Water).Kind);
        }

        // ------------------------------------------------------------------ encounter preview

        [Test]
        public void EncounterInsight_LevelGap_Matchups_AndEnemySkills_ReadTheCore()
        {
            GameSession session = NewSession(4);
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            EncounterViewModel encounter = new EncounterViewModel(session, node.NodeId);
            EncounterInsightView insight = EncounterInsightView.For(session, encounter);

            Assert.AreEqual(4, insight.TeamLevel);
            Assert.AreEqual(encounter.Team.Count, insight.Team.Count);
            Assert.AreEqual(encounter.Enemies.Count, insight.Enemies.Count);
            foreach (EnemyInsightView enemy in insight.Enemies)
            {
                Assert.AreEqual(enemy.Level - 4, enemy.Gap);
                Assert.AreEqual(DamageFormula.GetLevelMultiplier(4, enemy.Level), enemy.TeamDealt, 0.0);
                Assert.AreEqual(DamageFormula.GetLevelMultiplier(enemy.Level, 4), enemy.EnemyDealt, 0.0);
                Assert.IsNotEmpty(enemy.GapText);
                Assert.IsNotEmpty(enemy.Skills, enemy.Name + "'s kit");
                Assert.IsTrue(enemy.Skills.TrueForAll(c => !string.IsNullOrEmpty(c.TargetingRule) && !string.IsNullOrEmpty(c.TauntRule)));
            }

            Assert.IsTrue(insight.Enemies.Exists(e => e.Gap != 0), "the team is over the first location's level");
            CollectionAssert.AllItemsAreUnique(insight.ColumnElements, "one column per enemy element");
            CollectionAssert.AreEquivalent(insight.Enemies.Select(e => e.Element).Distinct(), insight.ColumnElements);
            Assert.AreEqual(encounter.Enemies.Sum(e => e.Count), insight.ColumnCounts.Sum());
            for (int r = 0; r < insight.Team.Count; r++)
            {
                CreatureSpeciesSO species = Content.Battle.GetSpecies(insight.Team[r].SpeciesId);
                Assert.AreEqual(insight.ColumnElements.Count, insight.Matrix[r].Count);
                for (int c = 0; c < insight.ColumnElements.Count; c++)
                {
                    MatchupCellView cell = insight.Matrix[r][c];
                    Assert.AreEqual(ElementChart.GetMultiplier(insight.TeamAttack[r], insight.ColumnElements[c]), cell.Dealt);
                    Assert.AreEqual(ElementChart.GetMultiplier(insight.ColumnElements[c], species.Elements), cell.Taken);
                    Assert.AreEqual(Math.Sign(Math.Sign(cell.Dealt - 1f) - Math.Sign(cell.Taken - 1f)), cell.Verdict);
                }
            }
        }

        // ------------------------------------------------------------------ battle log

        [TestCase(1, 1)]
        [TestCase(2, 3)]
        [TestCase(3, 6)]
        [TestCase(4, 2)]
        [TestCase(5, 9)]
        [TestCase(6, 4)]
        [TestCase(7, 1)]
        [TestCase(8, 12)]
        public void BattleLog_EveryHitsBreakdown_MultipliesBackToTheDamage(int mapSeed, int teamLevel)
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => mapSeed);
            session.StartWith(TestSaves.SixStarters(Content, teamLevel));
            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            NodeBattle battle = new EncounterViewModel(session, node.NodeId).Start(out string error);
            Assert.IsNotNull(battle, error);
            battle.Run.Battle.RunToEnd();

            int recorded = 0;
            foreach (PassiveActivation opening in battle.Run.Battle.OpeningPassiveActivations)
            {
                recorded += opening.Activation?.Hits.Count ?? 0;
            }

            foreach (BattleTurnResult turn in battle.Run.Battle.Turns)
            {
                foreach (SkillActivation activation in Activations(turn))
                {
                    recorded += activation.Hits.Count;
                }
            }

            ResultsViewModel results = battle.Complete();
            BattleLogViewModel log = results.Log;
            List<BattleLogEntry> hits = log.Entries.Where(e => e.Kind == BattleLogKind.Hit).ToList();
            Assert.AreEqual(recorded, hits.Count, "one line per recorded hit");
            Assert.Greater(hits.Count, 0);
            bool levelGap = false;
            foreach (BattleLogEntry hit in hits)
            {
                DamageBreakdownView b = hit.Breakdown;
                Assert.IsNotNull(b, hit.Text);
                Assert.AreEqual(hit.Amount, b.Amount);
                Assert.AreEqual(b.Amount, b.Recomputed, hit.Text + ": the Core formula on the recorded components");
                int product = Math.Max(DamageFormula.MinimumDamage, (int)b.Product);
                Assert.AreEqual(b.Amount, b.Power > 0f ? product : 0, hit.Text + ": the components multiply back");
                Assert.AreEqual(DamageFormula.ComputeBase(b.Power, b.Attack, b.Defense), b.Base, 0.0);
                Assert.That(b.VariancePercent, Is.InRange(DamageFormula.VarianceMinPercent, DamageFormula.VarianceMaxPercent));
                Assert.AreEqual(DamageFormula.CritMultiplier, b.CritMultiplier);
                Assert.AreEqual(hit.Crit, b.Crit);
                levelGap |= b.LevelMultiplier != 1.0;
                Assert.IsNotEmpty(b.Lines());
            }

            Assert.IsTrue(levelGap || teamLevel == 1, "a team over the location's level fights across a gap");

            // Filtering by unit keeps only its lines.
            Assert.IsNotEmpty(log.Units);
            foreach (BattleLogUnit unit in log.Units)
            {
                log.Filter = unit.UnitId;
                List<BattleLogEntry> mine = log.Filtered();
                Assert.IsTrue(mine.TrueForAll(e => e.ActorId == unit.UnitId || e.TargetId == unit.UnitId), unit.Name);
            }

            log.Filter = null;
            Assert.AreEqual(log.Entries.Count, log.Filtered().Count);
            Assert.IsTrue(log.Entries.Any(e => e.Kind == BattleLogKind.Defeat), "someone fell");
            Assert.IsTrue(log.Entries.Any(e => e.ActorId == CampaignAvatar.UnitId), "the Beastbinder's arts or passives");
        }

        [Test]
        public void BattleLog_RecordsHealsShieldsAndStatuses_WithTheirAmounts()
        {
            bool heal = false;
            bool shield = false;
            bool status = false;
            for (int seed = 1; seed <= 12 && !(heal && shield && status); seed++)
            {
                GameSession session = new GameSession(Content, new MemorySaveStorage(), () => seed);
                session.StartWith(TestSaves.SixStarters(Content, 3));
                MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
                NodeBattle battle = new EncounterViewModel(session, node.NodeId).Start(out string error);
                battle.Run.Battle.RunToEnd();
                BattleLogViewModel log = battle.Complete().Log;
                foreach (BattleLogEntry entry in log.Entries)
                {
                    heal |= entry.Kind == BattleLogKind.Heal && entry.Amount >= 0 && entry.Text.Contains("heals");
                    shield |= entry.Kind == BattleLogKind.Shield && entry.Text.Contains("shield");
                    status |= entry.Kind == BattleLogKind.Status || entry.Kind == BattleLogKind.StatChange;
                }
            }

            Assert.IsTrue(heal, "a heal was logged");
            Assert.IsTrue(shield, "a shield was logged");
            Assert.IsTrue(status, "a status or stat change was logged");
        }

        [Test]
        public void AppliedAmounts_AreWhatTheEffectDid()
        {
            BattleUnit caster = new BattleUnit("c", BattleTeam.Player, new StatBlock(100, 10, 40, 50, 10, 10), HexCoordinate.Zero);
            BattleUnit ally = new BattleUnit("a", BattleTeam.Player, new StatBlock(100, 10, 10, 10, 10, 10), HexCoordinate.Zero);
            ally.CurrentHp = 90;
            SkillSO skill = new SkillSO { SkillId = "mix", TargetSide = SkillTargetSide.Ally };
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 40 });
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Shield, Magnitude = 50, DurationTurns = 2 });
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.Attack, Magnitude = 5, DurationTurns = 2 });
            SkillActivation activation = new SkillActivation(skill, new[] { ally });
            SkillEffectApplier.Apply(activation, caster);

            Assert.AreEqual(3, activation.Applied.Count);
            Assert.AreEqual(10, activation.Applied[0].Amount, "the heal restored 10 of its 20 (it cannot overfill)");
            Assert.AreEqual(20, activation.Applied[1].Amount, "50% of Defense 40");
            Assert.AreEqual(5, activation.Applied[2].Amount);
        }

        // ------------------------------------------------------------------ glossary

        [Test]
        public void Glossary_HasTheNewTerms_TrueToTheRules()
        {
            foreach (string id in new[] { "atb", "gauge", "level_gap", "execute", "variance", "element_chart", "crit" })
            {
                Assert.IsNotNull(Content.Glossary.Find(id), id);
            }

            string gap = Content.Glossary.Find("level_gap").Definition;
            StringAssert.Contains(DamageFormula.LevelDifferencePerLevel.ToString(CultureInfo.InvariantCulture), gap);
            StringAssert.Contains(DamageFormula.LevelDifferenceConvex.ToString(CultureInfo.InvariantCulture), gap);
            StringAssert.Contains("x" + (1.0 - DamageFormula.LevelDifferenceCap).ToString(CultureInfo.InvariantCulture), gap);
            StringAssert.Contains("x" + (1.0 + DamageFormula.LevelDifferenceCap).ToString(CultureInfo.InvariantCulture), gap);
            string variance = Content.Glossary.Find("variance").Definition;
            StringAssert.Contains(DamageFormula.VarianceMinPercent + "%", variance);
            StringAssert.Contains(DamageFormula.VarianceMaxPercent + "%", variance);
            StringAssert.Contains(TurnManager.ActionThreshold.ToString("N0", CultureInfo.InvariantCulture), Content.Glossary.Find("gauge").Definition);
        }

        private static IEnumerable<SkillActivation> Activations(BattleTurnResult turn)
        {
            foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
            {
                if (outcome.Fired && outcome.Activation != null)
                {
                    yield return outcome.Activation;
                }
            }

            foreach (SkillActivation art in turn.AvatarActivations)
            {
                yield return art;
            }

            foreach (PassiveActivation passive in turn.PassiveActivations)
            {
                if (passive.Activation != null)
                {
                    yield return passive.Activation;
                }
            }

            foreach (Bonds.BondReactionRecord reaction in turn.BondReactions)
            {
                if (reaction.Activation != null)
                {
                    yield return reaction.Activation;
                }
            }
        }
    }
}
