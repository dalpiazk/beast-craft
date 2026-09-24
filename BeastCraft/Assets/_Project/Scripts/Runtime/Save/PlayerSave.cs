using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Customization;
using BeastCraft.Economy;
using BeastCraft.Progression;

namespace BeastCraft.Save
{
    /// <summary>
    /// Everything persisted about one player, as one versioned aggregate: the beast collection
    /// (each beast's species, level, XP and skill book), the avatar's level and skill books, and the
    /// material inventory with its first-clear and pity state, the gear inventory with what each
    /// beast and the avatar wears (schema 2), the region campaign: seals, region progress and
    /// the expedition in progress (schema 3), and the economy: gold, consumables, the Traders' frozen
    /// stock, cosmetic unlocks and the avatar's and every beast's appearance (schema 4).
    /// <para>
    /// <strong>JsonUtility-compatible by construction.</strong> Every type reachable from here is
    /// <c>[Serializable]</c> with public fields, and every map is a list (<c>JsonUtility</c> drops
    /// dictionaries and properties). Everything is named by stable string ids — species, skill,
    /// passive and material ids, all under the never-rename rule — never by asset reference.
    /// </para>
    /// <para>
    /// <strong>Versioned.</strong> <see cref="SchemaVersion"/> is stamped with
    /// <see cref="CurrentSchemaVersion"/> on every write by <see cref="SaveSerializer"/>, which
    /// upgrades older saves through its migration steps before reading them. Bump
    /// <see cref="CurrentSchemaVersion"/> and add a step to <see cref="SaveMigrations.All"/> whenever a
    /// change would make an old save read wrongly; purely additive fields with a sensible default
    /// need no bump.
    /// </para>
    /// </summary>
    [Serializable]
    public class PlayerSave
    {
        /// <summary>The schema this code writes, and the newest it reads.</summary>
        public const int CurrentSchemaVersion = 4;

        /// <summary>The schema the data is in. 0 (or missing) is never valid.</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>The beast collection, in the order obtained.</summary>
        public List<OwnedBeast> Beasts = new List<OwnedBeast>();

        /// <summary>The avatar's own level and XP.</summary>
        public AvatarProgress Avatar = new AvatarProgress();

        /// <summary>The avatar's active and passive skill books.</summary>
        public AvatarSkillBook AvatarSkills = new AvatarSkillBook();

        /// <summary>Held materials, cleared cells and pity counters.</summary>
        public MaterialInventory Materials = new MaterialInventory();

        /// <summary>Every owned piece of beast and avatar gear, worn or not. Added in schema 2.</summary>
        public GearInventory Gear = new GearInventory();

        /// <summary>
        /// The avatar's worn gear, by slot: index <c>(int)AvatarGearSlot</c> holds an
        /// <see cref="OwnedGear.InstanceId"/> from <see cref="GearInventory.AvatarGear"/>, or null/""
        /// when empty. Change it through <see cref="GearRules"/>. Added in schema 2.
        /// </summary>
        public string[] AvatarEquippedGear = new string[GearRules.AvatarSlotCount];

        /// <summary>
        /// The region campaign: owned seals, each unlocked region's progress and the expedition in
        /// progress (<see cref="CampaignProgress.ActiveRun"/>, whose <c>RegionId</c> is "" when there
        /// is none). Added in schema 3.
        /// </summary>
        public CampaignProgress Campaign = new CampaignProgress();

        /// <summary>Gold held, 0 to <see cref="Wallet.MaxGold"/>. Change it through <see cref="Wallet"/>. Added in schema 4.</summary>
        public int Gold;

        /// <summary>Held consumables, one stack per id, in the order first obtained. Added in schema 4.</summary>
        public List<ConsumableStack> Consumables = new List<ConsumableStack>();

        /// <summary>
        /// Every Trader's stock as first seen, frozen (<see cref="ShopVisit"/>), oldest first; the
        /// shop keeps only the most recent <c>ShopService.MaxRememberedShops</c>. Added in schema 4.
        /// </summary>
        public List<ShopVisit> Shops = new List<ShopVisit>();

        /// <summary>Account-wide cosmetic unlocks (defaults and starter looks are free and not listed). Added in schema 4.</summary>
        public CosmeticCollection Cosmetics = new CosmeticCollection();

        /// <summary>
        /// The avatar's chosen look: an option per discrete avatar category and a colour per colour
        /// category; a category missing here reads as its default. Change it through
        /// <c>CosmeticRules</c>. Purely cosmetic, no stats. Added in schema 4.
        /// </summary>
        public CustomizationSelection AvatarAppearance = new CustomizationSelection();

        /// <summary>
        /// A brand-new player: no beasts, avatar level 1, nothing learned or held, the starting
        /// region (<see cref="CampaignProgress.StartingRegionId"/>) unlocked.
        /// </summary>
        public static PlayerSave CreateNew()
        {
            PlayerSave save = new PlayerSave();
            save.Campaign.Unlock(CampaignProgress.StartingRegionId);
            return save;
        }

