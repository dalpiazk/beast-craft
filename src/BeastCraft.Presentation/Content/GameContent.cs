using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Idle;
using BeastCraft.Presentation.Art;
using BeastCraft.Presentation.Text;
using BeastCraft.Presentation.Ui;
using BeastCraft.Progression;
using BeastCraft.Session;
using BeastCraft.Skills;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// The authored content a host needs to fight and show a battle, loaded from the data JSON
    /// through the game's own validators and builders — the same mapping the tests and the balance
    /// simulator use (<see cref="BeastRosterBuilder"/>, <see cref="SkillLibraryBuilder"/>,
    /// <see cref="EnemyCatalog"/>, <see cref="EncounterLibrary"/>) — plus the VFX library and the
    /// art manifest (<see cref="ArtManifestData"/>, normalized to schema v2).
    /// <para>
    /// The <em>content root</em> is the folder holding <c>data/</c> and <c>art/pixel/</c>: in the
    /// repo, <c>content/</c>; in a built host, the <c>Content</c> folder it copies
    /// them to. <see cref="FindRoot"/> finds either. Every file is read through an
    /// <see cref="IContentSource"/>, so a host without a file system for its content (Android: APK
    /// assets) loads the same way.
    /// </para>
    /// </summary>
    public sealed class GameContent
    {
        /// <summary>The prefix of every data file's ProjectRelativePath, which a content root already stands for.</summary>
        public const string ProjectPrefix = "content/";

        /// <summary>
        /// The UI typeface, content-root relative: Fredoka SemiBold (SIL Open Font License 1.1,
        /// <see cref="UiFontLicensePath"/>), a rounded, friendly face for the HUD and cards. Hosts
        /// ship it (and its licence) beside the data; the viewer falls back to the built-in pixel
        /// font when it cannot load it.
        /// </summary>
        public const string UiFontPath = "fonts/Fredoka-SemiBold.ttf";

        /// <summary>The UI font's licence (the OFL requires it to travel with the font).</summary>
        public const string UiFontLicensePath = "fonts/OFL.txt";

        private GameContent()
        {
        }

        /// <summary>The content root, for messages (<see cref="IContentSource.Location"/>).</summary>
        public string Root { get; private set; }

        /// <summary>Where the content was read from; the sprite atlas opens the PNGs through it.</summary>
        public IContentSource Source { get; private set; }

        public BeastRosterData Roster { get; private set; }

        /// <summary>Every beast species, built, in roster order (the pickers list them in this order).</summary>
        public List<CreatureSpeciesSO> Species { get; private set; }

        public SkillLibraryData SkillLibrary { get; private set; }

        public EnemyLibraryData EnemyLibrary { get; private set; }

        /// <summary>The battlefield obstacles of each region's backdrops (battle-layouts.json), also on <see cref="BattleContent.Layouts"/>.</summary>
        public BattleLayoutData Layouts
        {
            get { return Battle == null ? null : Battle.Layouts; }
        }

        /// <summary>The campaign regions (regions.json): what an enemy's per-region art is keyed by.</summary>
        public RegionLibraryData Regions { get; private set; }

        public BattleContent Battle { get; private set; }

        public EnemyCatalog Enemies { get; private set; }

        public EncounterLibrary Encounters { get; private set; }

        public VfxLibrary Vfx { get; private set; }

        public ArtManifestData Art { get; private set; }

        /// <summary>How the board is dressed (<see cref="BattleArtData.ProjectRelativePath"/>): painted backdrops per region and arena, and their overlays.</summary>
        public BattleArtData BattleArt { get; private set; }

        /// <summary>The battle glossary (<see cref="GlossaryData.ProjectRelativePath"/>): the terms the skill card highlights.</summary>
        public Glossary Glossary { get; private set; }

        /// <summary>Every skill id a skill can have: beast skills, avatar actives and enemy-library skills.</summary>
        public HashSet<string> KnownSkillIds { get; private set; }

        /// <summary>The campaign's regions, seals and map rules, built (<see cref="RegionLibrary"/>): what the region map and <see cref="CampaignRules"/> read.</summary>
        public RegionLibrary Campaign { get; private set; }

        /// <summary>The region map's location names (location-names.json), resolved from each node's <c>LabelKey</c>.</summary>
        public LocationNameTable LocationNames { get; private set; }

        /// <summary>The material and gold drop tables (drop-tables.json), built with the skill library's material tiers: what a clear pays out.</summary>
        public DropTable Drops { get; private set; }

        /// <summary>The economy: gear, consumables and cosmetics (the reward modifiers' gear and looks, and the consumables a battle may use).</summary>
        public EconomyContent Economy { get; private set; }

        /// <summary>The idle (AFK) rewards (idle-rewards.json and what a claim pays out of), for <see cref="IdleRewardCalculator"/>.</summary>
        public IdleContent Idle { get; private set; }

        /// <summary>The UI toolkit's house style (<see cref="UiStyleData.ProjectRelativePath"/>): colours, panels, buttons and text sizes.</summary>
        public UiStyle Style { get; private set; }

        /// <summary>A file of the content root by its ProjectRelativePath (<c>content/...</c>).</summary>
        public static string PathOf(string root, string projectRelativePath)
        {
            return Path.Combine(root, RelativeOf(projectRelativePath).Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>A ProjectRelativePath (<c>content/...</c>) as a content-root-relative path with forward slashes.</summary>
        public static string RelativeOf(string projectRelativePath)
        {
            return projectRelativePath.StartsWith(ProjectPrefix, StringComparison.Ordinal)
                       ? projectRelativePath.Substring(ProjectPrefix.Length)
                       : projectRelativePath;
        }

        /// <summary>
        /// The content root: <paramref name="explicitRoot"/> when given, else the first of
        /// <c>Content/</c> beside the executable, or the repo's <c>content/</c> walking up
        /// from the working directory or the executable. Null when none holds the roster.
        /// </summary>
        public static string FindRoot(string explicitRoot = null)
        {
            if (!string.IsNullOrEmpty(explicitRoot))
            {
                return Path.GetFullPath(explicitRoot);
            }

            string beside = Path.Combine(AppContext.BaseDirectory, "Content");
            if (File.Exists(PathOf(beside, BeastRosterData.ProjectRelativePath)))
            {
                return beside;
            }

            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, "content");
                    if (File.Exists(PathOf(candidate, BeastRosterData.ProjectRelativePath)))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Loads and validates everything under <paramref name="root"/>. Returns null and fills
        /// <paramref name="errors"/> when a file is missing, unreadable or invalid.
        /// </summary>
        public static GameContent Load(string root, List<string> errors)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                errors.Add("No content root (looked for Content/ beside the app and content/ above it).");
                return null;
            }

            return Load(new FileContentSource(root), errors);
        }

        /// <summary>
        /// Loads and validates everything <paramref name="root"/> holds. Returns null and fills
        /// <paramref name="errors"/> when a file is missing, unreadable or invalid.
        /// </summary>
        public static GameContent Load(IContentSource root, List<string> errors)
        {
            BeastRosterData roster = Read<BeastRosterData>(root, BeastRosterData.ProjectRelativePath, errors);
            SkillLibraryData skills = Read<SkillLibraryData>(root, SkillLibraryData.ProjectRelativePath, errors);
            EnemyLibraryData enemyLibrary = Read<EnemyLibraryData>(root, EnemyLibraryData.ProjectRelativePath, errors);
            EncounterLibraryData encounterLibrary = Read<EncounterLibraryData>(root, EncounterLibraryData.ProjectRelativePath, errors);
            EncounterDifficultyData difficulty = Read<EncounterDifficultyData>(root, EncounterDifficultyData.ProjectRelativePath, errors);
            DropTableData dropTables = Read<DropTableData>(root, DropTableData.ProjectRelativePath, errors);
            VfxLibraryData vfx = Read<VfxLibraryData>(root, VfxLibraryData.ProjectRelativePath, errors);
            ArtManifestData art = ArtManifestData.Normalize(Read<ArtManifestData>(root, ArtManifestData.ProjectRelativePath, errors));
            GlossaryData glossaryData = Read<GlossaryData>(root, GlossaryData.ProjectRelativePath, errors);
            RegionLibraryData regions = Read<RegionLibraryData>(root, RegionLibraryData.ProjectRelativePath, errors);
            BattleArtData battleArt = Read<BattleArtData>(root, BattleArtData.ProjectRelativePath, errors);
            BattleLayoutData layouts = Read<BattleLayoutData>(root, BattleLayoutData.ProjectRelativePath, errors);
            LocationNameTableData locationNames = Read<LocationNameTableData>(root, LocationNameTableData.ProjectRelativePath, errors);
            GearLibraryData gearData = Read<GearLibraryData>(root, GearLibraryData.ProjectRelativePath, errors);
            ConsumableLibraryData consumableData = Read<ConsumableLibraryData>(root, ConsumableLibraryData.ProjectRelativePath, errors);
            CosmeticLibraryData cosmeticData = Read<CosmeticLibraryData>(root, CosmeticLibraryData.ProjectRelativePath, errors);
            UiStyleData style = Read<UiStyleData>(root, UiStyleData.ProjectRelativePath, errors);
            IdleRewardsData idleData = Read<IdleRewardsData>(root, IdleRewardsData.ProjectRelativePath, errors);
            if (errors.Count > 0)
            {
                return null;
            }

            Prefix(errors, "pixel-art-manifest.json", ArtManifestValidator.Validate(art));

            Prefix(errors, "beast-roster.json", BeastRosterValidator.Validate(roster));
            Prefix(errors, "skill-library.json", SkillLibraryValidator.Validate(skills, roster));
            Prefix(errors, "enemy-library.json", EnemyLibraryValidator.Validate(enemyLibrary, roster, regions));
            Prefix(errors, "encounter-library.json", EncounterLibraryValidator.Validate(encounterLibrary, enemyLibrary, dropTables));
            Prefix(errors, "encounter-difficulty.json", EncounterDifficultyTable.Validate(difficulty, encounterLibrary));
            Prefix(errors, "battle-layouts.json", ObstacleLayoutValidator.Validate(layouts, regions, encounterLibrary, enemyLibrary));
            Prefix(errors, "regions.json", RegionLibraryValidator.Validate(regions, encounterLibrary));
            Prefix(errors, "location-names.json", LocationNameTableValidator.Validate(locationNames, regions));
            Prefix(errors, "drop-tables.json", DropTableValidator.Validate(dropTables, skills.Materials));
            Prefix(errors, "gear-library.json", GearLibraryValidator.Validate(gearData));
            Prefix(errors, "consumable-library.json", ConsumableLibraryValidator.Validate(consumableData));
            Prefix(errors, "cosmetic-library.json", CosmeticLibraryValidator.Validate(cosmeticData));
            Prefix(errors, "ui-style.json", UiStyleValidator.Validate(style));
            Prefix(errors, "idle-rewards.json", IdleRewardsValidator.Validate(idleData, dropTables));

            HashSet<string> known = KnownSkills(skills, enemyLibrary);
            Prefix(errors, "vfx-library.json", VfxLibraryValidator.Validate(vfx, known, art));
            Prefix(errors, "art keys", ArtReferenceValidator.Validate(roster, enemyLibrary, skills, art));
            Prefix(errors, "battle-art.json", BattleArtValidator.Validate(battleArt, regions, art));
            Prefix(errors, "battle-art.json", BattleArtValidator.ValidateLayouts(battleArt, layouts));
            Prefix(errors, "glossary.json", GlossaryValidator.Validate(glossaryData));
            Glossary glossary = Glossary.Build(glossaryData);
            Prefix(errors, "skill text", GlossaryValidator.ValidateText(glossary, skills, enemyLibrary));
            if (errors.Count > 0)
            {
                return null;
            }

            List<CreatureSpeciesSO> species = BeastRosterBuilder.BuildAll(roster, out Dictionary<string, GrowthRateCurve> curves);
            Dictionary<string, SkillSO> built = new Dictionary<string, SkillSO>(StringComparer.Ordinal);
            foreach (SkillData data in skills.BeastSkills)
            {
                built[data.SkillId] = BuildSkill(data);
            }

            foreach (SkillData data in skills.AvatarActives)
            {
                built[data.SkillId] = BuildSkill(data);
            }

            foreach (CreatureSpeciesSO beast in species)
            {
                SpeciesKitData kit = Array.Find(skills.SpeciesKits, k => k.SpeciesId == beast.SpeciesId);
                foreach (string skillId in kit == null ? new string[0] : kit.DefaultLoadout)
                {
                    beast.DefaultLoadout.Add(built[skillId]);
                }
            }

            List<PassiveSkillSO> passives = new List<PassiveSkillSO>();
            foreach (PassiveData data in skills.AvatarPassives)
            {
                PassiveSkillSO passive = new PassiveSkillSO();
                SkillLibraryBuilder.ApplyPassive(data, passive);
                passive.name = data.PassiveId;
                passives.Add(passive);
            }

            List<TeamBondSO> bonds = new List<TeamBondSO>();
            foreach (TeamBondData data in skills.TeamBonds ?? new TeamBondData[0])
            {
                TeamBondSO bond = new TeamBondSO();
                SkillLibraryBuilder.ApplyTeamBond(data, bond);
                bond.name = data.BondId;
                bonds.Add(bond);
            }

            curves.TryGetValue(enemyLibrary.GrowthCurveId ?? string.Empty, out GrowthRateCurve enemyCurve);
            EnemyCatalog enemies = EnemyCatalog.Build(enemyLibrary, enemyCurve);
            GearLibrary gear = GearLibrary.Build(gearData);
            ConsumableLibrary consumables = ConsumableLibrary.Build(consumableData);
            EconomyContent economy = new EconomyContent { Gear = gear, Consumables = consumables, Cosmetics = CosmeticLibrary.Build(cosmeticData) };
            RegionLibrary campaign = RegionLibrary.Build(regions);
            DropTable drops = DropTableBuilder.Build(dropTables, DropTableBuilder.TierLookup(skills.Materials));

            return new GameContent
            {
                Root = root.Location,
                Source = root,
                Roster = roster,
                Species = species,
                SkillLibrary = skills,
                EnemyLibrary = enemyLibrary,
                Regions = regions,
                Enemies = enemies,
                Battle = new BattleContent(species, built.Values, passives, bonds, gear.BeastGearAssets, gear.AvatarGearAssets, enemies, consumables.All)
                {
                    Layouts = layouts
                },
                Encounters = EncounterLibrary.Build(encounterLibrary, EncounterDifficultyTable.Build(difficulty)),
                Vfx = VfxLibrary.Build(vfx),
                Art = art,
                BattleArt = battleArt,
                Glossary = glossary,
                KnownSkillIds = known,
                Campaign = campaign,
                LocationNames = LocationNameTable.Build(locationNames),
                Drops = drops,
                Idle = new IdleContent { Rewards = IdleRewardsBuilder.Build(idleData), DropTable = drops, Cosmetics = economy.Cosmetics, Regions = campaign },
                Economy = economy,
                Style = UiStyle.Build(style)
            };
        }

        /// <summary>Every skill id: beast skills, avatar actives and the enemy library's skills.</summary>
        public static HashSet<string> KnownSkills(SkillLibraryData skills, EnemyLibraryData enemies)
        {
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (SkillData data in skills.BeastSkills ?? new SkillData[0])
            {
                known.Add(data.SkillId);
            }

            foreach (SkillData data in skills.AvatarActives ?? new SkillData[0])
            {
                known.Add(data.SkillId);
            }

            foreach (EnemyData enemy in enemies.Enemies ?? new EnemyData[0])
            {
                foreach (SkillData data in enemy.Skills ?? new SkillData[0])
                {
                    known.Add(data.SkillId);
                }
            }

            return known;
        }

        private static SkillSO BuildSkill(SkillData data)
        {
            SkillSO skill = new SkillSO();
            SkillLibraryBuilder.ApplySkill(data, skill);
            skill.name = data.SkillId;
            return skill;
        }

        private static T Read<T>(IContentSource root, string projectRelativePath, List<string> errors) where T : class
        {
            string relative = RelativeOf(projectRelativePath);
            string path = root.Describe(relative);
            if (!root.Exists(relative))
            {
                errors.Add("Missing " + path + ".");
                return null;
            }

            try
            {
                string json;
                using (StreamReader reader = new StreamReader(root.Open(relative)))
                {
                    json = reader.ReadToEnd();
                }

                T data = FieldJson.FromJson<T>(json);
                if (data == null)
                {
                    errors.Add(path + " is empty.");
                }

                return data;
            }
            catch (Exception exception)
            {
                errors.Add("Could not read " + path + ": " + exception.Message);
                return null;
            }
        }

        private static void Prefix(List<string> errors, string file, List<string> found)
        {
            foreach (string error in found)
            {
                errors.Add(file + ": " + error);
            }
        }
    }
}
