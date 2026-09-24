using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Avatar;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
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
    /// pixel-art manifest.
    /// <para>
    /// The <em>content root</em> is the folder holding <c>Data/</c> and <c>Art/Pixel/</c>: in the
    /// repo, <c>BeastCraft/Assets/_Project</c>; in a built host, the <c>Content</c> folder it copies
    /// them to. <see cref="FindRoot"/> finds either. Every file is read through an
    /// <see cref="IContentSource"/>, so a host without a file system for its content (Android: APK
    /// assets) loads the same way.
    /// </para>
    /// </summary>
    public sealed class GameContent
    {
        /// <summary>The prefix of every data file's ProjectRelativePath, which a content root already stands for.</summary>
        public const string ProjectPrefix = "Assets/_Project/";

        private GameContent()
        {
        }

        /// <summary>The content root, for messages (<see cref="IContentSource.Location"/>).</summary>
        public string Root { get; private set; }

        /// <summary>Where the content was read from; the sprite atlas opens the PNGs through it.</summary>
        public IContentSource Source { get; private set; }

        public BeastRosterData Roster { get; private set; }

        public SkillLibraryData SkillLibrary { get; private set; }

        public EnemyLibraryData EnemyLibrary { get; private set; }

        public BattleContent Battle { get; private set; }

        public EnemyCatalog Enemies { get; private set; }

        public EncounterLibrary Encounters { get; private set; }

        public VfxLibrary Vfx { get; private set; }

        public PixelArtManifestData Art { get; private set; }

        /// <summary>Every skill id a skill can have: beast skills, avatar actives and enemy-library skills.</summary>
        public HashSet<string> KnownSkillIds { get; private set; }

        /// <summary>A file of the content root by its ProjectRelativePath (<c>Assets/_Project/...</c>).</summary>
        public static string PathOf(string root, string projectRelativePath)
        {
            return Path.Combine(root, RelativeOf(projectRelativePath).Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>A ProjectRelativePath (<c>Assets/_Project/...</c>) as a content-root-relative path with forward slashes.</summary>
        public static string RelativeOf(string projectRelativePath)
        {
            return projectRelativePath.StartsWith(ProjectPrefix, StringComparison.Ordinal)
                       ? projectRelativePath.Substring(ProjectPrefix.Length)
                       : projectRelativePath;
        }

        /// <summary>
        /// The content root: <paramref name="explicitRoot"/> when given, else the first of
        /// <c>Content/</c> beside the executable, or <c>BeastCraft/Assets/_Project</c> walking up
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
                    string candidate = Path.Combine(dir.FullName, "BeastCraft", "Assets", "_Project");
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
                errors.Add("No content root (looked for Content/ beside the app and BeastCraft/Assets/_Project above it).");
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
            PixelArtManifestData art = Read<PixelArtManifestData>(root, PixelArtManifestData.ProjectRelativePath, errors);
            if (errors.Count > 0)
            {
                return null;
            }

            Prefix(errors, "beast-roster.json", BeastRosterValidator.Validate(roster));
            Prefix(errors, "skill-library.json", SkillLibraryValidator.Validate(skills, roster));
            Prefix(errors, "enemy-library.json", EnemyLibraryValidator.Validate(enemyLibrary, roster));
            Prefix(errors, "encounter-library.json", EncounterLibraryValidator.Validate(encounterLibrary, enemyLibrary, dropTables));
            Prefix(errors, "encounter-difficulty.json", EncounterDifficultyTable.Validate(difficulty, encounterLibrary));

            HashSet<string> known = KnownSkills(skills, enemyLibrary);
            Prefix(errors, "vfx-library.json", VfxLibraryValidator.Validate(vfx, known, art));
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

            return new GameContent
            {
                Root = root.Location,
                Source = root,
                Roster = roster,
                SkillLibrary = skills,
                EnemyLibrary = enemyLibrary,
                Enemies = enemies,
                Battle = new BattleContent(species, built.Values, passives, bonds, null, null, enemies),
                Encounters = EncounterLibrary.Build(encounterLibrary, EncounterDifficultyTable.Build(difficulty)),
                Vfx = VfxLibrary.Build(vfx),
                Art = art,
                KnownSkillIds = known
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
