using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>Why the game autosaved (<see cref="GameSession.Autosave"/>).</summary>
    public enum AutosaveReason
    {
        NewGame,

        /// <summary>A battle node was entered (the battle began: its consumable is spent).</summary>
        NodeEntry,

        /// <summary>A battle's results were applied (win or loss: the retry count must persist).</summary>
        Results,

        /// <summary>The app went to the background or is closing (Android pause, desktop close).</summary>
        Background
    }

    /// <summary>What <see cref="GameSession.Continue"/> found.</summary>
    public sealed class LoadOutcome
    {
        public bool Success;

        /// <summary>The main save could not be used and the backup (<c>.bak</c>) was loaded instead.</summary>
        public bool FromBackup;

        /// <summary>A message for the player (the backup notice, or why nothing could be loaded); null when all was well.</summary>
        public string Message;
    }

    /// <summary>
    /// The player's game while the app runs: the content, the loaded <see cref="PlayerSave"/>, the
    /// settings, and the save flow — New Game (<see cref="NewGame"/>), Continue
    /// (<see cref="Continue"/>, falling back to the backup with a message when the main save is
    /// corrupt) and <see cref="Autosave"/> (after a node is entered, after results, and when the app
    /// is backgrounded or closed) into one slot of an <see cref="ISaveStorage"/>:
    /// <see cref="FileSaveStorage"/> under <see cref="SaveLocations"/> in the game (the Android
    /// host's root is the app's files directory), <see cref="MemorySaveStorage"/> in tests and
    /// screenshot runs. Engine-neutral.
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>The one save slot (a slot list comes later).</summary>
        public const string SlotName = "slot1";

        /// <summary>
        /// How many beasts a campaign battle fields: the standard squad fight
        /// (<c>BattleFormat.SmallGroup</c>, the team suggester's default size).
        /// </summary>
        public const int PartySize = 4;

        private readonly ISaveStorage _storage;
        private readonly SaveStore _store;
        private readonly PlayerSettingsStore _settingsStore;
        private readonly Func<int> _seeds;

        /// <param name="content">The loaded content (regions, drops, economy included).</param>
        /// <param name="storage">Where the save and the settings live.</param>
        /// <param name="seeds">Where new expedition map seeds come from (the clock in the game; fixed in tests).</param>
        public GameSession(GameContent content, ISaveStorage storage, Func<int> seeds = null)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            JsonSaveSerializer json = new JsonSaveSerializer(true);
            _store = new SaveStore(storage, new SaveSerializer(json));
            _settingsStore = new PlayerSettingsStore(storage, json);
            _seeds = seeds ?? (() => Environment.TickCount);
            Settings = _settingsStore.Load();
        }

        public GameContent Content { get; }

        /// <summary>The save being played, or null before New Game or Continue.</summary>
        public PlayerSave Save { get; private set; }

        public PlayerSettings Settings { get; private set; }

        /// <summary>Whether the slot holds a save (the title screen's Continue).</summary>
        public bool HasSave
        {
            get { return _store.Exists(SlotName); }
        }

        public int AutosaveCount { get; private set; }

        public AutosaveReason? LastAutosaveReason { get; private set; }

        /// <summary>Whether the last autosave was written.</summary>
        public bool LastAutosaveOk { get; private set; }

        /// <summary>The level New Game's starter beasts start at (<see cref="StarterSave.StartingLevel"/>; a debug flag may raise it).</summary>
        public int StarterLevel { get; set; } = StarterSave.StartingLevel;

        /// <summary>The team last taken into a battle this session (the encounter screen starts from it).</summary>
        public List<string> LastTeam { get; } = new List<string>();

        /// <summary>Encounter locations whose team suggestion the player dismissed this session.</summary>
        public HashSet<string> DismissedSuggestions { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>A brand-new game: the starter beasts and avatar (<see cref="StarterSave"/>), an expedition into the first region, saved.</summary>
        public void NewGame()
        {
            Save = StarterSave.Create(Content, StarterLevel);
            LastTeam.Clear();
            DismissedSuggestions.Clear();
            EnsureExpedition();
            Autosave(AutosaveReason.NewGame);
        }

        /// <summary>
        /// Loads the slot. When the main save is corrupt the backup is loaded and
        /// <see cref="LoadOutcome.Message"/> says so; when neither loads, nothing changes and the
        /// message says why. A loaded save with no expedition in progress starts one.
        /// </summary>
        public LoadOutcome Continue()
        {
            SaveLoadResult loaded = _store.Load(SlotName);
            if (!loaded.Success)
            {
                return new LoadOutcome { Success = false, Message = "Your save could not be loaded (" + loaded.Error + ")." };
            }

            Save = loaded.Save;
            LastTeam.Clear();
            DismissedSuggestions.Clear();
            bool fromBackup = loaded.StorageSource == SaveFileSource.Backup;
            bool started = !Save.Campaign.HasActiveRun;
            EnsureExpedition();
            if (started || fromBackup)
            {
                // Keep the main file current: a started expedition, or the restored backup made main again.
                Autosave(AutosaveReason.Results);
            }

            return new LoadOutcome
            {
                Success = true,
                FromBackup = fromBackup,
                Message = fromBackup
                              ? "Your latest save could not be read (" + (loaded.MainFileProblem ?? "corrupt") + "), so the backup was loaded. A little progress may be lost."
                              : null
            };
        }

        /// <summary>Writes the save to the slot (nothing to do before a game is loaded). Returns whether it was written.</summary>
        public bool Autosave(AutosaveReason reason)
        {
            if (Save == null)
            {
                return false;
            }

            LastAutosaveOk = _store.Save(SlotName, Save);
            LastAutosaveReason = reason;
            if (LastAutosaveOk)
            {
                AutosaveCount++;
            }

            return LastAutosaveOk;
        }

        public bool SaveSettings()
        {
            return _settingsStore.Save(Settings);
        }

        /// <summary>
        /// Makes sure an expedition is in progress: when none is, starts one (a new map seed) into
        /// the first unlocked region whose boss still stands, else the region last played.
        /// </summary>
        public CampaignResult EnsureExpedition()
        {
            if (Save == null || Save.Campaign.HasActiveRun)
            {
                return null;
            }

            return CampaignRules.StartRun(Save, Content.Campaign, NextRegionId(), _seeds());
        }

        /// <summary>The region an expedition starts into: see <see cref="EnsureExpedition"/>.</summary>
        public string NextRegionId()
        {
            foreach (RegionProgress progress in Save?.Campaign?.Regions ?? new List<RegionProgress>())
            {
                if (progress != null && !progress.BossCleared && Content.Campaign.GetRegion(progress.RegionId) != null)
                {
                    return progress.RegionId;
                }
            }

            string current = Save?.Campaign?.CurrentRegionId;
            return string.IsNullOrEmpty(current) ? CampaignProgress.StartingRegionId : current;
        }

        /// <summary>A display name for an owned beast: its species' name.</summary>
        public string BeastName(OwnedBeast beast)
        {
            CreatureSpeciesSO species = beast?.Progress == null ? null : Content.Battle.GetSpecies(beast.Progress.SpeciesId);
            return species?.DisplayName ?? beast?.Progress?.SpeciesId ?? "?";
        }
    }

    /// <summary>
    /// The save a new player starts with. The starter roster is the campaign pacing model's
    /// (<c>Tooling/BalanceSim</c> <c>--mode campaign</c>: three fielded beasts and three on the
    /// bench, all at level 1): Griffin, Phoenix, Golem, Kirin, Treant and Tarasque, each knowing and
    /// wearing its species' default loadout. The avatar starts at level 1 with the skill library's
    /// default loadout (its first three actives and its default passives) learned and equipped.
    /// </summary>
    public static class StarterSave
    {
        public static readonly string[] Species = { "griffin", "phoenix", "golem", "kirin", "treant", "tarasque" };

        public const int StartingLevel = 1;

        public static PlayerSave Create(GameContent content)
        {
            return Create(content, StartingLevel);
        }

        /// <summary>The starter save with the beasts at <paramref name="level"/> (a debug knob: <c>--starter-level</c>).</summary>
        public static PlayerSave Create(GameContent content, int level)
        {
            PlayerSave save = PlayerSave.CreateNew();
            SkillLibraryData library = content.SkillLibrary;
            int next = 1;
            foreach (string speciesId in Species)
            {
                SpeciesKitData kit = Array.Find(library.SpeciesKits ?? new SpeciesKitData[0], k => k != null && k.SpeciesId == speciesId);
                if (kit == null || content.Battle.GetSpecies(speciesId) == null)
                {
                    continue;
                }

                OwnedBeast beast = OwnedBeast.Create("b" + next++, speciesId, Math.Max(1, level));
                for (int slot = 0; slot < kit.DefaultLoadout.Length && slot < beast.Skills.SlotCount; slot++)
                {
                    beast.Skills.Learn(kit.DefaultLoadout[slot]);
                    beast.Skills.Equip(slot, kit.DefaultLoadout[slot]);
                }

                save.Beasts.Add(beast);
            }

            SkillData[] actives = library.AvatarActives ?? new SkillData[0];
            for (int i = 0; i < actives.Length && i < SkillLibraryData.AvatarDefaultActiveCount && i < save.AvatarSkills.Actives.SlotCount; i++)
            {
                save.AvatarSkills.Actives.Learn(actives[i].SkillId);
                save.AvatarSkills.Actives.Equip(i, actives[i].SkillId);
            }

            string[] passives = library.AvatarDefaultPassives ?? new string[0];
            for (int i = 0; i < passives.Length && i < save.AvatarSkills.Passives.SlotCount; i++)
            {
                save.AvatarSkills.Passives.Learn(passives[i]);
                save.AvatarSkills.Passives.Equip(i, passives[i]);
            }

            return save;
        }
    }
}
