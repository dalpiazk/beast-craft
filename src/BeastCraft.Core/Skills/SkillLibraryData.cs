using System;

namespace BeastCraft.Skills
{
    /// <summary>
    /// The plain-data shape of <c>data/Skills/skill-library.json</c>, the source of truth for every
    /// authored skill: beast skills, the avatar's active skills and passives, the skill-training
    /// materials, and which beast learns what (with its default loadout). The Unity assets
    /// (<c>SkillSO</c>, <c>PassiveSkillSO</c>, <c>SkillMaterialSO</c>, and each
    /// <c>CreatureSpeciesSO</c>'s <c>LearnableSkills</c> / <c>DefaultLoadout</c>) are generated from
    /// it by the Editor importer (menu: Beast Craft/Data/Import Skill Library); never hand-edit the
    /// imported fields on those assets, edit the JSON and re-import.
    /// <para>
    /// Like the roster DTOs (<c>BeastRosterData</c>), these are plain serializable classes with
    /// public fields and no Unity-object references, so the same types read the file inside Unity
    /// (<c>JsonUtility</c>) and outside it (<c>System.Text.Json</c> with <c>IncludeFields = true</c>,
    /// e.g. the balance simulator). JSON keys are the field names exactly. Every enum is written as
    /// its member name (a string), parsed case-sensitively by <see cref="SkillLibraryValidator"/>,
    /// because <c>JsonUtility</c> would otherwise read enums as bare numbers. A missing or empty
    /// enum string means that field's default, noted on each field.
    /// </para>
    /// <para>
    /// <see cref="SkillLibraryBuilder"/> turns these into the runtime objects; the importer and the
    /// simulator both go through it, so they cannot map a field differently.
    /// </para>
    /// </summary>
    [Serializable]
    public class SkillLibraryData
    {
        /// <summary>Path of the library file relative to the repository root.</summary>
        public const string ProjectRelativePath = "content/data/Skills/skill-library.json";

        /// <summary>
        /// The only <see cref="SchemaVersion"/> this code reads. 2 added scaling team bonds
        /// (<see cref="TeamBondData.PerCount"/>, <see cref="TeamBondData.MaxCount"/>, scope
        /// <c>Others</c>); 3 added behaviour bonds (<see cref="TeamBondTierData.Reaction"/>, the
        /// <c>DistinctStances</c> condition, the <c>Cleanse</c> effect), which older code would read
        /// as silent bonds.
        /// </summary>
        public const int CurrentSchemaVersion = 3;

        /// <summary>Bumped when the file's shape changes incompatibly.</summary>
        public int SchemaVersion;

        /// <summary>Skill-training materials. Imports into <c>SkillMaterialSO</c> assets.</summary>
        public SkillMaterialData[] Materials = new SkillMaterialData[0];

        /// <summary>Skills beasts learn. Imports into <c>SkillSO</c> assets.</summary>
        public SkillData[] BeastSkills = new SkillData[0];

        /// <summary>
        /// The avatar's active support skills. Also <c>SkillSO</c> assets, restricted by authoring
        /// convention (checked here by the validator) to the position-free shapes <c>Self</c>,
        /// <c>AllAllies</c> and <c>AllEnemies</c>. The first
        /// <see cref="AvatarDefaultActiveCount"/> in file order are the avatar's default loadout.
        /// </summary>
        public SkillData[] AvatarActives = new SkillData[0];

        /// <summary>The avatar's passives. Imports into <c>PassiveSkillSO</c> assets.</summary>
        public PassiveData[] AvatarPassives = new PassiveData[0];

        /// <summary>
        /// The avatar's default passive loadout, in slot order: up to
        /// <c>AvatarSkillBook.PassiveSlotCount</c> <see cref="PassiveData.PassiveId"/>s.
        /// </summary>
        public string[] AvatarDefaultPassives = new string[0];

        /// <summary>Per species: what it learns and at which level, and its default loadout.</summary>
        public SpeciesKitData[] SpeciesKits = new SpeciesKitData[0];

