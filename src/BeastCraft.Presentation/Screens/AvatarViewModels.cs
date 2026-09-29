using System;
using System.Collections.Generic;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>The Avatar screen's inner tabs (<see cref="AvatarHubViewModel"/>), the same shape as <see cref="GroveTab"/>.</summary>
    public enum AvatarTab
    {
        Overview = 0,
        Skills = 1,
        Gear = 2,
        Wardrobe = 3
    }

    /// <summary>One stat axis, base (<see cref="CampaignAvatar.Profile"/> at the avatar's level) and with worn gear.</summary>
    public sealed class AvatarStatRow
    {
        public string Name;
        public int Base;
        public int Total;
    }

    /// <summary>The Overview tab: level, XP, titled name and the base/total stat block (the Theorycrafter lens).</summary>
    public sealed class AvatarOverviewViewModel
    {
        private readonly GameSession _session;

        public AvatarOverviewViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public string DisplayName { get; private set; }

        public int Level { get; private set; }

        public int Xp { get; private set; }

        public int XpToNext { get; private set; }

        public List<AvatarStatRow> Stats { get; } = new List<AvatarStatRow>();

        public void Refresh()
        {
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            AvatarProgress progress = save?.Avatar;
            Level = progress?.Level ?? 1;
            Xp = progress?.Xp ?? 0;
            XpToNext = AvatarProgression.XpToNextLevel(Level);
            DisplayName = AchievementsViewModel.TitledName(save, _session.Content.Achievements?.Library, CampaignAvatar.DisplayName);
            Stats.Clear();
            AvatarStatsSO profile = CampaignAvatar.Profile(_session.Content);
            StatBlock baseStats = profile.GetStatsAtLevel(Level);
            StatBlock total = StatCalculator.ComputeStats(baseStats, StatCalculator.CollectModifiers(AvatarGear.Worn(_session)));
            AddRow("HP", baseStats.Hp, total.Hp);
            AddRow("Attack", baseStats.Attack, total.Attack);
            AddRow("Defense", baseStats.Defense, total.Defense);
            AddRow("Special Attack", baseStats.SpecialAttack, total.SpecialAttack);
            AddRow("Special Defense", baseStats.SpecialDefense, total.SpecialDefense);
            AddRow("Speed", baseStats.Speed, total.Speed);
        }

        private void AddRow(string name, int baseValue, int totalValue)
        {
            Stats.Add(new AvatarStatRow { Name = name, Base = baseValue, Total = totalValue });
        }
    }

    /// <summary>Resolves the avatar's currently worn gear, leniently (unknown or misplaced pieces are skipped) — mirrors <see cref="BeastDetailViewModel.ResolveGear"/>.</summary>
    internal static class AvatarGear
    {
        public static List<AvatarGearSO> Worn(GameSession session)
        {
            List<AvatarGearSO> worn = new List<AvatarGearSO>();
            PlayerSave save = session.Save;
            string[] equipped = save?.AvatarEquippedGear;
            for (int slot = 0; equipped != null && slot < equipped.Length; slot++)
            {
                string instanceId = GearRules.GetSlot(equipped, slot);
                OwnedGear owned = instanceId == null ? null : save.Gear.FindAvatarGear(instanceId);
                AvatarGearSO gear = owned == null ? null : session.Content.Battle.GetAvatarGear(owned.GearId);
                if (gear != null && (int)gear.Slot == slot)
                {
                    worn.Add(gear);
                }
            }

            return worn;
        }
    }

    /// <summary>One equipped (or empty) active/passive slot.</summary>
    public sealed class AvatarSkillSlotRow
    {
        public int Slot;
        public string SkillId;

        /// <summary>The full card, for an active slot (<see cref="SkillCard.Of"/>); null when empty or a passive.</summary>
        public SkillCard Card;

        /// <summary>A passive slot's display fields (there is no <see cref="SkillCard"/> for a <see cref="PassiveSkillSO"/>); null when empty or an active.</summary>
        public string PassiveName;
        public string PassiveTrigger;
        public string PassiveTarget;
        public string PassiveDescription;
        public List<string> PassivePower = new List<string>();
    }

    /// <summary>One skill or passive the avatar knows, and whether (and where) it is currently equipped.</summary>
    public sealed class AvatarKnownRow
    {
        public string Id;
        public string Name;
        public int EquippedSlot = -1;
    }

    /// <summary>
    /// The Skills tab: the avatar's three equipped actives and three equipped passives
    /// (<see cref="AvatarSkillBook"/>), each built into a <see cref="SkillCard"/> (actives — the same
    /// card the beast detail screen and the skill strip use, since avatar actives and beast skills
    /// share one <see cref="SkillSO"/> id space) or a small passive summary. Equip/swap only: avatar
    /// actives and passives are learned from the Trader, not from this screen.
    /// </summary>
    public sealed class AvatarSkillsViewModel
    {
        private readonly GameSession _session;

        public AvatarSkillsViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<AvatarSkillSlotRow> ActiveSlots { get; } = new List<AvatarSkillSlotRow>();
        public List<AvatarSkillSlotRow> PassiveSlots { get; } = new List<AvatarSkillSlotRow>();
        public List<AvatarKnownRow> KnownActives { get; } = new List<AvatarKnownRow>();
        public List<AvatarKnownRow> KnownPassives { get; } = new List<AvatarKnownRow>();

        public void Refresh()
        {
            ActiveSlots.Clear();
            PassiveSlots.Clear();
            KnownActives.Clear();
            KnownPassives.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            AvatarSkillBook book = save?.AvatarSkills;
            GameContent content = _session.Content;

            for (int i = 0; i < AvatarSkillBook.ActiveSlotCount; i++)
            {
                string id = book?.Actives?.GetEquipped(i);
                AvatarSkillSlotRow row = new AvatarSkillSlotRow { Slot = i, SkillId = id };
                SkillSO skill = string.IsNullOrEmpty(id) ? null : content.Battle.GetSkill(id);
                if (skill != null)
                {
                    row.Card = SkillCard.Of(skill, content.Glossary);
                }

                ActiveSlots.Add(row);
            }

            for (int i = 0; i < AvatarSkillBook.PassiveSlotCount; i++)
            {
                string id = book?.Passives?.GetEquipped(i);
                AvatarSkillSlotRow row = new AvatarSkillSlotRow { Slot = i, SkillId = id };
                PassiveSkillSO passive = string.IsNullOrEmpty(id) ? null : content.Battle.GetPassive(id);
                if (passive != null)
                {
                    row.PassiveName = passive.DisplayName ?? passive.PassiveId;
                    row.PassiveTrigger = TriggerText(passive.Trigger);
                    row.PassiveTarget = TargetText(passive.TargetScope);
                    row.PassiveDescription = passive.Description;
                    foreach (SkillEffect effect in passive.Effects ?? new List<SkillEffect>())
                    {
                        if (effect != null)
                        {
                            row.PassivePower.Add(SkillCard.PowerLine(effect, passive.Element, passive.Category));
                        }
                    }
                }

                PassiveSlots.Add(row);
            }

            foreach (SkillProgress progress in book?.Actives?.Known ?? new List<SkillProgress>())
            {
                SkillSO skill = progress == null ? null : content.Battle.GetSkill(progress.SkillId);
                if (skill != null)
                {
                    KnownActives.Add(new AvatarKnownRow { Id = skill.SkillId, Name = skill.DisplayName ?? skill.SkillId, EquippedSlot = book.Actives.IndexOfEquipped(skill.SkillId) });
                }
            }

            foreach (SkillProgress progress in book?.Passives?.Known ?? new List<SkillProgress>())
            {
                PassiveSkillSO passive = progress == null ? null : content.Battle.GetPassive(progress.SkillId);
                if (passive != null)
                {
                    KnownPassives.Add(new AvatarKnownRow
                    {
                        Id = passive.PassiveId,
                        Name = passive.DisplayName ?? passive.PassiveId,
                        EquippedSlot = book.Passives.IndexOfEquipped(passive.PassiveId)
                    });
                }
            }
        }

        /// <summary>Puts <paramref name="skillId"/> into active slot <paramref name="slot"/> (swapping if it is already equipped elsewhere). Autosaves.</summary>
        public bool EquipActive(int slot, string skillId, out string message)
        {
            return Equip(_session.Save?.AvatarSkills?.Actives, slot, skillId, out message);
        }

        /// <summary>Empties active slot <paramref name="slot"/> (never the last one: the avatar always fights with one active). Autosaves.</summary>
        public bool UnequipActive(int slot, out string message)
        {
            return Unequip(_session.Save?.AvatarSkills?.Actives, slot, "The avatar needs at least one active skill.", out message);
        }

        /// <summary>Puts <paramref name="passiveId"/> into passive slot <paramref name="slot"/> (swapping if it is already equipped elsewhere). Autosaves.</summary>
        public bool EquipPassive(int slot, string passiveId, out string message)
        {
            return Equip(_session.Save?.AvatarSkills?.Passives, slot, passiveId, out message);
        }

        /// <summary>Empties passive slot <paramref name="slot"/> (never the last one). Autosaves.</summary>
        public bool UnequipPassive(int slot, out string message)
        {
            return Unequip(_session.Save?.AvatarSkills?.Passives, slot, "The avatar needs at least one passive.", out message);
        }

        private bool Equip(SkillBook book, int slot, string id, out string message)
        {
            message = null;
            if (book == null)
            {
                message = "No avatar.";
                return false;
            }

            int current = book.IndexOfEquipped(id);
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

            SkillEquipResult result = book.Equip(slot, id);
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

        private bool Unequip(SkillBook book, int slot, string guardMessage, out string message)
        {
            message = null;
            if (book == null || book.GetEquipped(slot) == null)
            {
                message = "That slot is empty.";
                return false;
            }

            int equipped = 0;
            for (int i = 0; i < book.SlotCount; i++)
            {
                equipped += book.GetEquipped(i) != null ? 1 : 0;
            }

            if (equipped <= 1)
            {
                message = guardMessage;
                return false;
            }

            book.Unequip(slot);
            return Changed(out message, "Unequipped.");
        }

        private bool Changed(out string message, string text)
        {
            message = text;
            _session.Autosave(AutosaveReason.PlayerEdit);
            Refresh();
            return true;
        }

        private static string TriggerText(PassiveTrigger trigger)
        {
            switch (trigger)
            {
                case PassiveTrigger.BattleStart:
                    return "At battle start";
                case PassiveTrigger.EnemyDefeated:
                    return "When an enemy falls";
                case PassiveTrigger.AllyDefeated:
                    return "When an ally falls";
                case PassiveTrigger.AllyCrit:
                    return "When an ally crits";
                case PassiveTrigger.AllyTurnStart:
                    return "At the start of an ally's turn";
                case PassiveTrigger.AllyBelowHpPercent:
                    return "While an ally is low on HP";
                default:
                    return "Always active";
            }
        }

        private static string TargetText(PassiveTarget target)
        {
            switch (target)
            {
                case PassiveTarget.TriggeringUnit:
                    return "Whoever triggers it";
                case PassiveTarget.AllEnemies:
                    return "Every enemy";
                case PassiveTarget.LowestHpFractionAlly:
                    return "The ally lowest on HP%";
                default:
                    return "Every ally";
            }
        }
    }

    /// <summary>One avatar gear slot: what is worn there, and the player's other owned pieces for it.</summary>
    public sealed class AvatarGearSlotRow
    {
        public AvatarGearSlot Slot;
        public string SlotName;
        public AvatarGearOptionRow Worn;
        public List<AvatarGearOptionRow> Options = new List<AvatarGearOptionRow>();
    }

    public sealed class AvatarGearOptionRow
    {
        public string InstanceId;
        public string Name;
        public int MinimumLevel;
        public List<string> Bonuses = new List<string>();
        public bool Equippable;
        public string Reason;
        public bool Inactive;
    }

    /// <summary>The Gear tab: the avatar's three slots, equip/unequip through the same <see cref="GearRules"/> a beast uses.</summary>
    public sealed class AvatarGearViewModel
    {
        private readonly GameSession _session;

        public AvatarGearViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<AvatarGearSlotRow> Slots { get; } = new List<AvatarGearSlotRow>();

        public void Refresh()
        {
            Slots.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            GameContent content = _session.Content;
            int level = save?.Avatar?.Level ?? 1;
            foreach (AvatarGearSlot slot in (AvatarGearSlot[])Enum.GetValues(typeof(AvatarGearSlot)))
            {
                AvatarGearSlotRow row = new AvatarGearSlotRow { Slot = slot, SlotName = SlotName(slot) };
                string wornId = GearRules.GetSlot(save?.AvatarEquippedGear, (int)slot);
                foreach (OwnedGear owned in save?.Gear?.AvatarGear ?? new List<OwnedGear>())
                {
                    AvatarGearSO gear = content.Battle.GetAvatarGear(owned.GearId);
                    if (gear == null || gear.Slot != slot)
                    {
                        continue;
                    }

                    AvatarGearOptionRow option = View(owned, gear);
                    if (owned.InstanceId == wornId)
                    {
                        option.Inactive = level < gear.MinimumLevel;
                        row.Worn = option;
                    }
                    else if (level < gear.MinimumLevel)
                    {
                        option.Reason = "Needs Lv " + gear.MinimumLevel + ".";
                    }
                    else
                    {
                        option.Equippable = true;
                    }

                    row.Options.Add(option);
                }

                Slots.Add(row);
            }
        }

        /// <summary>Equips <paramref name="instanceId"/> into <paramref name="slot"/> (<see cref="GearRules.EquipAvatarGear"/>). Autosaves.</summary>
        public bool EquipGear(AvatarGearSlot slot, string instanceId, out string message)
        {
            GearEquipResult result = GearRules.EquipAvatarGear(_session.Save, slot, instanceId, _session.Content.Battle);
            if (result == GearEquipResult.Equipped)
            {
                return Changed(out message, "Equipped.");
            }

            message = EquipReason(result);
            return false;
        }

        /// <summary>Takes the gear in <paramref name="slot"/> off (<see cref="GearRules.UnequipAvatarGear"/>). Autosaves.</summary>
        public bool UnequipGear(AvatarGearSlot slot, out string message)
        {
            if (!GearRules.UnequipAvatarGear(_session.Save, slot))
            {
                message = "Nothing worn there.";
                return false;
            }

            return Changed(out message, "Taken off.");
        }

        private bool Changed(out string message, string text)
        {
            message = text;
            _session.Autosave(AutosaveReason.PlayerEdit);
            Refresh();
            return true;
        }

        private static AvatarGearOptionRow View(OwnedGear owned, AvatarGearSO gear)
        {
            AvatarGearOptionRow view = new AvatarGearOptionRow { InstanceId = owned.InstanceId, Name = gear.DisplayName ?? gear.AvatarGearId, MinimumLevel = gear.MinimumLevel };
            foreach (StatModifier modifier in gear.Modifiers ?? new List<StatModifier>())
            {
                if (modifier == null)
                {
                    continue;
                }

                if (modifier.FlatBonus != 0)
                {
                    view.Bonuses.Add((modifier.FlatBonus > 0 ? "+" : string.Empty) + modifier.FlatBonus.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                                     DerivedStats.ShortName(modifier.Stat));
                }

                if (modifier.PercentBonus != 0f)
                {
                    view.Bonuses.Add((modifier.PercentBonus > 0f ? "+" : string.Empty) + (modifier.PercentBonus * 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                                     "% " + DerivedStats.ShortName(modifier.Stat));
                }
            }

            return view;
        }

        private static string EquipReason(GearEquipResult result)
        {
            switch (result)
            {
                case GearEquipResult.LevelTooLow:
                    return "The avatar's level is too low.";
                case GearEquipResult.SlotMismatch:
                    return "It does not go in that slot.";
                case GearEquipResult.UnknownInstance:
                    return "You do not have it.";
                case GearEquipResult.UnknownGear:
                    return "Unknown gear.";
                default:
                    return "No avatar.";
            }
        }

        public static string SlotName(AvatarGearSlot slot)
        {
            switch (slot)
            {
                case AvatarGearSlot.Weapon:
                    return "Staff";
                case AvatarGearSlot.Armor:
                    return "Coat";
                default:
                    return "Ring";
            }
        }
    }

    /// <summary>One wardrobe entry: a discrete look (owned/locked) or a colour swatch.</summary>
    public sealed class WardrobeOptionRow
    {
        public string OptionId;
        public string Key;
        public string Name;
        public bool Owned;
        public bool Worn;
        public bool TokenPurchasable;
        public int TokenPrice;
    }

    public sealed class WardrobeCategoryRow
    {
        public string CategoryId;
        public string DisplayName;
        public bool IsColor;
        public string WornColorHex;
        public List<WardrobeOptionRow> Options = new List<WardrobeOptionRow>();
    }

    /// <summary>
    /// The Wardrobe tab: every avatar cosmetic category (<see cref="CosmeticCategory.IsAvatar"/>),
    /// discrete looks with Wear (<see cref="CosmeticRules.TrySetOption"/> against the avatar,
    /// <c>beastId: null</c> — the established meaning throughout <see cref="CosmeticRules"/>) and
    /// colour categories as a small curated swatch row (<see cref="Swatches"/>: UI-only presets over
    /// the existing free <see cref="CosmeticRules.TrySetColor"/> rule; no colour-picker widget exists
    /// in the toolkit, see avatar-inventory-shop.md).
    /// </summary>
    public sealed class AvatarWardrobeViewModel
    {
        /// <summary>Six curated presets offered for every colour category (a picker widget is a later PR).</summary>
        public static readonly string[] Swatches = { "#F5E6C8", "#8B5E3C", "#3C2A21", "#E8B4B8", "#4A6B5A", "#2E3A59" };

        private readonly GameSession _session;

        public AvatarWardrobeViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<WardrobeCategoryRow> Categories { get; } = new List<WardrobeCategoryRow>();

        public void Refresh()
        {
            Categories.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            CosmeticLibrary library = _session.Content.Economy?.Cosmetics;
            if (library == null)
            {
                return;
            }

            foreach (CosmeticCategory category in library.Categories)
            {
                if (category == null || !category.IsAvatar)
                {
                    continue;
                }

                WardrobeCategoryRow row = new WardrobeCategoryRow { CategoryId = category.CategoryId, DisplayName = category.DisplayName, IsColor = category.IsColor };
                if (category.IsColor)
                {
                    row.WornColorHex = HexOf(CosmeticRules.AppearanceOf(save, null)?.GetColor(category.CategoryId) ?? category.DefaultColor);
                }
                else
                {
                    CosmeticOption worn = CosmeticRules.Worn(save, null, category.CategoryId, library);
                    foreach (CosmeticOption option in category.Options)
                    {
                        row.Options.Add(new WardrobeOptionRow
                        {
                            OptionId = option.OptionId,
                            Key = option.Key,
                            Name = option.DisplayName ?? option.OptionId,
                            Owned = CosmeticRules.IsUsable(save, option),
                            Worn = worn != null && worn.OptionId == option.OptionId,
                            TokenPurchasable = option.TokenPurchasable,
                            TokenPrice = option.TokenPrice
                        });
                    }
                }

                Categories.Add(row);
            }
        }

        /// <summary>Wears <paramref name="optionId"/> of <paramref name="categoryId"/> on the avatar. Autosaves on success.</summary>
        public CosmeticResult Wear(string categoryId, string optionId)
        {
            CosmeticResult result = CosmeticRules.TrySetOption(_session.Save, null, categoryId, optionId, _session.Content.Economy?.Cosmetics);
            if (result == CosmeticResult.Set)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return result;
        }

        /// <summary>Sets <paramref name="categoryId"/>'s colour to one of the <see cref="Swatches"/>. Always free; autosaves on success.</summary>
        public CosmeticResult SetColor(string categoryId, string hex)
        {
            CosmeticResult result = CosmeticRules.TrySetColor(_session.Save, null, categoryId, ParseColor(hex), _session.Content.Economy?.Cosmetics);
            if (result == CosmeticResult.Set)
            {
                _session.Autosave(AutosaveReason.PlayerEdit);
            }

            Refresh();
            return result;
        }

        private static string HexOf(BeastCraft.Color color)
        {
            int r = (int)(Math.Max(0f, Math.Min(1f, color.r)) * 255f);
            int g = (int)(Math.Max(0f, Math.Min(1f, color.g)) * 255f);
            int b = (int)(Math.Max(0f, Math.Min(1f, color.b)) * 255f);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        private static BeastCraft.Color ParseColor(string hex)
        {
            return CosmeticCategory.ParseColor(hex);
        }
    }

    /// <summary>
    /// The Avatar screen's four inner tabs, over one <see cref="GameSession"/> — the same composition
    /// shape as <see cref="GroveHubViewModel"/>.
    /// </summary>
    public sealed class AvatarHubViewModel
    {
        public AvatarHubViewModel(GameSession session)
        {
            Overview = new AvatarOverviewViewModel(session);
            Skills = new AvatarSkillsViewModel(session);
            Gear = new AvatarGearViewModel(session);
            Wardrobe = new AvatarWardrobeViewModel(session);
        }

        public AvatarTab Tab { get; private set; } = AvatarTab.Overview;
        public AvatarOverviewViewModel Overview { get; }
        public AvatarSkillsViewModel Skills { get; }
        public AvatarGearViewModel Gear { get; }
        public AvatarWardrobeViewModel Wardrobe { get; }

        public void Select(AvatarTab tab)
        {
            Tab = tab;
        }

        public void RefreshAll()
        {
            Overview.Refresh();
            Skills.Refresh();
            Gear.Refresh();
            Wardrobe.Refresh();
        }
    }
}
