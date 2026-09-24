using System;
using System.Collections.Generic;
using BeastCraft.Progression;
using BeastCraft.Skills;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The plain-data shape of <c>Data/Economy/shop-tables.json</c>: what a Trader stocks and at what
    /// price. Prices are in <em>price units</em> — one unit is <see cref="PriceUnitData"/>'s
    /// <c>Base + PerLevel x level</c>, the gold a squad clear pays at that level before variance — so
    /// a price keeps pace with income. <see cref="ShopTableValidator"/> checks it;
    /// <see cref="ShopService"/> rolls stock from it. See the economy design doc, "The Trader".
    /// </summary>
    [Serializable]
    public class ShopTableData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Economy/shop-tables.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        /// <summary>One price unit at a level (mirrors <c>drop-tables.json</c>'s squad gold).</summary>
        public PriceUnitData PriceUnit = new PriceUnitData();

        /// <summary>What a Trader buys gear back for, percent of its price (gear only).</summary>
        public int SellbackPct = 25;

        /// <summary>Level bands, contiguous 1-100: how many listings of each category a Trader of the band stocks.</summary>
        public ShopBandData[] Bands = new ShopBandData[0];

        /// <summary>The skill materials a Trader sells.</summary>
        public ShopMaterialData[] Materials = new ShopMaterialData[0];

        public ShopGearData Gear = new ShopGearData();

        public ShopSkillData Skills = new ShopSkillData();

        /// <summary>The avatar's actives and passives a Trader sells once the avatar is level enough (each always in the pool then).</summary>
        public AvatarSkillUnlockData[] AvatarSkillUnlocks = new AvatarSkillUnlockData[0];

        public ShopConsumableData Consumables = new ShopConsumableData();

        public ShopCosmeticData Cosmetics = new ShopCosmeticData();
    }

    [Serializable]
    public class PriceUnitData
    {
        public int Base = 10;

        public int PerLevel = 2;
    }

    /// <summary>One band of Traders (by the trading post's level).</summary>
    [Serializable]
    public class ShopBandData
    {
        public int MinLevel = 1;

        public int MaxLevel = 20;

        /// <summary>One entry per stock category: <c>Material</c>, <c>Gear</c>, <c>Skill</c>, <c>Consumable</c>, <c>Cosmetic</c>.</summary>
        public ShopCategoryCountData[] Categories = new ShopCategoryCountData[0];
    }

    /// <summary>How many listings of a category (uniform between <see cref="Min"/> and <see cref="Max"/>).</summary>
    [Serializable]
    public class ShopCategoryCountData
    {
        public string Category;

        public int Min;

        public int Max;
    }

    [Serializable]
    public class ShopMaterialData
    {
        public string MaterialId;

        /// <summary>Price units per material at the Trader's level.</summary>
        public float PriceUnits = 3f;

        /// <summary>The lowest Trader level that stocks it (never before its tier's first-clear band).</summary>
        public int MinShopLevel = 1;

        /// <summary>Most of it one listing stocks (1 to this, uniform).</summary>
        public int MaxQuantity = 1;

        /// <summary>Its weight in the draw.</summary>
        public int Weight = 1;
    }

    [Serializable]
    public class ShopGearData
    {
        /// <summary>Price units of a common, a rare and (for the sellback only; epics are never sold) an epic, at the piece's MinimumLevel + 10.</summary>
        public float CommonUnits = 6f;

        public float RareUnits = 15f;

        public float EpicUnits = 30f;

        public int CommonWeight = 75;

        public int RareWeight = 25;

        /// <summary>Stock pieces whose MinimumLevel is at most the Trader's level and at least its band's floor minus this.</summary>
        public int BandLookback = 20;
    }

    [Serializable]
    public class ShopSkillData
    {
        /// <summary>Price units of a beast skill tome, at the skill's learn level.</summary>
        public float BeastTomeUnits = 8f;

        /// <summary>A tome teaches a skill up to this many levels before the beast would learn it.</summary>
        public int EarlyAccessLevels = 10;
    }

    /// <summary>One avatar skill the Trader sells.</summary>
    [Serializable]
    public class AvatarSkillUnlockData
    {
        /// <summary>An avatar active (<c>SkillId</c>) or passive (<c>PassiveId</c>) id.</summary>
        public string SkillId;

        /// <summary><c>Active</c> or <c>Passive</c>.</summary>
        public string Kind = "Active";

        public int MinAvatarLevel = 1;

        /// <summary>Price units at <see cref="MinAvatarLevel"/>.</summary>
        public float PriceUnits = 12f;
    }

    [Serializable]
    public class ShopConsumableData
    {
        /// <summary>Price units of a common and a rare consumable at the Trader's level.</summary>
        public float CommonUnits = 1f;

        public float RareUnits = 2f;

        /// <summary>Most of one consumable a listing stocks (1 to this).</summary>
        public int MaxQuantity = 2;
    }

    [Serializable]
    public class ShopCosmeticData
    {
        /// <summary>Price units of a common and a rare look at the middle level of the Trader's region.</summary>
        public float CommonUnits = 8f;

        public float RareUnits = 20f;
    }

    /// <summary>
    /// Checks <see cref="ShopTableData"/>: the price unit positive; sellback 0-100; bands contiguous
    /// 1-100, each naming every category once with 0 &lt;= Min &lt;= Max &lt;= 6; materials known (with
    /// the skill library), each tier sold no earlier than its first-clear band (with the drop
    /// tables), positive prices and weights; gear, skill, consumable and cosmetic prices positive;
    /// avatar unlocks known actives / passives (with the skill library), levels 1-100.
    /// </summary>
    public static class ShopTableValidator
    {
        /// <summary>The stock categories, in the order a Trader rolls them.</summary>
        public static readonly string[] Categories = { "Material", "Gear", "Skill", "Consumable", "Cosmetic" };

        public static List<string> Validate(ShopTableData data)
        {
            return Validate(data, null, null);
        }

        /// <summary>Every problem; empty = importable. <paramref name="library"/> and <paramref name="dropTables"/> (null = not checked) resolve ids and first-clear bands.</summary>
        public static List<string> Validate(ShopTableData data, SkillLibraryData library, DropTableData dropTables)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("The shop tables are null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != ShopTableData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + ShopTableData.CurrentSchemaVersion + ".");
            }

            if (data.PriceUnit == null || data.PriceUnit.Base < 0 || data.PriceUnit.PerLevel < 0 || data.PriceUnit.Base + data.PriceUnit.PerLevel <= 0)
            {
                errors.Add("PriceUnit must have Base and PerLevel at least 0 and not both 0.");
            }

            if (data.SellbackPct < 0 || data.SellbackPct > 100)
            {
                errors.Add("SellbackPct must be 0 to 100.");
            }

            ValidateBands(data.Bands, errors);
            ValidateMaterials(data.Materials, library, dropTables, errors);
            Positive(data.Gear == null ? 0f : Math.Min(data.Gear.CommonUnits, Math.Min(data.Gear.RareUnits, data.Gear.EpicUnits)), "Gear prices", errors);
            if (data.Gear != null && (data.Gear.CommonWeight < 0 || data.Gear.RareWeight < 0 || data.Gear.CommonWeight + data.Gear.RareWeight <= 0 || data.Gear.BandLookback < 0))
            {
                errors.Add("Gear weights must be at least 0 (not both 0) and BandLookback at least 0.");
            }

            Positive(data.Skills == null ? 0f : data.Skills.BeastTomeUnits, "Skills.BeastTomeUnits", errors);
            if (data.Skills != null && (data.Skills.EarlyAccessLevels < 0 || data.Skills.EarlyAccessLevels > 100))
            {
                errors.Add("Skills.EarlyAccessLevels must be 0 to 100.");
            }

            Positive(data.Consumables == null ? 0f : Math.Min(data.Consumables.CommonUnits, data.Consumables.RareUnits), "Consumable prices", errors);
            if (data.Consumables != null && data.Consumables.MaxQuantity < 1)
            {
                errors.Add("Consumables.MaxQuantity must be at least 1.");
            }

            Positive(data.Cosmetics == null ? 0f : Math.Min(data.Cosmetics.CommonUnits, data.Cosmetics.RareUnits), "Cosmetic prices", errors);
            ValidateAvatarUnlocks(data.AvatarSkillUnlocks, library, errors);
            return errors;
        }

        private static void ValidateBands(ShopBandData[] bands, List<string> errors)
        {
            if (bands == null || bands.Length == 0)
            {
                errors.Add("No bands.");
                return;
            }

            int expected = 1;
            foreach (ShopBandData band in bands)
            {
                if (band == null)
                {
                    errors.Add("A band is null.");
                    continue;
                }

                string at = "Band " + band.MinLevel + "-" + band.MaxLevel;
                if (band.MinLevel != expected || band.MaxLevel < band.MinLevel)
                {
                    errors.Add(at + ": bands must be contiguous from level 1.");
                }

                expected = band.MaxLevel + 1;
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ShopCategoryCountData c in band.Categories ?? new ShopCategoryCountData[0])
                {
                    if (c == null || Array.IndexOf(Categories, c.Category) < 0 || !seen.Add(c.Category))
                    {
                        errors.Add(at + ": a category is unknown, null or listed twice.");
                    }
                    else if (c.Min < 0 || c.Max < c.Min || c.Max > 6)
                    {
                        errors.Add(at + " " + c.Category + ": needs 0 <= Min <= Max <= 6.");
                    }
                }

                if (seen.Count != Categories.Length)
                {
                    errors.Add(at + ": must name every category (" + string.Join(", ", Categories) + ").");
                }
            }

            if (expected != 101)
            {
                errors.Add("The bands must end at level 100.");
            }
        }

        private static void ValidateMaterials(ShopMaterialData[] materials, SkillLibraryData library, DropTableData dropTables, List<string> errors)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShopMaterialData m in materials ?? new ShopMaterialData[0])
            {
                if (m == null || string.IsNullOrEmpty(m.MaterialId) || !seen.Add(m.MaterialId))
                {
                    errors.Add("A material is null, has no id, or is listed twice.");
                    continue;
                }

                string at = "Material '" + m.MaterialId + "'";
                if (library != null && Array.Find(library.Materials ?? new SkillMaterialData[0], x => x != null && x.MaterialId == m.MaterialId) == null)
                {
                    errors.Add(at + ": not a material of the skill library.");
                }

                Positive(m.PriceUnits, at + " PriceUnits", errors);
                if (m.MaxQuantity < 1 || m.Weight < 1 || m.MinShopLevel < 1 || m.MinShopLevel > 100)
                {
                    errors.Add(at + ": MaxQuantity and Weight must be at least 1, MinShopLevel 1-100.");
                }

                if (dropTables != null)
                {
                    int firstBand = FirstClearBand(dropTables, m.MaterialId);
                    if (firstBand > m.MinShopLevel)
                    {
                        errors.Add(at + ": sold from level " + m.MinShopLevel + ", before its first-clear band (level " + firstBand + ").");
                    }
                }
            }
        }

        /// <summary>The first band whose first-clear bonus is <paramref name="materialId"/> (its tier's debut), or 1.</summary>
        private static int FirstClearBand(DropTableData tables, string materialId)
        {
            foreach (LevelBandData band in tables.Bands ?? new LevelBandData[0])
            {
                if (band != null && band.FirstClearMaterialId == materialId)
                {
                    return band.MinLevel;
                }
            }

            return 1;
        }

        private static void ValidateAvatarUnlocks(AvatarSkillUnlockData[] unlocks, SkillLibraryData library, List<string> errors)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AvatarSkillUnlockData u in unlocks ?? new AvatarSkillUnlockData[0])
            {
                if (u == null || string.IsNullOrEmpty(u.SkillId) || !seen.Add(u.SkillId))
                {
                    errors.Add("An avatar skill unlock is null, has no id, or is listed twice.");
                    continue;
                }

                string at = "Avatar skill '" + u.SkillId + "'";
                if (u.Kind != "Active" && u.Kind != "Passive")
                {
                    errors.Add(at + ": Kind must be Active or Passive.");
                }
                else if (library != null)
                {
                    bool known = u.Kind == "Active"
                        ? Array.Find(library.AvatarActives ?? new SkillData[0], x => x != null && x.SkillId == u.SkillId) != null
                        : Array.Find(library.AvatarPassives ?? new PassiveData[0], x => x != null && x.PassiveId == u.SkillId) != null;
                    if (!known)
                    {
                        errors.Add(at + ": not an avatar " + u.Kind.ToLowerInvariant() + " of the skill library.");
                    }
                }

                if (u.MinAvatarLevel < 1 || u.MinAvatarLevel > 100)
                {
                    errors.Add(at + ": MinAvatarLevel must be 1 to 100.");
                }

                Positive(u.PriceUnits, at + " PriceUnits", errors);
            }
        }

        private static void Positive(float value, string what, List<string> errors)
        {
            if (!(value > 0f))
            {
                errors.Add(what + " must be above 0.");
            }
        }
    }
}