        /// <summary>
        /// Team bonds: composition-triggered team effects applied at battle start. Imports into
        /// <c>TeamBondSO</c> assets. Optional (an empty list means no bonds); see the battle-system
        /// design doc, "Team bonds".
        /// </summary>
        public TeamBondData[] TeamBonds = new TeamBondData[0];

        /// <summary>How many of <see cref="AvatarActives"/>, from the front, make the avatar's default active loadout.</summary>
        public const int AvatarDefaultActiveCount = 3;
    }

    /// <summary>One skill-training material. Imports into a <c>SkillMaterialSO</c>.</summary>
    [Serializable]
    public class SkillMaterialData
    {
        /// <summary>Stable lowercase snake_case save key. Never rename after ship.</summary>
        public string MaterialId;

        public string DisplayName;

        public string Description;

        /// <summary>Rarity tier, 1 upward; a breakthrough gate needs at least its required tier.</summary>
        public int Tier = 1;

        /// <summary>XP the material adds when fed to a skill.</summary>
        public int XpValue = 100;
    }

    /// <summary>
    /// One skill (a beast skill or an avatar active). Imports into a <c>SkillSO</c>; the fields
    /// mirror it one to one. Targeting defaults differ from <c>SkillSO</c>'s on purpose: a missing
    /// <see cref="TargetingCriterion"/> means <c>Distance</c> (the nearest unit), not
    /// <c>Random</c>, so an authored skill never spends the battle rng on targeting unless it says so.
    /// </summary>
    [Serializable]
    public class SkillData
    {
        /// <summary>Stable lowercase snake_case save key. Never rename after ship.</summary>
        public string SkillId;

        public string DisplayName;

        public string Description;

        /// <summary>
        /// The skill's icon: an art key naming a sprite's <c>ArtKey</c> in the art manifest
        /// (<c>content/art/pixel/pixel-art-manifest.json</c>), e.g. <c>"skill/ember_shot"</c>, shown in
        /// the battle skill strip, the skill card and skill lists. Presentation only (never read by the
        /// battle). Optional in the DTO (an enemy-library skill may have none), but every shipped
        /// beast skill and avatar active names one (<c>ArtReferenceValidator</c>).
        /// </summary>
        public string ArtKey;

        public int ResourceCost;

        /// <summary>Turns between uses; 0 or 1 fires every turn (see <c>SkillLoadout</c>).</summary>
        public int Cooldown;

        /// <summary>
        /// <c>SkillSO.InitialCooldown</c>: -1 (the default, and any negative value) starts at the
        /// ordinary cooldown; 0 fires on the owner's first turn.
        /// </summary>
        public int InitialCooldown = -1;

        /// <summary><c>SkillSO.MaxUsesPerBattle</c>; 0 is unlimited.</summary>
        public int MaxUsesPerBattle;

        /// <summary>A <c>SkillTargetShape</c> name. Missing: <c>SingleTarget</c>.</summary>
        public string TargetShape;

        /// <summary>Reach in hex steps (ignored by <c>Self</c>, <c>AllEnemies</c>, <c>AllAllies</c>).</summary>
        public int Range = 1;

        /// <summary>A <c>SkillTargetSide</c> name. Missing: <c>Enemy</c>.</summary>
        public string TargetSide;

        /// <summary>A <c>SkillTargetingCriterion</c> name. Missing: <c>Distance</c> (see the class remarks).</summary>
        public string TargetingCriterion;

        /// <summary>A <c>SkillTargetingOrder</c> name. Missing: <c>Lowest</c>.</summary>
        public string TargetingOrder;

        /// <summary>A <c>StatType</c> name, read only by the <c>Stat</c> criterion. Missing: <c>HP</c>.</summary>
        public string TargetingStat;

        /// <summary>An <c>Element</c> name. Missing: <c>None</c>.</summary>
        public string Element;

        /// <summary>A <c>DamageCategory</c> name. Missing: <c>Physical</c>.</summary>
        public string Category;

        public EffectData[] Effects = new EffectData[0];

        public ProgressionData Progression = new ProgressionData();
    }

