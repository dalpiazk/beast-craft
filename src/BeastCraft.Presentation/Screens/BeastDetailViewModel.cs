using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>A skill's level, tier and XP, and the gate to its next breakthrough (<see cref="SkillProgression"/>'s rules).</summary>
    public sealed class SkillProgressView
    {
        public int Level;
        public int MaxLevel;
        public int Tier;
        public int TierCount;
        public int Xp;

        /// <summary>XP from this level to the next (<see cref="SkillProgression.XpToNextLevel"/>); 0 at the maximum.</summary>
        public int XpToNext;

        /// <summary>The level the current tier stops at (<see cref="SkillProgression.LevelCap"/>).</summary>
        public int LevelCap;

        /// <summary>At its tier's cap with a breakthrough left (<see cref="SkillProgression.IsAwaitingBreakthrough"/>).</summary>
        public bool AwaitingBreakthrough;

        /// <summary>The next breakthrough's level (0 when none is left).</summary>
        public int NextGateLevel;

        /// <summary>The material tier the next breakthrough needs (0 when none is left).</summary>
        public int NextGateMaterialTier;

        /// <summary>Its power now, as a multiplier of the authored magnitudes (<see cref="SkillProgressionDefinition.GetMagnitudeMultiplier"/>).</summary>
        public double PowerMultiplier;

        /// <summary>Its power one level up (the same as <see cref="PowerMultiplier"/> at the cap).</summary>
        public double NextPowerMultiplier;

        /// <summary>E.g. "Breakthrough at Lv 5: a tier 1 material or better", "Fully broken through".</summary>
        public string GateText;
    }

    /// <summary>One of the beast's skills: equipped in a slot, known, or still to learn.</summary>
    public sealed class SkillEntryView
    {
        public string SkillId;
        public SkillSO Skill;
        public SkillCard Card;

        /// <summary>The slot it is equipped in, or -1.</summary>
        public int EquippedSlot = -1;

        /// <summary>Whether the beast knows it.</summary>
        public bool Known;

        /// <summary>The beast level its skill tome teaches it from (the species' kit); 0 when it is not in the kit.</summary>
        public int LearnLevel;

        /// <summary>Null for a skill not known yet.</summary>
        public SkillProgressView Progress;
    }

    /// <summary>A training material the player holds, and what it can do for one skill.</summary>
    public sealed class MaterialOptionView
    {
        public string MaterialId;
        public string Name;
        public int Tier;
        public int XpValue;
        public int Owned;

        /// <summary>It can feed the skill XP now (held, the skill is not at its tier's cap or the maximum).</summary>
        public bool CanTrain;

        /// <summary>It can open the skill's next breakthrough now (held, the skill is at the gate, the tier is high enough).</summary>
        public bool CanBreakthrough;

        /// <summary>Why not, when neither.</summary>
        public string Reason;
    }

    /// <summary>A piece of beast gear: its name, bonuses and level requirement.</summary>
    public sealed class GearView
    {
        public string InstanceId;
        public string GearId;
        public string Name;
        public List<string> Bonuses = new List<string>();
        public int MinimumLevel;

        /// <summary>The beast wearing it (another beast's name when it is worn elsewhere); null when free.</summary>
        public string WornBy;

        /// <summary>It can go on this beast in this slot now (<see cref="GearRules.EquipBeastGear"/>'s checks, read ahead).</summary>
        public bool Equippable;

        /// <summary>Why not, when it cannot.</summary>
        public string Reason;

        /// <summary>Worn but ignored: below its level (<see cref="StatCalculator.CollectModifiers(IEnumerable{GearSO}, int)"/>).</summary>
        public bool Inactive;
    }

    /// <summary>One gear slot: what is in it and what could be.</summary>
    public sealed class GearSlotView
    {
        public GearSlot Slot;
        public string SlotName;

        /// <summary>The gear worn there; null when empty.</summary>
        public GearView Worn;

        /// <summary>The player's gear for this slot (worn by this beast, free, or worn by another).</summary>
        public List<GearView> Options = new List<GearView>();
    }

    /// <summary>A team bond the beast can take part in, and the owned beasts it would take part with.</summary>
    public sealed class BondView
    {
        public string BondId;
        public string Name;
        public string Description;

        /// <summary>What makes a member, in words ("Vanguard beasts", "Fire, Nature or Earth beasts", "different stances").</summary>
        public string Condition;

        /// <summary>The members each tier needs.</summary>
        public List<int> TierCounts = new List<int>();

        /// <summary>The other owned beasts that count toward it.</summary>
        public List<string> Partners = new List<string>();

        /// <summary>The tier it reaches in the current party (0 when inactive or the beast is not in the party).</summary>
        public int PartyTier;
    }

    /// <summary>One cosmetic look the beast wears.</summary>
    public sealed class LookView
    {
        public string Category;
        public string Option;
    }

    /// <summary>
    /// One colour form of the beast's species (docs/design/grove.md, "Colour evolutions" — D3/D4):
    /// locked (needs <see cref="ItemHeld"/> of <see cref="ItemCount"/> <see cref="ItemDisplay"/>),
    /// owned but not worn by this beast, or worn.
    /// </summary>
    public sealed class ColourFormRow
    {
        public string ColourFormId;
        public string DisplayName;
        public string CosmeticCategoryId;
        public string CosmeticOptionId;
        public string ItemDisplay;
        public int ItemCount;
        public int ItemHeld;
        public bool Owned;
        public bool Worn;
    }

    /// <summary>
    /// The beast detail screen: one owned beast's raw stats with the gear's share broken out, the
    /// derived numbers (turns per 100 gauge ticks against the team and a level-matched average
    /// enemy, the element chart as attacker and defender, crits, the level-gap curve), its skills
    /// (the three slots, the learnable list, swaps under <see cref="BeastSkillBook"/>'s rules,
    /// training and breakthroughs under <see cref="SkillProgression"/>'s material-tier rules, each
    /// skill's card with its targeting rule in words), its gear (on and off through
    /// <see cref="GearRules"/>), its stance's behaviour, the bonds it takes part in, and the looks it
    /// wears. Every number is read from the Core rule it describes; every change autosaves.
    /// </summary>
    public sealed class BeastDetailViewModel
    {
        private static readonly StatType[] ShownStats =
        {
            StatType.HP, StatType.Attack, StatType.Defense, StatType.SpecialAttack, StatType.SpecialDefense, StatType.Speed, StatType.CritChance
        };

        private readonly GameSession _session;

        public BeastDetailViewModel(GameSession session, string beastId)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            BeastId = beastId;
            Refresh();
        }

        public string BeastId { get; }

        /// <summary>False when the beast is not in the save (the screen shows nothing).</summary>
        public bool Exists { get; private set; }

        public OwnedBeast Beast { get; private set; }
        public CreatureSpeciesSO Species { get; private set; }
        public string Name { get; private set; }
        public int Level { get; private set; }
        public float XpFraction { get; private set; }
        public string XpText { get; private set; }
        public CombatStance Stance { get; private set; }
        public IReadOnlyList<Element> Elements { get; private set; }
        public Element Element { get; private set; }
        public string ArtKey { get; private set; }
        public string Description { get; private set; }

        /// <summary>The stance's behaviour: its movement and positioning rules (the glossary's definition).</summary>
        public string StanceBehaviour { get; private set; }

        // ---- stats -----------------------------------------------------------------------------

        public List<StatLine> Stats { get; } = new List<StatLine>();

        /// <summary>The stats the beast fights with.</summary>
        public StatBlock Total { get; private set; }

        /// <summary>The worn gear's bonuses, one line per piece (the per-stat total is in <see cref="Stats"/>).</summary>
        public List<GearView> GearContributions { get; } = new List<GearView>();

        // ---- derived ---------------------------------------------------------------------------

        /// <summary>Turns per 100 gauge ticks: this beast, the rest of the party, then the level-matched average enemy.</summary>
        public List<TurnRateView> TurnRates { get; } = new List<TurnRateView>();

        public double TurnsPer100Ticks { get; private set; }

        /// <summary>The element its hits carry: its first equipped damaging skill's, else its own.</summary>
        public Element AttackElement { get; private set; }

        public List<ElementMatchView> Matchups { get; } = new List<ElementMatchView>();

        public int CritChance { get; private set; }
        public float CritMultiplier { get; private set; }
        public double ExpectedCritFactor { get; private set; }

        public List<LevelGapPoint> LevelGap { get; } = new List<LevelGapPoint>();

        // ---- skills ----------------------------------------------------------------------------

        /// <summary>The three slots in order (a slot's entry has a null <see cref="SkillEntryView.Skill"/> when it is empty).</summary>
        public List<SkillEntryView> Slots { get; } = new List<SkillEntryView>();

        /// <summary>Every skill the species' kit teaches (through skill tomes), known or not, in kit order; then anything else it knows.</summary>
        public List<SkillEntryView> Learnable { get; } = new List<SkillEntryView>();

        // ---- gear, bonds, looks ----------------------------------------------------------------

        public List<GearSlotView> Gear { get; } = new List<GearSlotView>();

        public List<BondView> Bonds { get; } = new List<BondView>();

        public List<LookView> Looks { get; } = new List<LookView>();

        /// <summary>The species' collectible colour forms (docs/design/grove.md, D3/D4), locked/owned/worn.</summary>
        public List<ColourFormRow> ColourForms { get; } = new List<ColourFormRow>();

        /// <summary>Re-reads the save and recomputes everything.</summary>
        public void Refresh()
        {
            Stats.Clear();
            GearContributions.Clear();
            TurnRates.Clear();
            Matchups.Clear();
            LevelGap.Clear();
            Slots.Clear();
            Learnable.Clear();
            Gear.Clear();
            Bonds.Clear();
            Looks.Clear();
            ColourForms.Clear();

            GameContent content = _session.Content;
            Beast = _session.Save?.FindBeast(BeastId);
            Species = Beast?.Progress == null ? null : content.Battle.GetSpecies(Beast.Progress.SpeciesId);
            Exists = Species != null;
            if (!Exists)
            {
                return;
            }

            Name = Species.DisplayName ?? Species.SpeciesId;
            Level = Beast.Progress.Level;
            int toNext = BeastProgression.XpToNextLevel(Level);
            XpFraction = Level >= BeastProgression.MaxLevel || toNext <= 0 ? 1f : Math.Min(1f, Math.Max(0f, (float)Beast.Progress.Xp / toNext));
            XpText = Level >= BeastProgression.MaxLevel ? "Max level" : Beast.Progress.Xp.ToString(CultureInfo.InvariantCulture) + " / " + toNext.ToString(CultureInfo.InvariantCulture) + " XP";
            Stance = Species.Stance;
            Elements = Species.Elements ?? new Element[0];
            Element = RosterViewModel.PrimaryElement(Species);
            ArtKey = Species.ArtKey;
            Description = Species.Description;
            StanceBehaviour = content.Glossary?.Find(Species.Stance.ToString().ToLowerInvariant())?.Definition;

            BuildStats();
            BuildSkills();
            BuildDerived();
            BuildGear();
            BuildBonds();
            BuildLooks();
            BuildColourForms();
        }

        // ------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Puts <paramref name="skillId"/> in <paramref name="slot"/> under <see cref="SkillBook.Equip"/>'s
        /// rules; a skill already in another slot swaps places with the slot's (<see cref="SkillBook.SwapSlots"/>).
        /// Autosaves on a change.
        /// </summary>
        public bool EquipSkill(int slot, string skillId, out string message)
        {
            message = null;
            if (!Exists)
            {
                message = "No such beast.";
                return false;
            }

            BeastSkillBook book = Beast.Skills;
            int current = book.IndexOfEquipped(skillId);
            if (current == slot)
            {
                message = "Already in that slot.";
                return false;
            }

            if (current >= 0)
            {
                book.SwapSlots(current, slot);
                return Changed(out message, "Swapped.");
            }

            SkillEquipResult result = book.Equip(slot, skillId);
            switch (result)
            {
                case SkillEquipResult.Equipped:
                    return Changed(out message, "Equipped.");
                case SkillEquipResult.UnknownSkill:
                    message = "Not learned yet.";
                    return false;
                case SkillEquipResult.SlotOutOfRange:
                    message = "No such slot.";
                    return false;
                default:
                    message = "Already equipped.";
                    return false;
            }
        }

        /// <summary>Empties <paramref name="slot"/> (never the last equipped skill: a beast always fights with one). Autosaves.</summary>
        public bool UnequipSkill(int slot, out string message)
        {
            message = null;
            if (!Exists || Beast.Skills.GetEquipped(slot) == null)
            {
                message = "That slot is empty.";
                return false;
            }

            int equipped = 0;
            for (int i = 0; i < Beast.Skills.SlotCount; i++)
            {
                equipped += Beast.Skills.GetEquipped(i) != null ? 1 : 0;
            }

            if (equipped <= 1)
            {
                message = "A beast needs at least one skill.";
                return false;
            }

            Beast.Skills.Unequip(slot);
            return Changed(out message, "Unequipped.");
        }

        /// <summary>
        /// Feeds one <paramref name="materialId"/> to <paramref name="skillId"/>: its XP value through
        /// <see cref="SkillProgression.ApplyMaterial"/>, the material spent. Refused at the tier's cap
        /// (a breakthrough is due: XP there would only bank) and at the maximum level. Autosaves.
        /// </summary>
        public bool Train(string skillId, string materialId, out string message)
        {
            MaterialOptionView option = MaterialFor(skillId, materialId, out SkillProgress progress, out SkillSO skill, out SkillMaterialSO material);
            if (option == null)
            {
                message = "Nothing to train.";
                return false;
            }

            if (!option.CanTrain)
            {
                message = option.Reason ?? "It cannot be trained with that.";
                return false;
            }

            if (!_session.Save.Materials.TryConsume(materialId, 1))
            {
                message = "None left.";
                return false;
            }

            int levels = SkillProgression.ApplyMaterial(progress, skill.Progression, material);
            return Changed(out message, levels > 0 ? "Up " + levels + (levels == 1 ? " level" : " levels") + ": Lv " + progress.Level + "." : "+" + material.XpValue + " XP.");
        }

        /// <summary>
        /// Opens <paramref name="skillId"/>'s next breakthrough with one <paramref name="materialId"/>
        /// (<see cref="SkillProgression.TryBreakthrough"/>: at the gate, a material of the gate's tier
        /// or better), spending it on success. Autosaves.
        /// </summary>
        public bool Breakthrough(string skillId, string materialId, out string message)
        {
            MaterialOptionView option = MaterialFor(skillId, materialId, out SkillProgress progress, out SkillSO skill, out SkillMaterialSO material);
            if (option == null)
            {
                message = "Nothing to break through.";
                return false;
            }

            if (!option.CanBreakthrough)
            {
                message = option.Reason ?? "Not now.";
                return false;
            }

            SkillBreakthroughResult result = SkillProgression.TryBreakthrough(progress, skill.Progression, material);
            if (result != SkillBreakthroughResult.Success)
            {
                message = result.ToString();
                return false;
            }

            _session.Save.Materials.TryConsume(materialId, 1);
            return Changed(out message, "Breakthrough! Tier " + progress.Tier + ": it can grow to Lv " + SkillProgression.LevelCap(skill.Progression, progress.Tier) + ".");
        }

        /// <summary>Wears gear <paramref name="instanceId"/> in <paramref name="slot"/> (<see cref="GearRules.EquipBeastGear"/>). Autosaves.</summary>
        public bool EquipGear(GearSlot slot, string instanceId, out string message)
        {
            GearEquipResult result = GearRules.EquipBeastGear(_session.Save, BeastId, slot, instanceId, _session.Content.Battle);
            if (result == GearEquipResult.Equipped)
            {
                return Changed(out message, "Equipped.");
            }

            message = EquipReason(result, 0);
            return false;
        }

        /// <summary>Takes the gear in <paramref name="slot"/> off (<see cref="GearRules.UnequipBeastGear"/>). Autosaves.</summary>
        public bool UnequipGear(GearSlot slot, out string message)
        {
            if (!GearRules.UnequipBeastGear(_session.Save, BeastId, slot))
            {
                message = "Nothing worn there.";
                return false;
            }

            return Changed(out message, "Taken off.");
        }

        /// <summary>The materials the player holds (every tier), and what each can do for <paramref name="skillId"/> now.</summary>
        public List<MaterialOptionView> Materials(string skillId)
        {
            List<MaterialOptionView> options = new List<MaterialOptionView>();
            SkillProgress progress = Exists ? Beast.Skills.GetProgress(skillId) : null;
            SkillSO skill = _session.Content.Battle.GetSkill(skillId);
            foreach (SkillMaterialSO material in MaterialDefinitions(_session.Content))
            {
                options.Add(Option(material, progress, skill));
            }

            return options;
        }

        /// <summary>The skill training materials, as the Core's <see cref="SkillMaterialSO"/>, in the library's order.</summary>
        public static List<SkillMaterialSO> MaterialDefinitions(GameContent content)
        {
            List<SkillMaterialSO> materials = new List<SkillMaterialSO>();
            foreach (SkillMaterialData data in content?.SkillLibrary?.Materials ?? new SkillMaterialData[0])
            {
                if (data == null || string.IsNullOrEmpty(data.MaterialId))
                {
                    continue;
                }

                SkillMaterialSO material = new SkillMaterialSO();
                SkillLibraryBuilder.ApplyMaterial(data, material);
                materials.Add(material);
            }

            return materials;
        }

        /// <summary>A skill's progress under <paramref name="definition"/>, as the screen shows it.</summary>
        public static SkillProgressView ProgressOf(SkillProgress progress, SkillProgressionDefinition definition)
        {
            if (progress == null)
            {
                return null;
            }

            SkillProgressionDefinition def = definition ?? new SkillProgressionDefinition();
            int tier = def.ClampTier(progress.Tier);
            int level = def.ClampLevel(progress.Level);
            int max = def.EffectiveMaxLevel;
            SkillTierDefinition gate = tier < def.TierCount ? def.GetTier(tier) : null;
            SkillProgressView view = new SkillProgressView
            {
                Level = level,
                MaxLevel = max,
                Tier = tier,
                TierCount = def.TierCount,
                Xp = progress.Xp,
                XpToNext = level >= max ? 0 : SkillProgression.XpToNextLevel(level),
                LevelCap = SkillProgression.LevelCap(def, tier),
                AwaitingBreakthrough = SkillProgression.IsAwaitingBreakthrough(progress, def),
                NextGateLevel = gate == null ? 0 : def.ClampLevel(gate.ThresholdLevel),
                NextGateMaterialTier = gate == null ? 0 : gate.RequiredMaterialTier,
                PowerMultiplier = def.GetMagnitudeMultiplier(level),
                NextPowerMultiplier = def.GetMagnitudeMultiplier(Math.Min(max, level + 1))
            };
            view.GateText = gate == null
                ? "Fully broken through: grows to Lv " + max
                : (view.AwaitingBreakthrough ? "Breakthrough due" : "Next breakthrough at Lv " + view.NextGateLevel) + ": a tier " + view.NextGateMaterialTier + " material or better";
            return view;
        }

        // ------------------------------------------------------------------------------------------
        // Building
        // ------------------------------------------------------------------------------------------

        private void BuildStats()
        {
            List<GearSO> worn = WornGear(out List<GearView> contributions);
            StatBlock baseStats = StatCalculator.GetBaseStatsAtLevel(Species, Level);
            StatBlock total = StatCalculator.ComputeStats(Species, Level, worn);
            Total = total;
            foreach (StatType stat in ShownStats)
            {
                Stats.Add(new StatLine
                {
                    Stat = stat,
                    Label = DerivedStats.ShortName(stat),
                    Base = baseStats.GetStat(stat),
                    Gear = total.GetStat(stat) - baseStats.GetStat(stat),
                    Total = total.GetStat(stat)
                });
            }

            GearContributions.AddRange(contributions);
        }

        private void BuildDerived()
        {
            GameContent content = _session.Content;
            TurnsPer100Ticks = DerivedStats.TurnsPer100Ticks(Total.Speed);
            TurnRates.Add(Rate(Name, Total.Speed, true, false));
            foreach (string id in _session.Party())
            {
                if (id == BeastId)
                {
                    continue;
                }

                OwnedBeast other = _session.Save.FindBeast(id);
                CreatureSpeciesSO species = other?.Progress == null ? null : content.Battle.GetSpecies(other.Progress.SpeciesId);
                if (species != null)
                {
                    StatBlock stats = StatCalculator.ComputeStats(species, other.Progress.Level, ResolveGear(_session, other));
                    TurnRates.Add(Rate(species.DisplayName ?? species.SpeciesId, stats.Speed, false, false));
                }
            }

            double enemyTurns = DerivedStats.AverageEnemyTurns(content.Enemies, Level, out int enemySpeed);
            if (enemyTurns > 0.0)
            {
                TurnRateView enemy = Rate("Average enemy, Lv " + Level, enemySpeed, false, true);
                enemy.TurnsPer100Ticks = enemyTurns;
                enemy.Relative = TurnsPer100Ticks > 0.0 ? enemyTurns / TurnsPer100Ticks : 0.0;
                TurnRates.Add(enemy);
            }

            AttackElement = EncounterInsightView.AttackElement(content, Beast, Species);

            Matchups.AddRange(DerivedStats.ElementMatchups(AttackElement, Elements));
            CritChance = DerivedStats.CritChance(Total.CritChance);
            CritMultiplier = DerivedStats.CritMultiplier;
            ExpectedCritFactor = DerivedStats.ExpectedCritFactor(Total.CritChance);
            LevelGap.AddRange(DerivedStats.LevelGapCurve(Level));
        }

        private TurnRateView Rate(string name, int speed, bool self, bool enemy)
        {
            double turns = DerivedStats.TurnsPer100Ticks(speed);
            double mine = DerivedStats.TurnsPer100Ticks(Total.Speed);
            return new TurnRateView
            {
                Name = name,
                Speed = speed,
                FillRate = TurnManager.FillRateForSpeed(speed),
                TurnsPer100Ticks = turns,
                Relative = mine > 0.0 ? turns / mine : 0.0,
                IsSelf = self,
                IsEnemy = enemy
            };
        }

        private void BuildSkills()
        {
            GameContent content = _session.Content;
            BeastSkillBook book = Beast.Skills;
            for (int slot = 0; slot < BeastSkillBook.EquipSlotCount; slot++)
            {
                string id = book.GetEquipped(slot);
                SkillSO skill = id == null ? null : content.Battle.GetSkill(id);
                Slots.Add(Entry(id, skill, slot));
            }

            // The species' kit (the skill library's SpeciesKits): what its skill tomes teach, and from what level.
            HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
            SpeciesKitData kit = Array.Find(content.SkillLibrary?.SpeciesKits ?? new SpeciesKitData[0], k => k != null && k.SpeciesId == Species.SpeciesId);
            foreach (LearnEntryData learn in kit?.LearnableSkills ?? new LearnEntryData[0])
            {
                SkillSO skill = learn == null ? null : content.Battle.GetSkill(learn.SkillId);
                if (skill == null || !listed.Add(skill.SkillId))
                {
                    continue;
                }

                SkillEntryView entry = Entry(skill.SkillId, skill, book.IndexOfEquipped(skill.SkillId));
                entry.LearnLevel = learn.Level;
                Learnable.Add(entry);
            }

            // Anything known outside the learn table (a tome, an old save) is listed too.
            foreach (SkillProgress known in book.Known ?? new List<SkillProgress>())
            {
                if (known != null && listed.Add(known.SkillId))
                {
                    SkillSO skill = content.Battle.GetSkill(known.SkillId);
                    if (skill != null)
                    {
                        Learnable.Add(Entry(known.SkillId, skill, book.IndexOfEquipped(known.SkillId)));
                    }
                }
            }
        }

        private SkillEntryView Entry(string skillId, SkillSO skill, int slot)
        {
            SkillProgress progress = skillId == null ? null : Beast.Skills.GetProgress(skillId);
            return new SkillEntryView
            {
                SkillId = skillId,
                Skill = skill,
                Card = skill == null ? null : SkillCard.Of(skill, _session.Content.Glossary),
                EquippedSlot = slot,
                Known = progress != null,
                Progress = ProgressOf(progress, skill?.Progression)
            };
        }

        private MaterialOptionView MaterialFor(string skillId, string materialId, out SkillProgress progress, out SkillSO skill, out SkillMaterialSO material)
        {
            progress = Exists ? Beast.Skills.GetProgress(skillId) : null;
            skill = _session.Content.Battle.GetSkill(skillId);
            material = MaterialDefinitions(_session.Content).Find(m => m.MaterialId == materialId);
            if (progress == null || skill == null || material == null)
            {
                return null;
            }

            return Option(material, progress, skill);
        }

        private MaterialOptionView Option(SkillMaterialSO material, SkillProgress progress, SkillSO skill)
        {
            int owned = _session.Save.Materials.GetCount(material.MaterialId);
            MaterialOptionView option = new MaterialOptionView
            {
                MaterialId = material.MaterialId,
                Name = material.DisplayName ?? material.MaterialId,
                Tier = material.Tier,
                XpValue = material.XpValue,
                Owned = owned
            };
            if (progress == null || skill == null)
            {
                option.Reason = "Not learned yet.";
                return option;
            }

            SkillProgressView view = ProgressOf(progress, skill.Progression);
            if (owned <= 0)
            {
                option.Reason = "None held.";
            }
            else if (view.AwaitingBreakthrough)
            {
                option.CanBreakthrough = material.Tier >= view.NextGateMaterialTier;
                option.Reason = option.CanBreakthrough ? null : "The breakthrough needs a tier " + view.NextGateMaterialTier + " material or better.";
            }
            else if (view.Level >= view.MaxLevel)
            {
                option.Reason = "Already at the maximum level.";
            }
            else
            {
                option.CanTrain = true;
            }

            return option;
        }

        private void BuildGear()
        {
            GameContent content = _session.Content;
            PlayerSave save = _session.Save;
            foreach (GearSlot slot in (GearSlot[])Enum.GetValues(typeof(GearSlot)))
            {
                GearSlotView view = new GearSlotView { Slot = slot, SlotName = SlotName(slot) };
                string wornId = GearRules.GetSlot(Beast.EquippedGear, (int)slot);
                foreach (OwnedGear owned in save.Gear.BeastGear)
                {
                    GearSO gear = content.Battle.GetGear(owned.GearId);
                    if (gear == null || gear.Slot != slot)
                    {
                        continue;
                    }

                    GearView option = View(owned, gear);
                    string holder = GearRules.FindBeastGearHolder(save, owned.InstanceId);
                    if (holder == BeastId)
                    {
                        option.WornBy = Name;
                        option.Reason = "Worn.";
                    }
                    else if (holder != null)
                    {
                        option.WornBy = _session.BeastName(save.FindBeast(holder));
                        option.Reason = EquipReason(GearEquipResult.EquippedElsewhere, 0, option.WornBy);
                    }
                    else if (Level < gear.MinimumLevel)
                    {
                        option.Reason = EquipReason(GearEquipResult.LevelTooLow, gear.MinimumLevel);
                    }
                    else
                    {
                        option.Equippable = true;
                    }

                    if (owned.InstanceId == wornId)
                    {
                        option.Inactive = Level < gear.MinimumLevel;
                        view.Worn = option;
                    }

                    view.Options.Add(option);
                }

                Gear.Add(view);
            }
        }

        private void BuildBonds()
        {
            GameContent content = _session.Content;
            List<TeamBondMember> self = new List<TeamBondMember> { TeamBondMember.FromSpecies(Species) };
            List<string> party = _session.Party();
            List<CreatureSpeciesSO> partySpecies = new List<CreatureSpeciesSO>();
            foreach (string id in party)
            {
                OwnedBeast member = _session.Save.FindBeast(id);
                CreatureSpeciesSO species = member?.Progress == null ? null : content.Battle.GetSpecies(member.Progress.SpeciesId);
                if (species != null)
                {
                    partySpecies.Add(species);
                }
            }

            foreach (TeamBondSO bond in content.Battle.TeamBonds ?? new List<TeamBondSO>())
            {
                List<int> matched = new List<int>();
                if (bond == null || TeamBondResolver.Count(bond, self, matched) <= 0 || matched.Count == 0)
                {
                    continue;
                }

                BondView view = new BondView
                {
                    BondId = bond.BondId,
                    Name = bond.DisplayName ?? bond.BondId,
                    Description = bond.Description,
                    Condition = ConditionText(bond)
                };
                foreach (TeamBondTier tier in bond.Tiers ?? new List<TeamBondTier>())
                {
                    if (tier != null)
                    {
                        view.TierCounts.Add(tier.MinCount);
                    }
                }

                foreach (OwnedBeast other in _session.Save.Beasts)
                {
                    CreatureSpeciesSO species = other == null || other.BeastId == BeastId ? null : content.Battle.GetSpecies(other.Progress?.SpeciesId);
                    if (species == null)
                    {
                        continue;
                    }

                    List<TeamBondMember> pair = new List<TeamBondMember> { TeamBondMember.FromSpecies(Species), TeamBondMember.FromSpecies(species) };
                    List<int> members = new List<int>();
                    int count = TeamBondResolver.Count(bond, pair, members);
                    bool partner = bond.Condition == TeamBondCondition.DistinctStances ? count >= 2 : members.Contains(1) && count >= 2;
                    if (partner)
                    {
                        view.Partners.Add(species.DisplayName ?? species.SpeciesId);
                    }
                }

                if (party.Contains(BeastId))
                {
                    view.PartyTier = TeamBondResolver.TierFor(bond, TeamBondResolver.Count(bond, TeamBondResolver.MembersOf(partySpecies), null));
                }

                Bonds.Add(view);
            }
        }

        private void BuildLooks()
        {
            CosmeticLibrary library = _session.Content.Economy?.Cosmetics;
            if (library == null)
            {
                return;
            }

            foreach (CosmeticCategory category in library.Categories)
            {
                if (category == null || category.Scope != Species.SpeciesId)
                {
                    continue;
                }

                CosmeticOption worn = CosmeticRules.Worn(_session.Save, BeastId, category.CategoryId, library);
                Looks.Add(new LookView { Category = category.DisplayName, Option = worn?.DisplayName ?? "Default" });
            }
        }

        private void BuildColourForms()
        {
            GroveLibrary grove = _session.Content.GroveLibrary;
            GardenLibrary garden = _session.Content.GardenLibrary;
            CosmeticLibrary cosmetics = _session.Content.Economy?.Cosmetics;
            if (grove == null || cosmetics == null)
            {
                return;
            }

            foreach (ColourFormData form in grove.ColourFormsFor(Species.SpeciesId))
            {
                if (form == null)
                {
                    continue;
                }

                string key = CosmeticCollection.Key(form.CosmeticCategoryId, form.CosmeticOptionId);
                bool owned = _session.Save.Cosmetics != null && _session.Save.Cosmetics.Has(key);
                CosmeticOption worn = CosmeticRules.Worn(_session.Save, BeastId, form.CosmeticCategoryId, cosmetics);
                ColourForms.Add(new ColourFormRow
                {
                    ColourFormId = form.ColourFormId,
                    DisplayName = form.DisplayName ?? form.ColourFormId,
                    CosmeticCategoryId = form.CosmeticCategoryId,
                    CosmeticOptionId = form.CosmeticOptionId,
                    ItemDisplay = garden.Variety(form.ItemId)?.DisplayName ?? GardenViewModel.Humanize(form.ItemId),
                    ItemCount = form.ItemCount,
                    ItemHeld = _session.Save.Grove?.Items.GetCount(form.ItemId) ?? 0,
                    Owned = owned,
                    Worn = owned && worn != null && worn.OptionId == form.CosmeticOptionId
                });
            }
        }

        /// <summary>
        /// Spends the colour form's Grove item to unlock it account-wide
        /// (<see cref="GroveRules.TryUnlockColourForm"/>) — already owned still spends the item but
        /// grants look tokens instead of wasting it. Autosaves and refreshes on success.
        /// </summary>
        public ColourFormResult UnlockColourForm(string colourFormId)
        {
            ColourFormResult result = GroveRules.TryUnlockColourForm(_session.Save, _session.Content.GroveLibrary, _session.Content.Economy?.Cosmetics, colourFormId);
            if (result.Success)
            {
                _session.Autosave(AutosaveReason.BeastEdit);
            }

            Refresh();
            return result;
        }

        /// <summary>Wears an already-owned colour form on this beast (<see cref="CosmeticRules.TrySetOption"/>). Autosaves and refreshes on success.</summary>
        public CosmeticResult WearColourForm(string colourFormId)
        {
            ColourFormRow row = ColourForms.Find(c => c.ColourFormId == colourFormId);
            if (row == null)
            {
                return CosmeticResult.UnknownOption;
            }

            CosmeticResult result = CosmeticRules.TrySetOption(_session.Save, BeastId, row.CosmeticCategoryId, row.CosmeticOptionId, _session.Content.Economy?.Cosmetics);
            if (result == CosmeticResult.Set)
            {
                _session.Autosave(AutosaveReason.BeastEdit);
            }

            Refresh();
            return result;
        }

        /// <summary>Switches this beast back to its species' free natural colour (the category's <c>"natural"</c> default option).</summary>
        public CosmeticResult WearNaturalColour(string categoryId)
        {
            CosmeticResult result = CosmeticRules.TrySetOption(_session.Save, BeastId, categoryId, "natural", _session.Content.Economy?.Cosmetics);
            if (result == CosmeticResult.Set)
            {
                _session.Autosave(AutosaveReason.BeastEdit);
            }

            Refresh();
            return result;
        }

        private List<GearSO> WornGear(out List<GearView> contributions)
        {
            contributions = new List<GearView>();
            List<GearSO> worn = new List<GearSO>();
            for (int slot = 0; Beast.EquippedGear != null && slot < Beast.EquippedGear.Length; slot++)
            {
                string instanceId = GearRules.GetSlot(Beast.EquippedGear, slot);
                OwnedGear owned = instanceId == null ? null : _session.Save.Gear.FindBeastGear(instanceId);
                GearSO gear = owned == null ? null : _session.Content.Battle.GetGear(owned.GearId);
                if (gear == null || (int)gear.Slot != slot)
                {
                    continue;
                }

                worn.Add(gear);
                GearView view = View(owned, gear);
                view.Inactive = Level < gear.MinimumLevel;
                contributions.Add(view);
            }

            return worn;
        }

        /// <summary>The gear <paramref name="beast"/> wears, resolved leniently (unknown or misplaced pieces are skipped).</summary>
        public static List<GearSO> ResolveGear(GameSession session, OwnedBeast beast)
        {
            List<GearSO> worn = new List<GearSO>();
            for (int slot = 0; beast?.EquippedGear != null && slot < beast.EquippedGear.Length; slot++)
            {
                string instanceId = GearRules.GetSlot(beast.EquippedGear, slot);
                OwnedGear owned = instanceId == null ? null : session.Save.Gear.FindBeastGear(instanceId);
                GearSO gear = owned == null ? null : session.Content.Battle.GetGear(owned.GearId);
                if (gear != null && (int)gear.Slot == slot)
                {
                    worn.Add(gear);
                }
            }

            return worn;
        }

        private static GearView View(OwnedGear owned, GearSO gear)
        {
            GearView view = new GearView { InstanceId = owned.InstanceId, GearId = gear.GearId, Name = gear.DisplayName ?? gear.GearId, MinimumLevel = gear.MinimumLevel };
            foreach (StatModifier modifier in gear.Modifiers ?? new List<StatModifier>())
            {
                if (modifier == null)
                {
                    continue;
                }

                if (modifier.FlatBonus != 0)
                {
                    view.Bonuses.Add((modifier.FlatBonus > 0 ? "+" : string.Empty) + modifier.FlatBonus.ToString(CultureInfo.InvariantCulture) + " " + DerivedStats.ShortName(modifier.Stat));
                }

                if (modifier.PercentBonus != 0f)
                {
                    view.Bonuses.Add((modifier.PercentBonus > 0f ? "+" : string.Empty) + (modifier.PercentBonus * 100f).ToString("0.##", CultureInfo.InvariantCulture) + "% " +
                                     DerivedStats.ShortName(modifier.Stat));
                }
            }

            return view;
        }

        private bool Changed(out string message, string text)
        {
            message = text;
            _session.Autosave(AutosaveReason.BeastEdit);
            Refresh();
            return true;
        }

        /// <summary>A gear slot's name.</summary>
        public static string SlotName(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.WeaponOrCore:
                    return "Weapon / core";
                case GearSlot.ArmorOrShell:
                    return "Armour / shell";
                default:
                    return "Accessory";
            }
        }

        private static string EquipReason(GearEquipResult result, int minimumLevel, string holder = null)
        {
            switch (result)
            {
                case GearEquipResult.LevelTooLow:
                    return minimumLevel > 0 ? "Needs Lv " + minimumLevel + "." : "The beast's level is too low.";
                case GearEquipResult.EquippedElsewhere:
                    return holder != null ? "Worn by " + holder + "." : "Another beast wears it.";
                case GearEquipResult.SlotMismatch:
                    return "It does not go in that slot.";
                case GearEquipResult.UnknownInstance:
                    return "You do not have it.";
                case GearEquipResult.UnknownGear:
                    return "Unknown gear.";
                default:
                    return "No such beast.";
            }
        }

        /// <summary>A bond's membership rule in words.</summary>
        public static string ConditionText(TeamBondSO bond)
        {
            switch (bond.Condition)
            {
                case TeamBondCondition.Stance:
                    return bond.Stance + " beasts";
                case TeamBondCondition.Elements:
                    List<string> elements = (bond.Elements ?? new List<Element>()).ConvertAll(e => e.ToString());
                    return (elements.Count <= 1 ? string.Join(string.Empty, elements) : string.Join(", ", elements.GetRange(0, elements.Count - 1)) + " or " + elements[elements.Count - 1]) +
                           " beasts (one per element)";
                case TeamBondCondition.Species:
                    return "Particular beasts";
                default:
                    return "Beasts of different stances";
            }
        }
    }
}