        /// <summary>The beast with <paramref name="beastId"/>, or <c>null</c>.</summary>
        public OwnedBeast FindBeast(string beastId)
        {
            if (string.IsNullOrEmpty(beastId) || Beasts == null)
            {
                return null;
            }

            for (int i = 0; i < Beasts.Count; i++)
            {
                if (Beasts[i] != null && string.Equals(Beasts[i].BeastId, beastId, StringComparison.Ordinal))
                {
                    return Beasts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces any missing (null) collection or sub-object with its empty default and drops null
        /// beast entries, so code reading a loaded save never meets a null. <c>JsonUtility</c> never
        /// produces nulls here; other serializers, or a hand-edited file, can. Ids and numbers are
        /// left alone — reporting bad ones is <see cref="SaveValidator"/>'s job. Returns how many
        /// things were repaired.
        /// </summary>
        public int EnsureInitialized()
        {
            int repaired = 0;

            if (Beasts == null)
            {
                Beasts = new List<OwnedBeast>();
                repaired++;
            }

            repaired += Beasts.RemoveAll(beast => beast == null);

            foreach (OwnedBeast beast in Beasts)
            {
                if (beast.Progress == null)
                {
                    beast.Progress = new BeastProgress();
                    repaired++;
                }

                if (beast.Skills == null)
                {
                    beast.Skills = new BeastSkillBook();
                    repaired++;
                }

                repaired += RepairBook(beast.Skills);

                if (beast.EquippedGear == null)
                {
                    beast.EquippedGear = new string[GearRules.BeastSlotCount];
                    repaired++;
                }

                repaired += RepairAppearance(ref beast.Appearance);
            }

            if (Avatar == null)
            {
                Avatar = new AvatarProgress();
                repaired++;
            }

            if (AvatarSkills == null)
            {
                AvatarSkills = new AvatarSkillBook();
                repaired++;
            }

            if (AvatarSkills.Actives == null)
            {
                AvatarSkills.Actives = new AvatarActiveSkillBook();
                repaired++;
            }

            if (AvatarSkills.Passives == null)
            {
                AvatarSkills.Passives = new AvatarPassiveSkillBook();
                repaired++;
            }

            repaired += RepairBook(AvatarSkills.Actives);
            repaired += RepairBook(AvatarSkills.Passives);

            if (Materials == null)
            {
                Materials = new MaterialInventory();
                repaired++;
            }

            if (Materials.Materials == null)
            {
                Materials.Materials = new List<MaterialStack>();
                repaired++;
            }

            if (Materials.ClearedCells == null)
            {
                Materials.ClearedCells = new List<ClearedCell>();
                repaired++;
            }

            if (Materials.Pity == null)
            {
                Materials.Pity = new List<PityCounter>();
                repaired++;
            }

            if (Gear == null)
            {
                Gear = new GearInventory();
                repaired++;
            }

            if (Gear.BeastGear == null)
            {
                Gear.BeastGear = new List<OwnedGear>();
                repaired++;
            }

            if (Gear.AvatarGear == null)
            {
                Gear.AvatarGear = new List<OwnedGear>();
                repaired++;
            }

            repaired += Gear.BeastGear.RemoveAll(gear => gear == null);
            repaired += Gear.AvatarGear.RemoveAll(gear => gear == null);

            if (AvatarEquippedGear == null)
            {
                AvatarEquippedGear = new string[GearRules.AvatarSlotCount];
                repaired++;
            }

            repaired += Materials.Materials.RemoveAll(stack => stack == null);
            repaired += Materials.ClearedCells.RemoveAll(cell => cell == null);
            repaired += Materials.Pity.RemoveAll(counter => counter == null);

            if (Campaign == null)
            {
                Campaign = new CampaignProgress();
                repaired++;
            }

            repaired += Campaign.EnsureInitialized();

            if (Consumables == null)
            {
                Consumables = new List<ConsumableStack>();
                repaired++;
            }

            repaired += Consumables.RemoveAll(stack => stack == null);

            if (Shops == null)
            {
                Shops = new List<ShopVisit>();
                repaired++;
            }

            repaired += Shops.RemoveAll(visit => visit == null);
            foreach (ShopVisit visit in Shops)
            {
                if (visit.Listings == null)
                {
                    visit.Listings = new List<ShopListing>();
                    repaired++;
                }

                repaired += visit.Listings.RemoveAll(listing => listing == null);
            }

            if (Cosmetics == null)
            {
                Cosmetics = new CosmeticCollection();
                repaired++;
            }

            if (Cosmetics.Unlocked == null)
            {
                Cosmetics.Unlocked = new List<string>();
                repaired++;
            }

            repaired += RepairAppearance(ref AvatarAppearance);
            return repaired;
        }

        private static int RepairAppearance(ref CustomizationSelection appearance)
        {
            int repaired = 0;
            if (appearance == null)
            {
                appearance = new CustomizationSelection();
                repaired++;
            }

            if (appearance.OptionEntries == null)
            {
                appearance.OptionEntries = new List<CustomizationSelection.CategoryOptionEntry>();
                repaired++;
            }

            if (appearance.ColorEntries == null)
            {
                appearance.ColorEntries = new List<CustomizationSelection.CategoryColorEntry>();
                repaired++;
            }

            return repaired;
        }

        private static int RepairBook(SkillBook book)
        {
            int repaired = 0;

            if (book.Known == null)
            {
                book.Known = new List<SkillProgress>();
                repaired++;
            }

            repaired += book.Known.RemoveAll(progress => progress == null);

            if (book.Equipped == null)
            {
                book.Equipped = new string[book.SlotCount];
                repaired++;
            }

            return repaired;
        }
    }
}