    /// <summary>One <c>SkillEffect</c>. Missing numbers take <c>SkillEffect</c>'s defaults.</summary>
    [Serializable]
    public class EffectData
    {
        /// <summary>A <c>SkillEffectType</c> name. Missing: <c>Damage</c>.</summary>
        public string EffectType;

        /// <summary>A <c>StatType</c> name, for <c>BuffStat</c> / <c>DebuffStat</c>. Missing: <c>Attack</c>.</summary>
        public string AffectedStat;

        public float Magnitude;

        public int DurationTurns;

        /// <summary>A <c>StatusType</c> name, for <c>ApplyStatus</c>. Missing: <c>None</c>.</summary>
        public string Status;

        public int Chance = 100;

        public int MaxStacks = 1;

        public bool IsPercent;

        public int HitCount = 1;

        public int ExecuteBonusPercent;
    }

    /// <summary>A <c>SkillProgressionDefinition</c>.</summary>
    [Serializable]
    public class ProgressionData
    {
        public int MaxLevel = 20;

        public float MagnitudeGrowthPerLevel = 3f;

        public TierData[] Tiers = new TierData[0];
    }

    /// <summary>A <c>SkillTierDefinition</c> (one breakthrough gate).</summary>
    [Serializable]
    public class TierData
    {
        public int ThresholdLevel = 5;

        public int RequiredMaterialTier = 1;

        public int CooldownReduction;

        public EffectData[] BonusEffects = new EffectData[0];
    }

    /// <summary>One avatar passive. Imports into a <c>PassiveSkillSO</c>; the fields mirror it.</summary>
    [Serializable]
    public class PassiveData
    {
        /// <summary>Stable lowercase snake_case save key. Never rename after ship.</summary>
        public string PassiveId;

        public string DisplayName;

        public string Description;

        /// <summary>
        /// The skill's icon: an art key naming a sprite's <c>ArtKey</c> in the art manifest
        /// (<c>content/art/pixel/pixel-art-manifest.json</c>), e.g. <c>"skill/keen_eye"</c>, shown in
        /// the battle skill strip, the skill card and skill lists. Presentation only (never read by the
        /// battle). Optional in the DTO (an enemy-library skill may have none), but every shipped
        /// avatar passive names one (<c>ArtReferenceValidator</c>).
        /// </summary>
        public string ArtKey;

        /// <summary>A <c>PassiveTrigger</c> name. Missing: <c>Aura</c>.</summary>
        public string Trigger;

        public int HpThresholdPercent = 50;

        public int ProcChance = 100;

        public int MaxTriggersPerBattle;

        public int InternalCooldown;

        /// <summary>A <c>PassiveTarget</c> name. Missing: <c>AllAllies</c>.</summary>
        public string TargetScope;

        /// <summary>An <c>Element</c> name. Missing: <c>None</c>.</summary>
        public string Element;

        /// <summary>A <c>DamageCategory</c> name. Missing: <c>Physical</c>.</summary>
        public string Category;

        public EffectData[] Effects = new EffectData[0];

        public ProgressionData Progression = new ProgressionData();
    }

    /// <summary>What one species learns, and the loadout a fresh beast of it starts with.</summary>
    [Serializable]
    public class SpeciesKitData
    {
        /// <summary>A <c>SpeciesId</c> from <c>beast-roster.json</c>.</summary>
        public string SpeciesId;

        /// <summary>Level-up acquisition: each entry's skill becomes available at its level.</summary>
        public LearnEntryData[] LearnableSkills = new LearnEntryData[0];

        /// <summary>
        /// Exactly <c>BeastSkillBook.EquipSlotCount</c> beast-skill ids, in slot (fire-priority)
        /// order, each learnable by level <see cref="SkillLibraryValidator.MaxDefaultLearnLevel"/>.
        /// </summary>
        public string[] DefaultLoadout = new string[0];
    }

    /// <summary>One team bond. Imports into a <c>TeamBondSO</c>; the fields mirror it.</summary>
    [Serializable]
    public class TeamBondData
    {
        /// <summary>Stable lowercase snake_case key. Never rename after ship.</summary>
        public string BondId;

        public string DisplayName;

        public string Description;

