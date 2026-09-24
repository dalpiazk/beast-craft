using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Progression;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Skill progression: the XP curve, practice and material XP, breakthrough gates, the skill book's
    /// equip rules, and how a skill's level and tier reach a battle (magnitude scaling, tier cooldown
    /// reduction and bonus effects, the loadout built from a book, and use counts read back out).
    /// </summary>
    public class SkillProgressionTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // XP curve.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void XpToNextLevel_IsStrictlyIncreasing()
        {
            for (int level = 1; level < 30; level++)
            {
                Assert.Greater(SkillProgression.XpToNextLevel(level + 1), SkillProgression.XpToNextLevel(level), "level " + level);
            }
        }

        [Test]
        public void XpCurve_PinnedNumbers_AreSlow()
        {
            Assert.AreEqual(100, SkillProgression.XpToNextLevel(1));
            Assert.AreEqual(100, SkillProgression.XpToNextLevel(0));
            Assert.AreEqual(3162, SkillProgression.XpToNextLevel(10));
            Assert.AreEqual(1703, SkillProgression.TotalXpToReach(5));
            Assert.AreEqual(11106, SkillProgression.TotalXpToReach(10));
            Assert.AreEqual(67135, SkillProgression.TotalXpToReach(20));

            // Practice alone: 171 uses to level 5 at 10 XP a use.
            Assert.AreEqual(171, (SkillProgression.TotalXpToReach(5) + SkillProgression.PracticeXpPerUse - 1) / SkillProgression.PracticeXpPerUse);
        }

        // ---------------------------------------------------------------------------------------
        // Practice XP.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void AwardPractice_LevelsUpWhenXpSuffices()
        {
            SkillProgress progress = new SkillProgress("s");
            SkillProgressionDefinition def = Ungated(20);

            Assert.AreEqual(0, SkillProgression.AwardPractice(progress, def, 9));
            Assert.AreEqual(1, progress.Level);
            Assert.AreEqual(90, progress.Xp);

            Assert.AreEqual(1, SkillProgression.AwardPractice(progress, def, 1));
            Assert.AreEqual(2, progress.Level);
            Assert.AreEqual(0, progress.Xp);
        }

        [Test]
        public void AwardPractice_CapsUsesPerAward_AndIgnoresNonPositiveUses()
        {
            SkillProgress progress = new SkillProgress("s");
            SkillProgressionDefinition def = Ungated(20);

            SkillProgression.AwardPractice(progress, def, 1000);

            // 20 uses credited = 200 XP: level 2 (100), 100 banked.
            Assert.AreEqual(2, progress.Level);
            Assert.AreEqual(100, progress.Xp);

            Assert.AreEqual(0, SkillProgression.AwardPractice(progress, def, 0));
            Assert.AreEqual(0, SkillProgression.AwardPractice(progress, def, -5));
            Assert.AreEqual(100, progress.Xp);
        }

        [Test]
        public void AddXp_StopsAtMaxLevel_WithNothingBanked()
        {
            SkillProgress progress = new SkillProgress("s");

            SkillProgression.AddXp(progress, Ungated(3), 1000000);

            Assert.AreEqual(3, progress.Level);
            Assert.AreEqual(0, progress.Xp);
        }

        // ---------------------------------------------------------------------------------------
        // Gates and breakthroughs.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void TierThreshold_BlocksLeveling_AndBanksOneLevelOfXp()
        {
            SkillProgress progress = new SkillProgress("s");
            SkillProgressionDefinition def = new SkillProgressionDefinition();

            int gained = SkillProgression.AddXp(progress, def, 1000000);

            Assert.AreEqual(4, gained);
            Assert.AreEqual(5, progress.Level);
            Assert.AreEqual(0, progress.Tier);
            Assert.AreEqual(SkillProgression.XpToNextLevel(5), progress.Xp);
            Assert.IsTrue(SkillProgression.IsAwaitingBreakthrough(progress, def));

            // More practice at the gate changes nothing.
            Assert.AreEqual(0, SkillProgression.AwardPractice(progress, def, 20));
            Assert.AreEqual(5, progress.Level);
            Assert.AreEqual(SkillProgression.XpToNextLevel(5), progress.Xp);
        }

        [Test]
        public void Breakthrough_WithRequiredTier_PassesGate_AndSpendsTheBank()
        {
            SkillProgress progress = new SkillProgress("s");
            SkillProgressionDefinition def = new SkillProgressionDefinition();
            SkillProgression.AddXp(progress, def, 1000000);

            Assert.AreEqual(SkillBreakthroughResult.Success, SkillProgression.TryBreakthrough(progress, def, Material(1, 0)));

            Assert.AreEqual(1, progress.Tier);
            Assert.AreEqual(6, progress.Level);
            Assert.AreEqual(0, progress.Xp);
            Assert.IsFalse(SkillProgression.IsAwaitingBreakthrough(progress, def));
            Assert.AreEqual(10, SkillProgression.LevelCap(def, progress.Tier));
        }

        [Test]
        public void Breakthrough_WithTooLowTier_Fails_AndChangesNothing()
        {
            SkillProgressionDefinition def = new SkillProgressionDefinition();
            SkillProgress progress = new SkillProgress("s") { Level = 10, Tier = 1, Xp = 7 };

            Assert.AreEqual(SkillBreakthroughResult.MaterialTierTooLow, SkillProgression.TryBreakthrough(progress, def, Material(1, 0)));
            Assert.AreEqual(1, progress.Tier);
            Assert.AreEqual(10, progress.Level);
            Assert.AreEqual(7, progress.Xp);

            // A higher tier than required also passes.
            Assert.AreEqual(SkillBreakthroughResult.Success, SkillProgression.TryBreakthrough(progress, def, Material(3, 0)));
            Assert.AreEqual(2, progress.Tier);
        }

        [Test]
        public void Breakthrough_BelowThreshold_NoTierRemaining_AndMissingInput()
        {
            SkillProgressionDefinition def = new SkillProgressionDefinition();

            SkillProgress early = new SkillProgress("s") { Level = 3 };
            Assert.AreEqual(SkillBreakthroughResult.BelowThreshold, SkillProgression.TryBreakthrough(early, def, Material(5, 0)));
            Assert.AreEqual(0, early.Tier);

            SkillProgress done = new SkillProgress("s") { Level = 20, Tier = 3 };
            Assert.AreEqual(SkillBreakthroughResult.NoTierRemaining, SkillProgression.TryBreakthrough(done, def, Material(5, 0)));

            Assert.AreEqual(SkillBreakthroughResult.MissingInput, SkillProgression.TryBreakthrough(early, def, null));
            Assert.AreEqual(SkillBreakthroughResult.MissingInput, SkillProgression.TryBreakthrough(null, def, Material(1, 0)));
        }

        [Test]
        public void ApplyMaterial_AddsItsXp()
        {
            SkillProgress progress = new SkillProgress("s");

            int gained = SkillProgression.ApplyMaterial(progress, new SkillProgressionDefinition(), Material(1, 400));

            // 400 XP: 100 to level 2, 283 to level 3, 17 left.
            Assert.AreEqual(2, gained);
            Assert.AreEqual(3, progress.Level);
            Assert.AreEqual(17, progress.Xp);
            Assert.AreEqual(0, SkillProgression.ApplyMaterial(progress, null, null));
        }

        // ---------------------------------------------------------------------------------------
        // Level and tier in battle.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SkillInstance_LevelOne_IsTheAuthoredSkill()
        {
            SkillSO skill = DamageSkill(100f, 3);
            SkillInstance instance = new SkillInstance(skill);

            Assert.AreEqual(1, instance.Level);
            Assert.AreEqual(0, instance.Tier);
            Assert.AreEqual(1.0, instance.MagnitudeMultiplier);
            Assert.AreEqual(3, instance.EffectiveCooldown);
            Assert.AreSame(skill.Effects, instance.Effects);
            Assert.AreEqual(39.7f, instance.ScaleMagnitude(39.7f));
        }

        [Test]
        public void SkillInstance_ClampsLevelAndTier()
        {
            SkillSO skill = DamageSkill(100f, 3);

            SkillInstance high = new SkillInstance(skill, 99, 99);
            Assert.AreEqual(20, high.Level);
            Assert.AreEqual(3, high.Tier);

            SkillInstance low = new SkillInstance(skill, -4, -1);
            Assert.AreEqual(1, low.Level);
            Assert.AreEqual(0, low.Tier);
        }

        [Test]
        public void Level_ScalesDamage_ThroughTheDamageFormula()
        {
            // Attack 100 against Defense 0 makes the base exactly the power, so the damage is the scaled power.
            SkillSO skill = DamageSkill(100f, 0);

            Assert.AreEqual(100, DamageDealt(new SkillInstance(skill, 1)));
            Assert.AreEqual(103, DamageDealt(new SkillInstance(skill, 2)));
            Assert.AreEqual(130, DamageDealt(new SkillInstance(skill, 11)));
            Assert.AreEqual(157, DamageDealt(new SkillInstance(skill, 20)));
        }

        [Test]
        public void Level_ScalesHealAndBuffMagnitudes()
        {
            SkillSO skill = Skill(0);
            skill.TargetShape = SkillTargetShape.Self;
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 10f });
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.Attack, Magnitude = 10f, DurationTurns = 2 });

            // SpecialAttack 100, so the heal's percent reads as HP: 10 x 1.3 = 13.
            BattleUnit unit = new BattleUnit("u", BattleTeam.Player, new StatBlock(100, 50, 0, 100, 0, 10), HexCoordinate.Zero);
            unit.CurrentHp = 50;

            SkillEffectApplier.Apply(new SkillActivation(new SkillInstance(skill, 11), new[] { unit }), unit);

            Assert.AreEqual(63, unit.CurrentHp);
            Assert.AreEqual(63, unit.Stats.Attack);
        }

        [Test]
        public void TierBonus_ReducesCooldown_AndAppendsBonusEffects()
        {
            SkillSO skill = DamageSkill(100f, 3);
            skill.Progression.Tiers[0].CooldownReduction = 1;
            skill.Progression.Tiers[0].BonusEffects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f });
            skill.Progression.Tiers[1].CooldownReduction = 5;

            SkillInstance tierOne = new SkillInstance(skill, 5, 1);
            Assert.AreEqual(2, tierOne.EffectiveCooldown);
            Assert.AreEqual(2, tierOne.Effects.Count);
            Assert.AreEqual(1, skill.Effects.Count, "the asset's own list is not modified");

            // Reductions accumulate and clamp at 0.
            Assert.AreEqual(0, new SkillInstance(skill, 10, 2).EffectiveCooldown);

            SkillLoadout loadout = SkillLoadout.FromInstances(new[] { tierOne });
            Assert.AreEqual(2, loadout.RemainingCooldown(0));
            loadout.MarkFired(0);
            Assert.AreEqual(2, loadout.RemainingCooldown(0));
        }

        [Test]
        public void TierBonus_AppliesInBattle()
        {
            // Cooldown 2 less a tier reduction of 1 fires on the unit's very first turn; untiered it would not.
            SkillSO skill = DamageSkill(100f, 2);
            skill.TargetShape = SkillTargetShape.AllEnemies;
            skill.Progression.Tiers[0].CooldownReduction = 1;
            skill.Progression.Tiers[0].BonusEffects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50f });

            BattleUnit caster = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 0, 0, 0, 10), HexCoordinate.Zero,
                                               SkillLoadout.FromInstances(new[] { new SkillInstance(skill, 5, 1) }));
            BattleUnit target = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(1000, 0, 0, 0, 0, 10), new HexCoordinate(2, 0));
            List<BattleUnit> all = new List<BattleUnit> { caster, target };

            BattleTurnResult turn = BattleTurnExecutor.ExecuteTurn(caster, all, null, null, null);

            Assert.AreEqual(1, turn.SkillOutcomes.Count);
            Assert.IsTrue(turn.SkillOutcomes[0].Fired);
            Assert.AreEqual(5, turn.SkillOutcomes[0].Activation.Instance.Level);

            // Level 5 is +12%: 112 from the base effect and 56 from the tier bonus.
            Assert.AreEqual(1000 - 112 - 56, target.CurrentHp);

            BattleUnit untiered = new BattleUnit("p2", BattleTeam.Player, new StatBlock(1000, 100, 0, 0, 0, 10), HexCoordinate.Zero,
                                                 new SkillLoadout(new[] { skill }));
            Assert.AreEqual(0, BattleTurnExecutor.ExecuteTurn(untiered, all, null, null, null).SkillOutcomes.Count);
        }

        // ---------------------------------------------------------------------------------------
        // Skill book.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void SkillBook_Learn_RejectsDuplicatesAndEmptyIds()
        {
            BeastSkillBook book = new BeastSkillBook();

            Assert.IsTrue(book.Learn("a"));
            Assert.IsFalse(book.Learn("a"));
            Assert.IsFalse(book.Learn((string)null));
            Assert.IsFalse(book.Learn(""));
            Assert.IsFalse(book.Learn((SkillSO)null));
            Assert.AreEqual(1, book.Known.Count);
            Assert.AreEqual(1, book.GetProgress("a").Level);
        }

        [Test]
        public void SkillBook_EquipRules()
        {
            BeastSkillBook book = new BeastSkillBook();
            book.Learn("a");
            book.Learn("b");

            Assert.AreEqual(SkillEquipResult.UnknownSkill, book.Equip(0, "zzz"));
            Assert.AreEqual(SkillEquipResult.UnknownSkill, book.Equip(0, null));
            Assert.AreEqual(SkillEquipResult.SlotOutOfRange, book.Equip(-1, "a"));
            Assert.AreEqual(SkillEquipResult.SlotOutOfRange, book.Equip(BeastSkillBook.EquipSlotCount, "a"));

            Assert.AreEqual(SkillEquipResult.Equipped, book.Equip(0, "a"));
            Assert.AreEqual(SkillEquipResult.AlreadyEquipped, book.Equip(2, "a"));
            Assert.AreEqual(SkillEquipResult.Equipped, book.Equip(0, "a"), "same slot is a no-op success");

            // Slots may be empty.
            Assert.IsNull(book.GetEquipped(1));
            Assert.IsNull(book.GetEquipped(2));
            Assert.IsNull(book.GetEquipped(5));

            // Replacing a slot's skill.
            Assert.AreEqual(SkillEquipResult.Equipped, book.Equip(0, "b"));
            Assert.AreEqual("b", book.GetEquipped(0));
            Assert.AreEqual(-1, book.IndexOfEquipped("a"));

            Assert.IsTrue(book.SwapSlots(0, 2));
            Assert.IsNull(book.GetEquipped(0));
            Assert.AreEqual("b", book.GetEquipped(2));
            Assert.IsFalse(book.SwapSlots(0, 3));

            Assert.IsTrue(book.Unequip(2));
            Assert.IsFalse(book.Unequip(2));
            Assert.IsFalse(book.Unequip(7));
        }

        [Test]
        public void SkillBook_ToleratesWrongLengthSlotData()
        {
            BeastSkillBook book = new BeastSkillBook { Equipped = new[] { "" } };
            book.Learn("a");

            Assert.IsNull(book.GetEquipped(0), "an empty string is an empty slot");
            Assert.AreEqual(SkillEquipResult.Equipped, book.Equip(2, "a"));
            Assert.AreEqual(BeastSkillBook.EquipSlotCount, book.Equipped.Length);
            Assert.AreEqual("a", book.GetEquipped(2));
        }

        [Test]
        public void SkillBook_LearnAvailable_LearnsSpeciesSkillsUpToLevel()
        {
            SkillSO first = Skill(1, "first");
            SkillSO fifth = Skill(1, "fifth");
            SkillSO tenth = Skill(1, "tenth");
            CreatureSpeciesSO species = new CreatureSpeciesSO();
            _created.Add(species);
            species.LearnableSkills.Add(new SkillLearnEntry { Level = 1, Skill = first });
            species.LearnableSkills.Add(new SkillLearnEntry { Level = 5, Skill = fifth });
            species.LearnableSkills.Add(new SkillLearnEntry { Level = 10, Skill = tenth });

            BeastSkillBook book = new BeastSkillBook();

            Assert.AreEqual(2, book.LearnAvailable(species, 5));
            Assert.AreEqual(0, book.LearnAvailable(species, 5));
            Assert.IsTrue(book.Knows("first"));
            Assert.IsTrue(book.Knows("fifth"));
            Assert.IsFalse(book.Knows("tenth"));
            Assert.AreEqual(1, book.LearnAvailable(species, 10));
            Assert.AreEqual(0, book.LearnAvailable(null, 10));
        }

        // ---------------------------------------------------------------------------------------
        // Factory and use counts.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void BuildLoadout_UsesEquippedSlotsInSlotOrder_WithTheirLevels()
        {
            SkillSO a = Skill(1, "a");
            SkillSO b = Skill(1, "b");
            SkillSO c = Skill(1, "c");
            Dictionary<string, SkillSO> assets = new Dictionary<string, SkillSO> { { "a", a }, { "b", b }, { "c", c } };

            BeastSkillBook book = new BeastSkillBook();
            book.Learn(a);
            book.Learn(b);
            book.Learn(c);
            book.GetProgress("a").Level = 4;
            book.GetProgress("a").Tier = 0;
            book.GetProgress("c").Level = 7;
            book.GetProgress("c").Tier = 1;
            book.Equip(0, "c");
            book.Equip(2, "a");

            SkillLoadout loadout = BattleUnitFactory.BuildLoadout(book, Lookup(assets));

            Assert.AreEqual(2, loadout.Count);
            Assert.AreSame(c, loadout.Skills[0]);
            Assert.AreSame(a, loadout.Skills[1]);
            Assert.AreEqual(7, loadout.GetInstance(0).Level);
            Assert.AreEqual(1, loadout.GetInstance(0).Tier);
            Assert.AreEqual(4, loadout.GetInstance(1).Level);
            Assert.IsNull(loadout.GetInstance(2));

            CreatureSpeciesSO species = new CreatureSpeciesSO();
            _created.Add(species);
            BattleUnit unit = BattleUnitFactory.CreateBeast("b1", BattleTeam.Player, species, 1, null, HexCoordinate.Zero, book, Lookup(assets));
            Assert.AreEqual(2, unit.Skills.Count);
            Assert.AreSame(c, unit.Skills.Skills[0]);
        }

        [Test]
        public void BuildLoadout_SkipsUnresolvedSkills_AndToleratesNulls()
        {
            BeastSkillBook book = new BeastSkillBook();
            book.Learn("ghost");
            book.Equip(0, "ghost");

            Assert.AreEqual(0, BattleUnitFactory.BuildLoadout(book, Lookup(new Dictionary<string, SkillSO>())).Count);
            Assert.AreEqual(0, BattleUnitFactory.BuildLoadout(book, null).Count);
            Assert.AreEqual(0, BattleUnitFactory.BuildLoadout(null, Lookup(new Dictionary<string, SkillSO>())).Count);
        }

        [Test]
        public void CountFiredSkills_ReadsUsesFromRunBattle_AndFeedsPractice()
        {
            SkillSO strike = Skill(0, "strike");
            strike.TargetShape = SkillTargetShape.AllEnemies;
            strike.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 10f });
            SkillSO never = Skill(1000, "never");
            never.TargetShape = SkillTargetShape.AllEnemies;
            SkillSO poke = Skill(0, "poke");
            poke.TargetShape = SkillTargetShape.AllEnemies;
            poke.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 1f });
            SkillSO cheer = Skill(0, "cheer");
            cheer.TargetShape = SkillTargetShape.AllAllies;

            BattleUnit player = new BattleUnit("p", BattleTeam.Player, new StatBlock(1000, 100, 50, 0, 0, 10), HexCoordinate.Zero,
                                               new SkillLoadout(new[] { strike, never }));
            BattleUnit enemy = new BattleUnit("e", BattleTeam.Enemy, new StatBlock(100, 10, 50, 0, 0, 10), new HexCoordinate(2, 0),
                                              new SkillLoadout(new[] { poke }));
            BattleUnit avatar = BattleAvatar.Create(new SkillLoadout(new[] { cheer }));
            List<BattleUnit> roster = new List<BattleUnit> { player, enemy };

            BattleResult result = BattleTurnExecutor.RunBattle(new TurnManager(new List<BattleUnit>(roster) { avatar }), roster, null, new System.Random(3), avatar);
            Assert.AreEqual(BattleOutcome.PlayerVictory, result.Outcome);

            int playerFired = 0;
            int enemyFired = 0;
            int avatarFired = 0;
            foreach (BattleTurnResult turn in result.Turns)
            {
                foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
                {
                    if (outcome.Fired && turn.Unit == player)
                    {
                        playerFired++;
                    }
                    else if (outcome.Fired && turn.Unit == enemy)
                    {
                        enemyFired++;
                    }
                }

                avatarFired += turn.AvatarActivations.Count;
            }

            Dictionary<string, Dictionary<string, int>> counts = BattleSkillUsage.CountFiredSkills(result, avatar);

            Assert.Greater(playerFired, 0);
            Assert.AreEqual(playerFired, counts["p"]["strike"]);
            Assert.IsFalse(counts["p"].ContainsKey("never"));
            Assert.AreEqual(enemyFired, counts["e"]["poke"]);
            Assert.AreEqual(avatarFired, counts[avatar.Id]["cheer"]);
            Assert.IsFalse(BattleSkillUsage.CountFiredSkills(result).ContainsKey(avatar.Id), "avatar counted only when passed");
            Assert.AreEqual(0, BattleSkillUsage.CountFiredSkillsFor(result, "nobody").Count);

            BeastSkillBook book = new BeastSkillBook();
            book.Learn(strike);
            book.Learn(never);
            book.AwardPractice(BattleSkillUsage.CountFiredSkillsFor(result, "p"), Lookup(new Dictionary<string, SkillSO> { { "strike", strike } }));

            SkillProgress progress = book.GetProgress("strike");
            int expectedXp = System.Math.Min(playerFired, SkillProgression.PracticeUseCapPerAward) * SkillProgression.PracticeXpPerUse;
            Assert.AreEqual(expectedXp, SkillProgression.TotalXpToReach(progress.Level) + progress.Xp);
            Assert.AreEqual(0, book.GetProgress("never").Xp);
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        /// <summary>HP the target loses to one application of <paramref name="instance"/> on the deterministic fallback.</summary>
        private static int DamageDealt(SkillInstance instance)
        {
            BattleUnit caster = new BattleUnit("c", BattleTeam.Player, new StatBlock(1000, 100, 0, 0, 0, 10), HexCoordinate.Zero);
            BattleUnit target = new BattleUnit("t", BattleTeam.Enemy, new StatBlock(1000, 0, 0, 0, 0, 10), new HexCoordinate(1, 0));

            SkillEffectApplier.Apply(new SkillActivation(instance, new[] { target }), caster);

            return 1000 - target.CurrentHp;
        }

        private static SkillProgressionDefinition Ungated(int maxLevel)
        {
            return new SkillProgressionDefinition { MaxLevel = maxLevel, Tiers = new List<SkillTierDefinition>() };
        }

        private static System.Func<string, SkillSO> Lookup(Dictionary<string, SkillSO> assets)
        {
            return id =>
            {
                SkillSO skill;
                return id != null && assets.TryGetValue(id, out skill) ? skill : null;
            };
        }

        private SkillSO DamageSkill(float power, int cooldown)
        {
            SkillSO skill = Skill(cooldown, "dmg");
            skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = power });
            return skill;
        }

        private SkillSO Skill(int cooldown, string id = "skill")
        {
            SkillSO skill = new SkillSO();
            skill.SkillId = id;
            skill.Cooldown = cooldown;
            skill.TargetShape = SkillTargetShape.SingleTarget;
            skill.TargetSide = SkillTargetSide.Enemy;
            _created.Add(skill);
            return skill;
        }

        private SkillMaterialSO Material(int tier, int xp)
        {
            SkillMaterialSO material = new SkillMaterialSO();
            material.MaterialId = "mat-t" + tier;
            material.Tier = tier;
            material.XpValue = xp;
            _created.Add(material);
            return material;
        }
    }
}
