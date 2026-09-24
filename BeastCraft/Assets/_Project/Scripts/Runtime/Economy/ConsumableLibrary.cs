using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Skills;
using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>Who a consumable's effects land on.</summary>
    public enum ConsumableRecipients
    {
        /// <summary>Every living beast of the player's team, each applying the effects to itself (like a bond).</summary>
        Team = 0,

        /// <summary>Every living enemy, from the team's first living beast.</summary>
        Enemies = 1
    }

    /// <summary>
    /// A battle item (a draught, a salve, a bomb): used from the pack as the battle begins (at most
    /// <see cref="ConsumableLoadout.MaxPerBattle"/> per battle), spent whatever the outcome.
    /// Created from <c>consumable-library.json</c> by <see cref="ConsumableLibrary"/> (and the
    /// Editor importer). Its effects are ordinary <see cref="SkillEffect"/>s, applied through
    /// <see cref="SkillEffectApplier"/> by <see cref="ConsumableLoadout"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Economy/Consumable", fileName = "NewConsumable")]
    public class ConsumableSO : ScriptableObject
    {
        /// <summary>Stable id persisted in save data. Never rename after ship.</summary>
        public string ConsumableId;

        public string DisplayName;

        [TextArea]
        public string Description;

        /// <summary>Inventory icon.</summary>
        public Sprite Icon;

        /// <summary>Rarity tier, 0 = common and upward.</summary>
        public int Rarity;

        /// <summary>The first region (1-based) whose Traders stock it.</summary>
        public int MinRegion = 1;

        /// <summary>The most of it the pack holds.</summary>
        public int MaxStack = ConsumableLibraryValidator.DefaultMaxStack;

        /// <summary>Who its effects land on.</summary>
        public ConsumableRecipients Recipients = ConsumableRecipients.Team;

        /// <summary>The effects, as a skill's.</summary>
        public List<SkillEffect> Effects = new List<SkillEffect>();
    }

    /// <summary>The plain-data shape of <c>Data/Items/consumable-library.json</c>.</summary>
    [Serializable]
    public class ConsumableLibraryData
    {
        /// <summary>Path of the file relative to the Unity project folder (<c>BeastCraft/</c>).</summary>
        public const string ProjectRelativePath = "Assets/_Project/Data/Items/consumable-library.json";

        /// <summary>The only <see cref="SchemaVersion"/> this code reads.</summary>
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;

        public ConsumableData[] Consumables = new ConsumableData[0];
    }

    /// <summary>One consumable. Imports into a <see cref="ConsumableSO"/>; the fields mirror it.</summary>
    [Serializable]
    public class ConsumableData
    {
        public string ConsumableId;

        public string DisplayName;

        public string Description;

        public int Rarity;

        public int MinRegion = 1;

        public int MaxStack = ConsumableLibraryValidator.DefaultMaxStack;

        /// <summary><c>Team</c> or <c>Enemies</c>.</summary>
        public string Recipients;

        /// <summary>The effects (the skill library's effect shape).</summary>
        public EffectData[] Effects = new EffectData[0];
    }

    /// <summary>
    /// The consumable library indexed for the game, with a shared <see cref="ConsumableSO"/> per
    /// entry (created on first use). Built from <see cref="ConsumableLibraryData"/>; expects data
    /// that passed <see cref="ConsumableLibraryValidator"/> and skips bad entries.
    /// </summary>
    public sealed class ConsumableLibrary
    {
        private readonly List<ConsumableData> _entries = new List<ConsumableData>();
        private readonly object _lock = new object();
        private List<ConsumableSO> _assets;

        private ConsumableLibrary()
        {
        }

        /// <summary>Indexes <paramref name="data"/> (null gives an empty library).</summary>
        public static ConsumableLibrary Build(ConsumableLibraryData data)
        {
            ConsumableLibrary library = new ConsumableLibrary();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConsumableData entry in data == null || data.Consumables == null ? new ConsumableData[0] : data.Consumables)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.ConsumableId) && seen.Add(entry.ConsumableId))
                {
                    library._entries.Add(entry);
                }
            }

            return library;
        }

        /// <summary>Every consumable, in authored order (created on first use, then shared).</summary>
        public IReadOnlyList<ConsumableSO> All
        {
            get
            {
                lock (_lock)
                {
                    if (_assets == null)
                    {
                        List<ConsumableSO> assets = new List<ConsumableSO>();
                        foreach (ConsumableData entry in _entries)
                        {
                            ConsumableSO asset = ScriptableObject.CreateInstance<ConsumableSO>();
                            Apply(entry, asset);
                            assets.Add(asset);
                        }

                        _assets = assets;
                    }

                    return _assets;
                }
            }
        }

        /// <summary>The consumable <paramref name="consumableId"/>, or null.</summary>
        public ConsumableSO Get(string consumableId)
        {
            foreach (ConsumableSO asset in All)
            {
                if (string.Equals(asset.ConsumableId, consumableId, StringComparison.Ordinal))
                {
                    return asset;
                }
            }

            return null;
        }

        /// <summary>Writes <paramref name="data"/> onto <paramref name="asset"/> (shared with the importer).</summary>
        public static void Apply(ConsumableData data, ConsumableSO asset)
        {
            asset.name = data.ConsumableId;
            asset.ConsumableId = data.ConsumableId;
            asset.DisplayName = data.DisplayName;
            asset.Description = data.Description;
            asset.Rarity = data.Rarity;
            asset.MinRegion = Math.Max(1, data.MinRegion);
            asset.MaxStack = data.MaxStack < 1 ? ConsumableLibraryValidator.DefaultMaxStack : data.MaxStack;
            asset.Recipients = SkillLibraryValidator.ParseOr(data.Recipients, ConsumableRecipients.Team);
            asset.Effects = SkillLibraryBuilder.BuildEffects(data.Effects);
        }
    }

    /// <summary>
    /// Checks <see cref="ConsumableLibraryData"/>: ids snake_case and unique, a display name,
    /// rarity 0-2, MinRegion 1-10, MaxStack 1-<see cref="MaxMaxStack"/>, recipients <c>Team</c> or
    /// <c>Enemies</c>, and 1-3 effects each of which is a timed stat change or status the
    /// consumable's recipients should get: for <c>Team</c> a <c>BuffStat</c> (percent, or flat
    /// CritChance) or an <c>ApplyStatus</c> Shield; for <c>Enemies</c> a <c>DebuffStat</c> or an
    /// <c>ApplyStatus</c> DamageOverTime. Every effect lasts 1-<see cref="MaxDuration"/> turns with a
    /// positive magnitude; never damage, heals, stuns, taunts or MoveRange. Strength (at most about
    /// +0.3 of a level each) is the balance simulator's <c>--economy-probe</c>, not checked here.
    /// </summary>
    public static class ConsumableLibraryValidator
    {
        /// <summary>The default and usual max stack.</summary>
        public const int DefaultMaxStack = 5;

        /// <summary>The largest max stack allowed.</summary>
        public const int MaxMaxStack = 20;

        /// <summary>The longest an effect may last, in the affected unit's turns.</summary>
        public const int MaxDuration = 3;

        public static List<string> Validate(ConsumableLibraryData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("The consumable library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != ConsumableLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + ConsumableLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            ConsumableData[] entries = data.Consumables ?? new ConsumableData[0];
            for (int i = 0; i < entries.Length; i++)
            {
                ConsumableData c = entries[i];
                if (c == null)
                {
                    errors.Add("Consumables[" + i + "] is null.");
                    continue;
                }

                string at = "Consumable '" + c.ConsumableId + "'";
                if (!Progression.DropTableValidator.IsSnakeCase(c.ConsumableId))
                {
                    errors.Add(at + ": ConsumableId is not a lowercase snake_case id.");
                }
                else if (!ids.Add(c.ConsumableId))
                {
                    errors.Add(at + ": ConsumableId is used twice.");
                }

                if (string.IsNullOrEmpty(c.DisplayName))
                {
                    errors.Add(at + ": no DisplayName.");
                }

                if (c.Rarity < 0 || c.Rarity > 2)
                {
                    errors.Add(at + ": Rarity must be 0 to 2.");
                }

                if (c.MinRegion < 1 || c.MinRegion > 10)
                {
                    errors.Add(at + ": MinRegion must be 1 to 10.");
                }

                if (c.MaxStack < 1 || c.MaxStack > MaxMaxStack)
                {
                    errors.Add(at + ": MaxStack must be 1 to " + MaxMaxStack + ".");
                }

                bool team = c.Recipients == "Team";
                if (!team && c.Recipients != "Enemies")
                {
                    errors.Add(at + ": Recipients must be Team or Enemies.");
                    continue;
                }

                EffectData[] effects = c.Effects ?? new EffectData[0];
                if (effects.Length < 1 || effects.Length > 3)
                {
                    errors.Add(at + ": needs 1 to 3 effects.");
                }

                foreach (EffectData e in effects)
                {
                    ValidateEffect(e, team, at, errors);
                }
            }

            return errors;
        }

        private static void ValidateEffect(EffectData e, bool team, string at, List<string> errors)
        {
            if (e == null)
            {
                errors.Add(at + ": an effect is null.");
                return;
            }

            SkillLibraryValidator.TryParse(e.EffectType, SkillEffectType.Damage, out SkillEffectType type);
            SkillLibraryValidator.TryParse(e.Status, StatusType.None, out StatusType status);
            bool ok;
            if (type == SkillEffectType.BuffStat || type == SkillEffectType.DebuffStat)
            {
                bool statOk = SkillLibraryValidator.TryParse(e.AffectedStat, StatType.Attack, out StatType stat) && stat != StatType.MoveRange;
                ok = statOk && type == (team ? SkillEffectType.BuffStat : SkillEffectType.DebuffStat) && (e.IsPercent || stat == StatType.CritChance);
            }
            else if (type == SkillEffectType.ApplyStatus)
            {
                ok = status == (team ? StatusType.Shield : StatusType.DamageOverTime);
            }
            else
            {
                ok = false;
            }

            if (!ok)
            {
                errors.Add(at + ": effect " + e.EffectType + " " + e.AffectedStat + " " + e.Status + " is not one this consumable's recipients may get " +
                           "(team: percent BuffStat / flat CritChance / Shield; enemies: DebuffStat / DamageOverTime).");
            }

            if (!(e.Magnitude > 0f) || e.DurationTurns < 1 || e.DurationTurns > MaxDuration)
            {
                errors.Add(at + ": every effect needs a positive Magnitude and 1 to " + MaxDuration + " DurationTurns.");
            }
        }
    }
}