        /// <summary>A <c>TeamBondCondition</c> name: <c>Stance</c>, <c>Elements</c> or <c>Species</c>. Missing: <c>Stance</c>.</summary>
        public string Condition;

        /// <summary>For a <c>Stance</c> bond: a <c>CombatStance</c> name. Missing: <c>Vanguard</c>.</summary>
        public string Stance;

        /// <summary>For an <c>Elements</c> bond: the element set, as <c>Element</c> names.</summary>
        public string[] Elements = new string[0];

        /// <summary>For a <c>Species</c> bond: the species set, as roster <c>SpeciesId</c>s.</summary>
        public string[] Species = new string[0];

        /// <summary>A <c>TeamBondScope</c> name: <c>Members</c>, <c>Team</c> or <c>Others</c>. Missing: <c>Members</c>.</summary>
        public string Scope;

        /// <summary>The tiers, by strictly rising <see cref="TeamBondTierData.MinCount"/>; exactly one for a <see cref="PerCount"/> bond.</summary>
        public TeamBondTierData[] Tiers = new TeamBondTierData[0];

        /// <summary>
        /// A scaling bond: its one tier's magnitudes apply once per counted member (stack), up to
        /// <see cref="MaxCount"/>. Missing: false (a tiered bond).
        /// </summary>
        public bool PerCount;

        /// <summary>For a <see cref="PerCount"/> bond: the stack cap (at least the tier's <c>MinCount</c>). Missing / 0 otherwise.</summary>
        public int MaxCount;
    }

    /// <summary>
    /// One <c>TeamBondTier</c>: the count it needs, its full battle-start effect list and its
    /// in-battle reaction (tiers replace, not stack). A tier needs effects, a reaction, or both.
    /// </summary>
    [Serializable]
    public class TeamBondTierData
    {
        public int MinCount = 2;

        /// <summary>Applied once at battle start (see <c>TeamBondTier.Effects</c>). May be empty when the tier has a <see cref="Reaction"/>.</summary>
        public EffectData[] Effects = new EffectData[0];

        /// <summary>
        /// The tier's behaviour-bond reaction (a <c>BondReaction</c>). Missing, or a missing / empty
        /// <see cref="BondReactionData.Trigger"/>: none.
        /// </summary>
        public BondReactionData Reaction;
    }

    /// <summary>One <c>BondReaction</c>. Enum fields are member names; missing ones take the defaults noted.</summary>
    [Serializable]
    public class BondReactionData
    {
        /// <summary>A <c>BondTrigger</c> name. Missing / empty: <c>None</c> (no reaction).</summary>
        public string Trigger;

        /// <summary>A <c>BondAction</c> name: <c>Apply</c> or <c>Intercept</c>. Missing: <c>Apply</c>.</summary>
        public string Action;

        /// <summary>A <c>BondReactionTarget</c> name. Missing: <c>TriggerTarget</c>.</summary>
        public string Target;

        /// <summary>A <c>BondTriggerFilter</c> name: <c>Any</c>, <c>Members</c> or <c>NonMembers</c>. Missing: <c>Any</c>.</summary>
        public string TriggerFilter;

        /// <summary>A <c>BondReactorOrder</c> name: <c>TeamOrder</c> or <c>HealthiestFirst</c>. Missing: <c>TeamOrder</c>.</summary>
        public string ReactorOrder;

        public int Chance = 100;

        public int Cooldown;

        public int MaxPerMember;

        public int MaxPerTriggerUnit;

        public int MaxPerBattle;

        public int Range;

        public int HpThresholdPercent = 50;

        public EffectData[] Effects = new EffectData[0];

        /// <summary>Whether this names a trigger at all (a missing / empty / <c>None</c> trigger is no reaction).</summary>
        public bool IsSet
        {
            get { return !string.IsNullOrEmpty(Trigger) && Trigger != "None"; }
        }
    }

    /// <summary>One <c>SkillLearnEntry</c>: a beast-skill id and the level it is learned at.</summary>
    [Serializable]
    public class LearnEntryData
    {
        public int Level = 1;

        public string SkillId;
    }
}
