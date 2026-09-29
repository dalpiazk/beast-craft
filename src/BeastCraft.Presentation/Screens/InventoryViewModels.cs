using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Localization;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One owned beast, as a picker offers it (equip gear to it, buy a tome for it) — shared by Inventory and the Trader.</summary>
    public sealed class BeastOptionRow
    {
        public string BeastId;
        public string Name;
        public string Subtitle;
        public string ArtKey;
        public string TintHex;
    }

    /// <summary>Builds a <see cref="BeastOptionRow"/> per owned beast, in save order.</summary>
    public static class BeastOptions
    {
        public static List<BeastOptionRow> For(GameSession session)
        {
            List<BeastOptionRow> rows = new List<BeastOptionRow>();
            foreach (OwnedBeast beast in session?.Save?.Beasts ?? new List<OwnedBeast>())
            {
                CreatureSpeciesSO species = beast?.Progress == null ? null : session.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                rows.Add(new BeastOptionRow
                {
                    BeastId = beast.BeastId,
                    Name = session.BeastName(beast),
                    Subtitle = species != null
                                   ? session.Content.Text.Format("ui.inventory.beast_subtitle", beast.Progress?.Level ?? 1, species.DisplayName)
                                   : session.Content.Text.Format("ui.common.level", beast.Progress?.Level ?? 1),
                    ArtKey = species?.ArtKey,
                    TintHex = species == null ? null : ColourFormPresentation.WornTint(session.Save, beast.BeastId, species.SpeciesId, session.Content.GroveLibrary, session.Content.Economy?.Cosmetics)
                });
            }

            return rows;
        }
    }

    /// <summary>The Inventory screen's inner tabs.</summary>
    public enum InventoryTab
    {
        Gear = 0,
        Materials = 1,
        Looks = 2
    }

    public enum GearOwnerFilter
    {
        All = 0,
        Beast = 1,
        Avatar = 2
    }

    public enum GearSortMode
    {
        Rarity = 0,
        Level = 1,
        Name = 2
    }

    /// <summary>One owned gear instance, beast or avatar, worn or not.</summary>
    public sealed class InventoryGearRow
    {
        public string InstanceId;
        public string GearId;
        public bool IsAvatarGear;
        public string Name;
        public string SlotName;
        public int Rarity;
        public int MinimumLevel;
        public List<string> Bonuses = new List<string>();

        /// <summary>The beast's name, "Avatar", or null when unworn.</summary>
        public string WornBy;

        public bool CanSell => WornBy == null;

        /// <summary>Owned and not seen yet (<see cref="SeenRules.IsNewGear"/>), as the list was built: the row shows a dot.</summary>
        public bool IsNew;
    }

    /// <summary>
    /// The Inventory screen's Gear tab: every owned gear instance (beast and avatar), filterable by
    /// owner and sortable, with Equip (<see cref="GearRules.EquipBeastGear"/>/<see cref="GearRules.EquipAvatarGear"/>,
    /// the same rules the beast detail and Avatar Gear tab already call) and Sell
    /// (<see cref="ShopService.TrySellGear"/>, reachable from anywhere since it needs no <c>ShopContext</c>).
    /// </summary>
    public sealed class InventoryGearViewModel
    {
        private readonly GameSession _session;
        private bool _seenChanged;

        public InventoryGearViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        /// <summary>The text table (<c>ui.*</c>).</summary>
        private StringTable Text
        {
            get { return _session.Content.Text; }
        }

        public GearOwnerFilter Filter { get; private set; } = GearOwnerFilter.All;
        public GearSortMode Sort { get; private set; } = GearSortMode.Rarity;
        public List<InventoryGearRow> Gear { get; } = new List<InventoryGearRow>();

        public void SetFilter(GearOwnerFilter filter)
        {
            Filter = filter;
            Refresh();
        }

        public void CycleSort()
        {
            Sort = (GearSortMode)(((int)Sort + 1) % 3);
            Refresh();
        }

        public void Refresh()
        {
            Gear.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            GameContent content = _session.Content;
            if (save?.Gear == null)
            {
                return;
            }

            if (Filter != GearOwnerFilter.Avatar)
            {
                foreach (OwnedGear owned in save.Gear.BeastGear)
                {
                    GearSO gear = content.Battle.GetGear(owned.GearId);
                    if (gear == null)
                    {
                        continue;
                    }

                    string holder = GearRules.FindBeastGearHolder(save, owned.InstanceId);
                    Gear.Add(Row(owned.InstanceId, gear.GearId, false, gear.DisplayName ?? gear.GearId, BeastDetailViewModel.SlotName(gear.Slot, Text), gear.Rarity, gear.MinimumLevel, gear.Modifiers,
                                 holder == null ? null : _session.BeastName(save.FindBeast(holder))));
                }
            }

            if (Filter != GearOwnerFilter.Beast)
            {
                foreach (OwnedGear owned in save.Gear.AvatarGear)
                {
                    AvatarGearSO gear = content.Battle.GetAvatarGear(owned.GearId);
                    if (gear == null)
                    {
                        continue;
                    }

                    bool worn = Array.IndexOf(save.AvatarEquippedGear ?? new string[0], owned.InstanceId) >= 0;
                    Gear.Add(Row(owned.InstanceId, gear.AvatarGearId, true, gear.DisplayName ?? gear.AvatarGearId, AvatarGearViewModel.SlotName(gear.Slot, Text), gear.Rarity, gear.MinimumLevel,
                                 gear.Modifiers, worn ? Text.Get("ui.inventory.avatar") : null));
                }
            }

            switch (Sort)
            {
                case GearSortMode.Level:
                    Gear.Sort((a, b) => a.MinimumLevel != b.MinimumLevel ? a.MinimumLevel.CompareTo(b.MinimumLevel) : string.CompareOrdinal(a.Name, b.Name));
                    break;
                case GearSortMode.Name:
                    Gear.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    break;
                default:
                    Gear.Sort((a, b) => a.Rarity != b.Rarity ? b.Rarity.CompareTo(a.Rarity) : string.CompareOrdinal(a.Name, b.Name));
                    break;
            }
        }

        /// <summary>Whether any owned gear (beast or avatar, whatever the filter) is not seen yet: the Gear tab shows a dot.</summary>
        public bool AnyNew
        {
            get
            {
                PlayerSave save = _session.Save;
                if (save?.Gear == null)
                {
                    return false;
                }

                foreach (OwnedGear gear in save.Gear.BeastGear.Concat(save.Gear.AvatarGear))
                {
                    if (gear != null && SeenRules.IsNewGear(save, gear.InstanceId))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Records <paramref name="instanceId"/> as seen (its row has been on screen); <see cref="SaveSeen"/> writes it.</summary>
        public void MarkSeen(string instanceId)
        {
            _seenChanged |= SeenRules.MarkGearSeen(_session.Save, instanceId);
        }

        /// <summary>Autosaves when rows were seen since the last call (the screen calls it as it leaves or switches tab).</summary>
        public void SaveSeen()
        {
            if (_seenChanged)
            {
                _seenChanged = false;
                _session.Autosave(AutosaveReason.PlayerEdit);
            }
        }

        private InventoryGearRow Row(string instanceId, string gearId, bool avatar, string name, string slotName, int rarity, int minimumLevel, List<StatModifier> modifiers, string wornBy)
        {
            InventoryGearRow row = new InventoryGearRow
            {
                InstanceId = instanceId,
                GearId = gearId,
                IsAvatarGear = avatar,
                Name = name,
                SlotName = slotName,
                Rarity = rarity,
                MinimumLevel = minimumLevel,
                WornBy = wornBy,
                IsNew = SeenRules.IsNewGear(_session.Save, instanceId)
            };
            foreach (StatModifier modifier in modifiers ?? new List<StatModifier>())
            {
                if (modifier == null)
                {
                    continue;
                }

                row.Bonuses.AddRange(DerivedStats.BonusLines(modifier, Text));
            }

            return row;
        }

        /// <summary>The owned beasts a piece of beast gear could be equipped to (every owned beast; the rule itself reports why not).</summary>
        public List<BeastOptionRow> BeastsFor(string instanceId)
        {
            return BeastOptions.For(_session);
        }

        /// <summary>Equips beast gear <paramref name="instanceId"/> onto <paramref name="beastId"/> (<see cref="GearRules.EquipBeastGear"/>). Autosaves on success.</summary>
        public bool EquipToBeast(string instanceId, string beastId, out string message)
        {
            PlayerSave save = _session.Save;
            OwnedGear owned = save.Gear.FindBeastGear(instanceId);
            GearSO gear = owned == null ? null : _session.Content.Battle.GetGear(owned.GearId);
            if (gear == null)
            {
                message = Text.Get("ui.gear.unknown");
                return false;
            }

            GearEquipResult result = GearRules.EquipBeastGear(save, beastId, gear.Slot, instanceId, _session.Content.Battle);
            if (result == GearEquipResult.Equipped)
            {
                return Changed(out message, Text.Get("ui.beast.equipped_done"));
            }

            message = EquipReason(result);
            return false;
        }

        /// <summary>Equips avatar gear <paramref name="instanceId"/> onto the avatar (<see cref="GearRules.EquipAvatarGear"/>). Autosaves on success.</summary>
        public bool EquipToAvatar(string instanceId, out string message)
        {
            PlayerSave save = _session.Save;
            OwnedGear owned = save.Gear.FindAvatarGear(instanceId);
            AvatarGearSO gear = owned == null ? null : _session.Content.Battle.GetAvatarGear(owned.GearId);
            if (gear == null)
            {
                message = Text.Get("ui.gear.unknown");
                return false;
            }

            GearEquipResult result = GearRules.EquipAvatarGear(save, gear.Slot, instanceId, _session.Content.Battle);
            if (result == GearEquipResult.Equipped)
            {
                return Changed(out message, Text.Get("ui.beast.equipped_done"));
            }

            message = EquipReason(result);
            return false;
        }

        /// <summary>Sells unworn gear <paramref name="instanceId"/> at 25% of its price (<see cref="ShopService.TrySellGear"/>). Autosaves on success.</summary>
        public bool Sell(string instanceId, out string message, out int gold)
        {
            gold = 0;
            ShopService shop = _session.Content.Shop;
            if (shop == null)
            {
                message = Text.Get("ui.shop.unavailable");
                return false;
            }

            ShopSaleResult result = shop.TrySellGear(_session.Save, instanceId);
            if (!result.Success)
            {
                message = Text.Get(result.Outcome == ShopOutcome.Equipped ? "ui.inventory.take_off_first" : "ui.inventory.cannot_sell");
                return false;
            }

            gold = result.Gold;
            return Changed(out message, Text.Format("ui.inventory.sold_for", gold));
        }

        private bool Changed(out string message, string text)
        {
            message = text;
            _session.Autosave(AutosaveReason.PlayerEdit);
            Refresh();
            return true;
        }

        private string EquipReason(GearEquipResult result)
        {
            switch (result)
            {
                case GearEquipResult.LevelTooLow:
                    return Text.Get("ui.gear.level_low");
                case GearEquipResult.EquippedElsewhere:
                    return Text.Get("ui.gear.worn_by_another");
                case GearEquipResult.SlotMismatch:
                    return Text.Get("ui.gear.slot_mismatch");
                case GearEquipResult.UnknownInstance:
                    return Text.Get("ui.gear.not_owned");
                case GearEquipResult.UnknownGear:
                    return Text.Get("ui.gear.unknown");
                default:
                    return Text.Get("ui.gear.no_owner");
            }
        }
    }

    public sealed class InventoryCountRow
    {
        public string Id;
        public string Name;
        public int Quantity;
        public string Detail;
    }

    /// <summary>The Inventory screen's Materials tab: held skill materials, Grove items and consumables — read-only (no Core rule uses any of them from here).</summary>
    public sealed class InventoryMaterialsViewModel
    {
        private readonly GameSession _session;

        public InventoryMaterialsViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        /// <summary>The text table (<c>ui.*</c>).</summary>
        private StringTable Text
        {
            get { return _session.Content.Text; }
        }

        public List<InventoryCountRow> Materials { get; } = new List<InventoryCountRow>();
        public List<InventoryCountRow> GroveItems { get; } = new List<InventoryCountRow>();
        public List<InventoryCountRow> Consumables { get; } = new List<InventoryCountRow>();

        public void Refresh()
        {
            Materials.Clear();
            GroveItems.Clear();
            Consumables.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            if (save == null)
            {
                return;
            }

            foreach (SkillMaterialSO material in BeastDetailViewModel.MaterialDefinitions(_session.Content))
            {
                int owned = save.Materials.GetCount(material.MaterialId);
                if (owned > 0)
                {
                    Materials.Add(new InventoryCountRow { Id = material.MaterialId, Name = material.DisplayName ?? material.MaterialId, Quantity = owned, Detail = Text.Format("ui.inventory.tier", material.Tier) });
                }
            }

            foreach (BeastCraft.Grove.GroveItemStack stack in save.Grove?.Items?.Items ?? new List<BeastCraft.Grove.GroveItemStack>())
            {
                if (stack != null && stack.Quantity > 0)
                {
                    GroveItems.Add(new InventoryCountRow { Id = stack.ItemId, Name = GardenViewModel.Humanize(stack.ItemId), Quantity = stack.Quantity });
                }
            }

            foreach (ConsumableStack stack in save.Consumables ?? new List<ConsumableStack>())
            {
                if (stack == null || stack.Quantity <= 0)
                {
                    continue;
                }

                ConsumableSO consumable = _session.Content.Economy?.Consumables?.Get(stack.ConsumableId);
                Consumables.Add(new InventoryCountRow
                {
                    Id = stack.ConsumableId,
                    Name = consumable?.DisplayName ?? stack.ConsumableId,
                    Quantity = stack.Quantity,
                    Detail = consumable?.Description
                });
            }
        }
    }

    public sealed class WardrobeCollectionRow
    {
        public string CategoryId;
        public string DisplayName;
        public int Owned;
        public int Total;
    }

    /// <summary>The Inventory screen's Looks tab: the look-token balance and a per-category owned/total count — a small Collector "collection" summary.</summary>
    public sealed class InventoryLooksViewModel
    {
        private readonly GameSession _session;

        public InventoryLooksViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public int LookTokens { get; private set; }
        public List<WardrobeCollectionRow> Categories { get; } = new List<WardrobeCollectionRow>();

        public void Refresh()
        {
            Categories.Clear();
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            LookTokens = save?.LookTokens ?? 0;
            CosmeticLibrary library = _session.Content.Economy?.Cosmetics;
            if (library == null)
            {
                return;
            }

            foreach (CosmeticCategory category in library.Categories)
            {
                if (category == null || category.IsColor)
                {
                    continue;
                }

                int owned = 0;
                foreach (CosmeticOption option in category.Options)
                {
                    if (CosmeticRules.IsUsable(save, option))
                    {
                        owned++;
                    }
                }

                Categories.Add(new WardrobeCollectionRow { CategoryId = category.CategoryId, DisplayName = category.DisplayName, Owned = owned, Total = category.Options.Count });
            }
        }
    }

    /// <summary>The Inventory screen's three inner tabs, over one <see cref="GameSession"/>.</summary>
    public sealed class InventoryHubViewModel
    {
        public InventoryHubViewModel(GameSession session)
        {
            Gear = new InventoryGearViewModel(session);
            Materials = new InventoryMaterialsViewModel(session);
            Looks = new InventoryLooksViewModel(session);
        }

        public InventoryTab Tab { get; private set; } = InventoryTab.Gear;
        public InventoryGearViewModel Gear { get; }
        public InventoryMaterialsViewModel Materials { get; }
        public InventoryLooksViewModel Looks { get; }

        public void Select(InventoryTab tab)
        {
            Tab = tab;
        }

        public void RefreshAll()
        {
            Gear.Refresh();
            Materials.Refresh();
            Looks.Refresh();
        }
    }
}
