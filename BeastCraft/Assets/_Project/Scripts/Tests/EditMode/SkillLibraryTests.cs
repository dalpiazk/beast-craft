using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Progression;
using BeastCraft.Skills;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Checks the authored skill library (<c>Data/Skills/skill-library.json</c>) straight from the
    /// JSON, so it runs without imported assets. Structural rules come from
    /// <see cref="SkillLibraryValidator"/> (with negative cases over small hand-built libraries);
    /// the content guidelines — five or six skills per beast, each beast's signature mechanics, the
    /// tier pattern, the first-draft power budget — live here, where the balance pass can
    /// deliberately move them. See the design doc, "Beast skill kits".
    /// </summary>
    public class SkillLibraryTests
    {
        /// <summary>The budget rule's damage per cooldown turn for a range-1 skill (the standard kit's Strike).</summary>
        private const double MeleeBudget = 90.0;

        /// <summary>The budget rule's damage per cooldown turn for a range-2+ skill (the standard kit's Blast).</summary>
        private const double RangedBudget = 70.0;

        /// <summary>How far over its budget an unlimited damage skill may sit in this first draft.</summary>
        private const double BudgetCeiling = 1.2;

        /// <summary>How far, in points of HP, a beast heal's share of its own HP may drift between levels 1, 50 and 100.</summary>
        private const double HealShareTolerance = 5.0;

        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        // ----------------------------------------------------------------------------------------
        // The authored file: structure.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Library_PassesStructuralValidationAgainstTheRoster()
        {
            List<string> errors = SkillLibraryValidator.Validate(LoadLibrary(), BeastRosterTests.LoadRoster());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Library_EveryRosterSpeciesHasAKitOfFiveOrSixSkillsLearnedByLevel60()
        {
            SkillLibraryData library = LoadLibrary();
            BeastRosterData roster = BeastRosterTests.LoadRoster();

            Assert.AreEqual(roster.Species.Length, library.SpeciesKits.Length);
            foreach (SpeciesData species in roster.Species)
            {
                SpeciesKitData kit = Kit(library, species.SpeciesId);
                Assert.That(kit.LearnableSkills.Length, Is.InRange(5, 6), species.SpeciesId);
                Assert.AreEqual(BeastSkillBook.EquipSlotCount, kit.DefaultLoadout.Length, species.SpeciesId);

                int highest = 0;
                foreach (LearnEntryData entry in kit.LearnableSkills)
                {
                    Assert.That(entry.Level, Is.InRange(1, 60), species.SpeciesId + " " + entry.SkillId);
                    highest = Math.Max(highest, entry.Level);
                }

                Assert.That(highest, Is.GreaterThanOrEqualTo(40), species.SpeciesId + "'s learn levels should spread into the late game.");
            }
        }

        [Test]
        public void Library_EveryBeastSkillBelongsToExactlyOneSpecies()
        {
            SkillLibraryData library = LoadLibrary();
            Dictionary<string, int> owners = new Dictionary<string, int>();
            foreach (SpeciesKitData kit in library.SpeciesKits)
            {
                foreach (LearnEntryData entry in kit.LearnableSkills)
                {
                    owners[entry.SkillId] = owners.TryGetValue(entry.SkillId, out int n) ? n + 1 : 1;
                }
            }

            foreach (SkillData skill in library.BeastSkills)
            {
                Assert.IsTrue(owners.TryGetValue(skill.SkillId, out int count), skill.SkillId + " is learned by nobody.");
                Assert.AreEqual(1, count, skill.SkillId);
            }
        }

        [Test]
        public void Library_DamageSkillsCarryTheirSpeciesElement()
        {
            SkillLibraryData library = LoadLibrary();
            foreach (SpeciesData species in BeastRosterTests.LoadRoster().Species)
            {
                foreach (LearnEntryData entry in Kit(library, species.SpeciesId).LearnableSkills)
                {
                    SkillData skill = Skill(library, entry.SkillId);
                    if (DealsDamage(skill))
                    {
                        Assert.AreEqual(species.Elements[0], skill.Element, skill.SkillId);
                    }
                }
            }
        }

        [Test]
        public void Library_EverySkillAndPassiveBreaksThroughAt5_10_15WithMaterialTiers1_2_3AndGainsSomething()
        {
            SkillLibraryData library = LoadLibrary();
            List<KeyValuePair<string, ProgressionData>> all = new List<KeyValuePair<string, ProgressionData>>();
            foreach (SkillData s in library.BeastSkills)
            {
                all.Add(new KeyValuePair<string, ProgressionData>(s.SkillId, s.Progression));
            }

            foreach (SkillData s in library.AvatarActives)
            {
                all.Add(new KeyValuePair<string, ProgressionData>(s.SkillId, s.Progression));
            }

            foreach (PassiveData p in library.AvatarPassives)
            {
                all.Add(new KeyValuePair<string, ProgressionData>(p.PassiveId, p.Progression));
            }

            foreach (KeyValuePair<string, ProgressionData> pair in all)
            {
                TierData[] tiers = pair.Value.Tiers;
                Assert.AreEqual(3, tiers.Length, pair.Key);
                bool gains = false;
                for (int i = 0; i < 3; i++)
                {
                    Assert.AreEqual(5 * (i + 1), tiers[i].ThresholdLevel, pair.Key);
                    Assert.AreEqual(i + 1, tiers[i].RequiredMaterialTier, pair.Key);
                    gains |= tiers[i].CooldownReduction > 0 || (tiers[i].BonusEffects != null && tiers[i].BonusEffects.Length > 0);
                }

                Assert.IsTrue(gains, pair.Key + " gains nothing from its breakthroughs.");
            }
        }

        [Test]
        public void Library_CooldownReductionOnlyOnCooldownsOfThreeOrMore()
        {
            // Taking a cooldown-2 damage skill to 1 doubles its output; tiers stay "modest".
            SkillLibraryData library = LoadLibrary();
            foreach (SkillData skill in library.BeastSkills)
            {
                foreach (TierData tier in skill.Progression.Tiers)
                {
                    if (tier.CooldownReduction > 0)
                    {
                        Assert.That(skill.Cooldown, Is.GreaterThanOrEqualTo(3), skill.SkillId);
                        Assert.AreEqual(1, tier.CooldownReduction, skill.SkillId);
                    }
                }
            }

            // The avatar's actives deal no damage and act on its own, slower gauge (the milestone-2
            // avatar retune took Rallying Cry and Mending Light to cooldown 2): its tier-15 reduction
            // may bring a cooldown to 1, never below.
            foreach (SkillData skill in library.AvatarActives)
            {
                Assert.IsFalse(DealsDamage(skill), skill.SkillId);
                foreach (TierData tier in skill.Progression.Tiers)
                {
                    if (tier.CooldownReduction > 0)
                    {
                        Assert.AreEqual(1, tier.CooldownReduction, skill.SkillId);
                        Assert.That(skill.Cooldown - tier.CooldownReduction, Is.GreaterThanOrEqualTo(1), skill.SkillId);
                    }
                }
            }
        }

        [Test]
        public void Library_UnlimitedDamageSkillsStayWithinTheFirstDraftPowerBudget()
        {
            foreach (SkillData skill in LoadLibrary().BeastSkills)
            {
                if (skill.MaxUsesPerBattle > 0 || !DealsDamage(skill))
                {
                    continue;
                }

                double budget = skill.Range <= 1 && skill.TargetShape != "AllEnemies" ? MeleeBudget : RangedBudget;
                Assert.That(DamagePerTurn(skill), Is.LessThanOrEqualTo(budget * BudgetCeiling), skill.SkillId);
            }
        }

        [Test]
        public void Library_BeastHealsRestoreAboutTheSameShareOfHpAtEveryLevel()
        {
            // Heals are a percent of the caster's SpecialAttack, which grows on the same curve as HP,
            // so a beast's heal on itself restores about the same share of its HP at level 1, 50 and
            // 100 (the flat heals it replaced restored ~5x more of it at level 1 than at level 100).
            // Only whole-HP rounding at level 1 (HP 14-22) moves it.
            SkillLibraryData library = LoadLibrary();
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            int checkedHeals = 0;

            foreach (SpeciesKitData kit in library.SpeciesKits)
            {
                SpeciesData species = Array.Find(roster.Species, s => s.SpeciesId == kit.SpeciesId);
                GrowthCurveData curve = Array.Find(roster.GrowthCurves, c => c.CurveId == species.GrowthCurveId);
                foreach (LearnEntryData entry in kit.LearnableSkills)
                {
                    foreach (EffectData effect in Skill(library, entry.SkillId).Effects)
                    {
                        if (effect.EffectType != "Heal")
                        {
                            continue;
                        }

                        double low = double.MaxValue;
                        double high = double.MinValue;
                        foreach (int level in new[] { 1, 50, 100 })
                        {
                            float scale = BeastRosterValidator.ScaleAtLevel(curve, level);
                            int hp = Mathf.RoundToInt(species.BaseStats.Hp * scale);
                            BattleUnit caster = new BattleUnit("c", BattleTeam.Player,
                                                               new StatBlock(hp, 1, 1, Mathf.RoundToInt(species.BaseStats.SpecialAttack * scale), 1, 1), BeastCraft.Battle.Grid.HexCoordinate.Zero);
                            double share = 100.0 * SkillEffectApplier.GetHealAmount(caster, effect.Magnitude) / hp;
                            low = Math.Min(low, share);
                            high = Math.Max(high, share);
                        }

                        Assert.That(high - low, Is.LessThanOrEqualTo(HealShareTolerance), kit.SpeciesId + " " + entry.SkillId + " heal share " + low + "-" + high + "% of HP");
                        checkedHeals++;
                    }
                }
            }

            Assert.That(checkedHeals, Is.GreaterThan(0));
        }

        [Test]
        public void Library_EveryDefaultLoadoutDealsDamage()
        {
            SkillLibraryData library = LoadLibrary();
            foreach (SpeciesKitData kit in library.SpeciesKits)
            {
                Assert.IsTrue(Array.Exists(kit.DefaultLoadout, id => DealsDamage(Skill(library, id))), kit.SpeciesId);
            }
        }

        // ----------------------------------------------------------------------------------------
        // The authored file: each beast's signature mechanics.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Golem_TauntsAt90Percent_ShieldsAndKnocksBack()
        {
            SkillLibraryData library = LoadLibrary();
            EffectData taunt = FindEffect(library, "golem", e => e.Status == "Taunt");
            Assert.IsNotNull(taunt);
            Assert.AreEqual(90, taunt.Chance, "Retuned from 85 (the authored-kits retune).");
            Assert.IsNotNull(FindEffect(library, "golem", e => e.Status == "Shield"));
            Assert.IsNotNull(FindEffect(library, "golem", e => e.Status == "Knockback"));
            AssertDefaultsInclude(library, "golem", e => e.Status == "Taunt");
            AssertDefaultsInclude(library, "golem", e => e.Status == "Shield");
        }

        [Test]
        public void Leviathan_TauntsSustainsAndHasAWave()
        {
            SkillLibraryData library = LoadLibrary();
            AssertDefaultsInclude(library, "leviathan", e => e.Status == "Taunt");
            AssertDefaultsInclude(library, "leviathan", e => e.Status == "Shield" || e.EffectType == "Heal");
            Assert.IsTrue(HasSkill(library, "leviathan", s => DealsDamage(s) && s.TargetShape != "SingleTarget"));
            Assert.AreEqual(3, Skill(library, "undertow").Range, "Retuned from 2 (niche pass), like the Golem's Stone Challenge: the taunt reaches ranged enemies.");
            Assert.AreEqual(65f, Skill(library, "deep_shell").Effects[0].Magnitude, "Retuned from 50 (niche pass).");
        }

        [Test]
        public void Treant_HealsTheWorstOffAlly_ShieldsRootsAndPoisons()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsTrue(HasSkill(library, "treant", s => s.TargetSide == "Ally" && s.TargetingCriterion == "HpFraction" && s.TargetingOrder == "Lowest" &&
                                                           Array.Exists(s.Effects, e => e.EffectType == "Heal")));
            EffectData root = FindEffect(library, "treant", e => e.Status == "Stun");
            Assert.IsNotNull(root);
            Assert.That(root.Chance, Is.LessThanOrEqualTo(30));
            Assert.IsNotNull(FindEffect(library, "treant", e => e.Status == "Shield"));
            Assert.IsNotNull(FindEffect(library, "treant", e => e.Status == "DamageOverTime"));
        }

        [Test]
        public void Tarasque_HeavyHit_StackingArmorBreak_AndFortify()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsTrue(HasSkill(library, "tarasque", s => s.Category == "Physical" && Array.Exists(s.Effects, e => e.EffectType == "Damage" && e.Magnitude >= 150f)));
            EffectData sunder = FindEffect(library, "tarasque", e => e.EffectType == "DebuffStat" && e.AffectedStat == "Defense" && e.MaxStacks == 3);
            Assert.IsNotNull(sunder);
            Assert.IsTrue(HasSkill(library, "tarasque", s => s.TargetShape == "Self" && Array.Exists(s.Effects, e => e.EffectType == "BuffStat")));
        }

        [Test]
        public void FrostWyrm_FreezesChillsInStacksAndHasAnIceAoE()
        {
            SkillLibraryData library = LoadLibrary();
            AssertDefaultsInclude(library, "frost_wyrm", e => e.Status == "Stun");
            Assert.IsNotNull(FindEffect(library, "frost_wyrm", e => e.EffectType == "DebuffStat" && e.AffectedStat == "Speed" && e.MaxStacks > 1));
            Assert.IsTrue(HasSkill(library, "frost_wyrm", s => DealsDamage(s) && s.TargetShape == "AreaBurst"));
        }

        [Test]
        public void Thunderbird_ManyWeakHits_MultiHitAoE_CritBuff_AndALimitedOpener()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsTrue(HasSkill(library, "thunderbird", s => s.TargetShape == "SingleTarget" && Array.Exists(s.Effects, e => e.HitCount >= 3 && e.Magnitude <= 40f)));
            Assert.IsTrue(HasSkill(library, "thunderbird", s => s.TargetShape == "AreaBurst" && Array.Exists(s.Effects, e => e.HitCount >= 3)));
            Assert.IsNotNull(FindEffect(library, "thunderbird", e => e.EffectType == "BuffStat" && e.AffectedStat == "CritChance"));
            Assert.IsTrue(HasSkill(library, "thunderbird", s => s.InitialCooldown == 0 && s.MaxUsesPerBattle > 0 && DealsDamage(s)));

            // The niche pass put the opener in the default loadout in place of Static Charge
            // (still learnable at level 3), and moved its learn level from 8 to 5.
            CollectionAssert.AreEqual(new[] { "thunder_talons", "chain_lightning", "storm_dive" }, Kit(library, "thunderbird").DefaultLoadout);
        }

        [Test]
        public void Griffin_GustKnockback_LineStrike_EvasionAndMobility()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsNotNull(FindEffect(library, "griffin", e => e.Status == "Knockback"));
            Assert.IsTrue(HasSkill(library, "griffin", s => s.TargetShape == "Line" && DealsDamage(s)));
            Assert.IsTrue(HasSkill(library, "griffin", s => s.TargetShape == "Self" && Array.Exists(s.Effects, e => e.AffectedStat == "Defense")));
            Assert.IsNotNull(FindEffect(library, "griffin", e => e.EffectType == "BuffStat" && e.AffectedStat == "MoveRange"));
        }

        [Test]
        public void Phoenix_StackingBurn_FireAoE_AndAOncePerBattleRebirth()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsNotNull(FindEffect(library, "phoenix", e => e.Status == "DamageOverTime" && e.MaxStacks > 1));
            Assert.IsTrue(HasSkill(library, "phoenix", s => DealsDamage(s) && s.TargetShape != "SingleTarget"));
            Assert.IsTrue(HasSkill(library, "phoenix", s => s.TargetShape == "Self" && s.MaxUsesPerBattle == 1 &&
                                                            Array.Exists(s.Effects, e => e.EffectType == "Heal") &&
                                                            Array.Exists(s.Effects, e => e.Status == "Shield")));
        }

        [Test]
        public void Kirin_TeamHeal_TeamBuff_Nuke_AndWard()
        {
            SkillLibraryData library = LoadLibrary();
            AssertDefaultsInclude(library, "kirin", e => e.EffectType == "Heal");
            Assert.IsTrue(HasSkill(library, "kirin", s => s.TargetShape == "AllAllies" && Array.Exists(s.Effects, e => e.EffectType == "Heal")));
            Assert.IsTrue(HasSkill(library, "kirin", s => s.TargetShape == "AllAllies" && Array.Exists(s.Effects, e => e.AffectedStat == "SpecialAttack")));
            Assert.IsTrue(HasSkill(library, "kirin", s => s.TargetShape == "SingleTarget" && Array.Exists(s.Effects, e => e.EffectType == "Damage" && e.Magnitude >= 150f)));
            Assert.IsTrue(HasSkill(library, "kirin", s => s.TargetShape == "AllAllies" && Array.Exists(s.Effects, e => e.Status == "Shield")));
            Assert.AreEqual(66f, Skill(library, "radiant_bolt").Effects[0].Magnitude, "Retuned from 62 (niche pass): 0.94x the range-2+ budget.");
        }

        [Test]
        public void Basilisk_Executes_Petrifies_Poisons_AndCritFishes()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.IsTrue(HasSkill(library, "basilisk", s => s.TargetingCriterion == "HpFraction" &&
                                                             Array.Exists(s.Effects, e => e.ExecuteBonusPercent >= 50)));
            EffectData gaze = FindEffect(library, "basilisk", e => e.Status == "Stun");
            Assert.IsNotNull(gaze);
            // Retuned from 25% to 45% (the authored-kits retune): a real petrify on a Ranged assassin,
            // paid for under the budget rule's hard-control band (45 power on cooldown 3).
            Assert.That(gaze.Chance, Is.LessThanOrEqualTo(50));
            Assert.IsNotNull(FindEffect(library, "basilisk", e => e.Status == "DamageOverTime"));
            Assert.IsTrue(HasSkill(library, "basilisk", s => Array.Exists(s.Effects, e => e.HitCount >= 3) ||
                                                             Array.Exists(s.Effects, e => e.AffectedStat == "CritChance")));
        }

        // ----------------------------------------------------------------------------------------
        // The authored file: avatar and materials.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Avatar_HasFourToSixPositionFreeActives()
        {
            SkillData[] actives = LoadLibrary().AvatarActives;
            Assert.That(actives.Length, Is.InRange(4, 6));
            foreach (SkillData skill in actives)
            {
                Assert.That(skill.TargetShape, Is.AnyOf("Self", "AllAllies", "AllEnemies"), skill.SkillId);
            }
        }

        [Test]
        public void Avatar_HasEightToTenPassivesCoveringEveryTrigger_AndThreeDefaults()
        {
            SkillLibraryData library = LoadLibrary();
            Assert.That(library.AvatarPassives.Length, Is.InRange(8, 10));
            HashSet<string> triggers = new HashSet<string>();
            foreach (PassiveData passive in library.AvatarPassives)
            {
                triggers.Add(passive.Trigger);
            }

            foreach (string trigger in Enum.GetNames(typeof(PassiveTrigger)))
            {
                Assert.IsTrue(triggers.Contains(trigger), "No passive uses " + trigger + ".");
            }

            Assert.AreEqual(AvatarSkillBook.PassiveSlotCount, library.AvatarDefaultPassives.Length);
        }

        [Test]
        public void Materials_AreThreeTiersWithRisingXp()
        {
            SkillMaterialData[] materials = LoadLibrary().Materials;
            Assert.AreEqual(3, materials.Length);
            for (int i = 0; i < materials.Length; i++)
            {
                Assert.AreEqual(i + 1, materials[i].Tier, materials[i].MaterialId);
                if (i > 0)
                {
                    Assert.That(materials[i].XpValue, Is.GreaterThan(materials[i - 1].XpValue), materials[i].MaterialId);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // The builder: DTO to runtime objects.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Builder_MapsEverySkillFieldAndTheTierBonuses()
        {
            SkillSO skill = BuildSkill(Skill(LoadLibrary(), "stone_challenge"));

            Assert.AreEqual("stone_challenge", skill.SkillId);
            Assert.AreEqual(SkillTargetShape.AreaBurst, skill.TargetShape);
            Assert.AreEqual(3, skill.Range, "Retuned from 2 to 3 so the taunt reaches more of an encounter.");
            Assert.AreEqual(3, skill.Cooldown);
            Assert.AreEqual(SkillSO.UseCooldownAsInitial, skill.InitialCooldown);
            Assert.AreEqual(SkillTargetSide.Enemy, skill.TargetSide);
            Assert.AreEqual(SkillTargetingCriterion.Distance, skill.TargetingCriterion, "A missing criterion reads as Distance, not Random.");
            Assert.AreEqual(Element.Earth, skill.Element);
            Assert.AreEqual(1, skill.Effects.Count);
            Assert.AreEqual(SkillEffectType.ApplyStatus, skill.Effects[0].EffectType);
            Assert.AreEqual(StatusType.Taunt, skill.Effects[0].Status);
            Assert.AreEqual(90, skill.Effects[0].Chance, "Retuned from 85.");
            Assert.AreEqual(3, skill.Progression.Tiers.Count);

            SkillInstance mastered = new SkillInstance(skill, 16, SkillLibraryBuilder.TierForLevel(skill.Progression, 16));
            Assert.AreEqual(3, mastered.Tier);
            Assert.AreEqual(2, mastered.EffectiveCooldown);
            Assert.AreEqual(2, mastered.Effects.Count, "The level-10 gate appends its Attack debuff.");
            Assert.AreEqual(StatType.Attack, mastered.Effects[1].AffectedStat);
        }

        [Test]
        public void Builder_MapsOpenersAndMultiHits()
        {
            SkillLibraryData library = LoadLibrary();
            SkillSO dive = BuildSkill(Skill(library, "storm_dive"));
            SkillSO talons = BuildSkill(Skill(library, "thunder_talons"));
            SkillSO coup = BuildSkill(Skill(library, "coup_de_grace"));

            SkillSO chain = BuildSkill(Skill(library, "chain_lightning"));

            Assert.AreEqual(0, dive.InitialCooldown);
            Assert.AreEqual(1, dive.MaxUsesPerBattle);
            Assert.AreEqual(120f, dive.Effects[0].Magnitude, "Retuned from 230 (niche pass): a default-loadout opener, not a learned nuke.");
            Assert.AreEqual(3, talons.Effects[0].HitCount);
            Assert.AreEqual(2, talons.Range, "Retuned from 1 (Thunderbird range vs move): it fires from outside melee and can retreat.");
            Assert.AreEqual(26f, talons.Effects[0].Magnitude, "Retuned from 28 (niche pass): 26 x 3 = 78 is 1.11x the range-2+ budget.");
            Assert.AreEqual(22f, chain.Effects[0].Magnitude, "Retuned from 28 (niche pass): 22 x 3 at radius 2, cooldown 2 = 66, 0.94x the range-2+ budget.");
            Assert.AreEqual(60, coup.Effects[0].ExecuteBonusPercent, "Retuned from 50 (the authored-kits retune).");
            Assert.AreEqual(115f, coup.Effects[0].Magnitude, "Retuned from 105 (niche pass): 115 x 1.3 / 2 = 74.75, 1.07x the range-2+ budget.");
            Assert.AreEqual(SkillTargetingCriterion.HpFraction, coup.TargetingCriterion);
            Assert.AreEqual(DamageCategory.Special, coup.Category);
        }

        [Test]
        public void Builder_MapsPassivesAndMaterials()
        {
            SkillLibraryData library = LoadLibrary();
            PassiveData data = Array.Find(library.AvatarPassives, p => p.PassiveId == "last_stand");
            PassiveSkillSO passive = ScriptableObject.CreateInstance<PassiveSkillSO>();
            _created.Add(passive);
            SkillLibraryBuilder.ApplyPassive(data, passive);

            Assert.AreEqual(PassiveTrigger.AllyBelowHpPercent, passive.Trigger);
            Assert.AreEqual(PassiveTarget.TriggeringUnit, passive.TargetScope);
            Assert.AreEqual(40, passive.HpThresholdPercent);
            Assert.AreEqual(2, passive.InternalCooldown);
            Assert.AreEqual(StatusType.Shield, passive.Effects[0].Status);

            SkillMaterialSO material = ScriptableObject.CreateInstance<SkillMaterialSO>();
            _created.Add(material);
            SkillLibraryBuilder.ApplyMaterial(library.Materials[2], material);
            Assert.AreEqual(3, material.Tier);
            Assert.AreEqual(library.Materials[2].XpValue, material.XpValue);
        }

        [Test]
        public void TierForLevel_CountsGatesStrictlyBelowTheLevel()
        {
            SkillProgressionDefinition definition = new SkillProgressionDefinition();

            Assert.AreEqual(0, SkillLibraryBuilder.TierForLevel(definition, 1));
            Assert.AreEqual(0, SkillLibraryBuilder.TierForLevel(definition, 5));
            Assert.AreEqual(1, SkillLibraryBuilder.TierForLevel(definition, 6));
            Assert.AreEqual(2, SkillLibraryBuilder.TierForLevel(definition, 11));
            Assert.AreEqual(3, SkillLibraryBuilder.TierForLevel(definition, 16));
            Assert.AreEqual(3, SkillLibraryBuilder.TierForLevel(definition, 99));
        }

        // ----------------------------------------------------------------------------------------
        // The validator: negative cases over a small valid library.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Validator_AcceptsTheMinimalLibrary()
        {
            List<string> errors = SkillLibraryValidator.Validate(MinimalLibrary(), MinimalRoster("Vanguard"));

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsDuplicateIdsAcrossSections()
        {
            SkillLibraryData library = MinimalLibrary();
            library.AvatarPassives[0].PassiveId = library.BeastSkills[0].SkillId;

            AssertRejects(library, "duplicate id");
        }

        [Test]
        public void Validator_RejectsNonSnakeCaseIds()
        {
            SkillLibraryData library = MinimalLibrary();
            library.Materials[0].MaterialId = "Shard";

            AssertRejects(library, "snake_case");
        }

        [Test]
        public void Validator_RejectsUnknownEnumNames()
        {
            SkillLibraryData library = MinimalLibrary();
            library.BeastSkills[0].Effects[0].Status = "Freeze";
            library.BeastSkills[0].Effects[0].EffectType = "ApplyStatus";

            AssertRejects(library, "'Freeze' is not a StatusType name");
        }

        [Test]
        public void Validator_RejectsUnresolvedReferences()
        {
            SkillLibraryData library = MinimalLibrary();
            library.SpeciesKits[0].LearnableSkills[4].SkillId = "no_such_skill";

            AssertRejects(library, "'no_such_skill' is not a beast skill");
        }

        [Test]
        public void Validator_RejectsTooFewLearnableSkills()
        {
            SkillLibraryData library = MinimalLibrary();
            library.SpeciesKits[0].LearnableSkills = new[] { Learn(1, "a_strike"), Learn(1, "a_bolt"), Learn(1, "a_guard") };

            AssertRejects(library, "at least 5");
        }

        [Test]
        public void Validator_RejectsLateDefaults()
        {
            SkillLibraryData library = MinimalLibrary();
            library.SpeciesKits[0].LearnableSkills[0].Level = 6;

            AssertRejects(library, "learnable by level 5");
        }

        [Test]
        public void Validator_RejectsADefaultLoadoutThatNeverMoves()
        {
            SkillLibraryData library = MinimalLibrary();
            library.SpeciesKits[0].DefaultLoadout = new[] { "a_guard", "a_wave", "a_roar" };

            AssertRejects(library, "never move");
        }

        [Test]
        public void Validator_RejectsMeleeDefaultsOnARangedSpecies()
        {
            List<string> errors = SkillLibraryValidator.Validate(MinimalLibrary(), MinimalRoster("Ranged"));

            Assert.IsTrue(errors.Exists(e => e.Contains("a_strike") && e.Contains("Ranged")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsMissingAndExtraSpeciesAgainstTheRoster()
        {
            BeastRosterData roster = MinimalRoster("Vanguard");
            roster.Species = new[] { roster.Species[0], new SpeciesData { SpeciesId = "beta" } };

            List<string> errors = SkillLibraryValidator.Validate(MinimalLibrary(), roster);
            Assert.IsTrue(errors.Exists(e => e.Contains("'beta' has no species kit")), string.Join("\n", errors));

            SkillLibraryData library = MinimalLibrary();
            library.SpeciesKits[0].SpeciesId = "gamma";
            errors = SkillLibraryValidator.Validate(library, MinimalRoster("Vanguard"));
            Assert.IsTrue(errors.Exists(e => e.Contains("not a species in the roster")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsPositionalAvatarSkillsAndAvatarKnockback()
        {
            SkillLibraryData library = MinimalLibrary();
            library.AvatarActives[0].TargetShape = "AreaBurst";
            library.AvatarActives[1].Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Knockback", Magnitude = 1f } };

            List<string> errors = SkillLibraryValidator.Validate(library);
            Assert.IsTrue(errors.Exists(e => e.Contains("avatar skills must use")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("must not be authored on the avatar")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsOutOfBandNumbers()
        {
            SkillLibraryData library = MinimalLibrary();
            library.BeastSkills[0].Effects[0].Magnitude = 900f;
            library.BeastSkills[1].Effects[0].HitCount = 0;
            library.BeastSkills[2].Effects[0].DurationTurns = 0;
            library.BeastSkills[3].Cooldown = 12;

            List<string> errors = SkillLibraryValidator.Validate(library);
            Assert.IsTrue(errors.Exists(e => e.Contains("damage power is 900")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("HitCount is 0")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("needs DurationTurns of at least 1")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("Cooldown is 12")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsCooldownReductionBelowOneAndUnknownMaterialTiers()
        {
            SkillLibraryData library = MinimalLibrary();
            library.BeastSkills[0].Progression.Tiers[0].CooldownReduction = 1;
            library.BeastSkills[1].Progression.Tiers[0].RequiredMaterialTier = 7;

            List<string> errors = SkillLibraryValidator.Validate(library);
            Assert.IsTrue(errors.Exists(e => e.Contains("below 1")), string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("RequiredMaterialTier 7")), string.Join("\n", errors));
        }

        [Test]
        public void Validator_RejectsATriggeringUnitScopeWithNoLivingTriggeringUnit()
        {
            SkillLibraryData library = MinimalLibrary();
            library.AvatarPassives[0].Trigger = "AllyDefeated";
            library.AvatarPassives[0].TargetScope = "TriggeringUnit";

            AssertRejects(library, "no living triggering unit");
        }

        [Test]
        public void Validator_RejectsAnUnknownSchemaVersion()
        {
            SkillLibraryData library = MinimalLibrary();
            library.SchemaVersion = SkillLibraryData.CurrentSchemaVersion + 1;

            AssertRejects(library, "SchemaVersion");
        }

        // ----------------------------------------------------------------------------------------
        // Helpers.
        // ----------------------------------------------------------------------------------------

        private SkillSO BuildSkill(SkillData data)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            _created.Add(skill);
            SkillLibraryBuilder.ApplySkill(data, skill);
            return skill;
        }

        private static void AssertRejects(SkillLibraryData library, string fragment)
        {
            List<string> errors = SkillLibraryValidator.Validate(library);
            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "Expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        private static bool DealsDamage(SkillData skill)
        {
            return Array.Exists(skill.Effects, e => e.EffectType == "Damage" || e.Status == "DamageOverTime");
        }

        /// <summary>
        /// The design doc's budget rule: power x hits x execute weight, plus half of each
        /// damage-over-time's total, times the shape factor, per turn of cooldown.
        /// </summary>
        private static double DamagePerTurn(SkillData skill)
        {
            double total = 0.0;
            foreach (EffectData e in skill.Effects)
            {
                if (e.EffectType == "Damage")
                {
                    total += e.Magnitude * e.HitCount * (1.0 + e.ExecuteBonusPercent / 200.0);
                }
                else if (e.Status == "DamageOverTime")
                {
                    total += e.Magnitude * e.DurationTurns * 0.5 * e.Chance / 100.0;
                }
            }

            double shape;
            switch (skill.TargetShape)
            {
                case "Line":
                    shape = 1.3;
                    break;
                case "Cross":
                    shape = 1.5;
                    break;
                case "AreaBurst":
                    shape = skill.Range <= 1 ? 1.5 : 2.0;
                    break;
                case "AllEnemies":
                    shape = 2.5;
                    break;
                default:
                    shape = 1.0;
                    break;
            }

            return total * shape / Math.Max(1, skill.Cooldown);
        }

        private static SpeciesKitData Kit(SkillLibraryData library, string speciesId)
        {
            SpeciesKitData kit = Array.Find(library.SpeciesKits, k => k.SpeciesId == speciesId);
            Assert.IsNotNull(kit, speciesId);
            return kit;
        }

        private static SkillData Skill(SkillLibraryData library, string skillId)
        {
            SkillData skill = Array.Find(library.BeastSkills, s => s.SkillId == skillId);
            Assert.IsNotNull(skill, skillId);
            return skill;
        }

        private static bool HasSkill(SkillLibraryData library, string speciesId, Predicate<SkillData> match)
        {
            foreach (LearnEntryData entry in Kit(library, speciesId).LearnableSkills)
            {
                if (match(Skill(library, entry.SkillId)))
                {
                    return true;
                }
            }

            return false;
        }

        private static EffectData FindEffect(SkillLibraryData library, string speciesId, Predicate<EffectData> match)
        {
            foreach (LearnEntryData entry in Kit(library, speciesId).LearnableSkills)
            {
                EffectData found = Array.Find(Skill(library, entry.SkillId).Effects, match);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void AssertDefaultsInclude(SkillLibraryData library, string speciesId, Predicate<EffectData> match)
        {
            bool found = Array.Exists(Kit(library, speciesId).DefaultLoadout, id => Array.Exists(Skill(library, id).Effects, match));
            Assert.IsTrue(found, speciesId + "'s default loadout is missing a signature effect.");
        }

        internal static SkillLibraryData LoadLibrary()
        {
            string path = FindLibraryFile();
            Assert.IsNotNull(path, "Could not find " + SkillLibraryData.ProjectRelativePath);

            SkillLibraryData library = JsonUtility.FromJson<SkillLibraryData>(File.ReadAllText(path));
            Assert.IsNotNull(library);
            return library;
        }

        private static string FindLibraryFile()
        {
            string[] starts = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

            foreach (string start in starts)
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string[] candidates =
                    {
                        Path.Combine(dir.FullName, SkillLibraryData.ProjectRelativePath),
                        Path.Combine(dir.FullName, "BeastCraft", SkillLibraryData.ProjectRelativePath),
                    };

                    foreach (string candidate in candidates)
                    {
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            return null;
        }

        private static BeastRosterData MinimalRoster(string stance)
        {
            return new BeastRosterData { Species = new[] { new SpeciesData { SpeciesId = "alpha", Stance = stance } } };
        }

        private static LearnEntryData Learn(int level, string id)
        {
            return new LearnEntryData { Level = level, SkillId = id };
        }

        private static ProgressionData Tiers()
        {
            return new ProgressionData
            {
                Tiers = new[]
                {
                    new TierData { ThresholdLevel = 5, RequiredMaterialTier = 1 },
                    new TierData { ThresholdLevel = 10, RequiredMaterialTier = 1 },
                },
            };
        }

        private static SkillData Skill(string id, string shape, int range, int cooldown, params EffectData[] effects)
        {
            return new SkillData
            {
                SkillId = id,
                DisplayName = id,
                Description = id,
                TargetShape = shape,
                Range = range,
                Cooldown = cooldown,
                Effects = effects,
                Progression = Tiers(),
            };
        }

        /// <summary>
        /// A small library that passes validation against <see cref="MinimalRoster"/> with a
        /// Vanguard stance: one material, five beast skills for species "alpha" (a melee strike
        /// first in its defaults), three avatar actives and one passive.
        /// </summary>
        private static SkillLibraryData MinimalLibrary()
        {
            return new SkillLibraryData
            {
                SchemaVersion = SkillLibraryData.CurrentSchemaVersion,
                Materials = new[] { new SkillMaterialData { MaterialId = "shard", DisplayName = "Shard", Description = "Shard", Tier = 1, XpValue = 100 } },
                BeastSkills = new[]
                {
                    Skill("a_strike", "SingleTarget", 1, 1, new EffectData { Magnitude = 80f }),
                    Skill("a_bolt", "SingleTarget", 3, 2, new EffectData { Magnitude = 90f, HitCount = 2 }),
                    Skill("a_guard", "Self", 0, 3, new EffectData { EffectType = "ApplyStatus", Status = "Shield", Magnitude = 40f, DurationTurns = 2 }),
                    Skill("a_wave", "AreaBurst", 2, 3, new EffectData { Magnitude = 50f }),
                    Skill("a_roar", "AreaBurst", 2, 3, new EffectData { EffectType = "ApplyStatus", Status = "Taunt", DurationTurns = 2, Chance = 80 }),
                },
                AvatarActives = new[]
                {
                    Skill("v_heal", "AllAllies", 0, 3, new EffectData { EffectType = "Heal", Magnitude = 8f }),
                    Skill("v_buff", "AllAllies", 0, 4, new EffectData { EffectType = "BuffStat", AffectedStat = "Attack", Magnitude = 10f, IsPercent = true, DurationTurns = 2 }),
                    Skill("v_hex", "AllEnemies", 0, 4, new EffectData { EffectType = "DebuffStat", AffectedStat = "Defense", Magnitude = 10f, IsPercent = true, DurationTurns = 2 }),
                },
                AvatarPassives = new[]
                {
                    new PassiveData
                    {
                        PassiveId = "p_aura",
                        DisplayName = "Aura",
                        Description = "Aura",
                        Trigger = "Aura",
                        TargetScope = "AllAllies",
                        Effects = new[] { new EffectData { EffectType = "BuffStat", AffectedStat = "CritChance", Magnitude = 5f } },
                        Progression = Tiers(),
                    },
                },
                AvatarDefaultPassives = new[] { "p_aura" },
                SpeciesKits = new[]
                {
                    new SpeciesKitData
                    {
                        SpeciesId = "alpha",
                        LearnableSkills = new[] { Learn(1, "a_strike"), Learn(1, "a_bolt"), Learn(3, "a_guard"), Learn(20, "a_wave"), Learn(40, "a_roar") },
                        DefaultLoadout = new[] { "a_strike", "a_bolt", "a_guard" },
                    },
                },
            };
        }
    }
}
