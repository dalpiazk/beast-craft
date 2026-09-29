using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Idle;
using BeastCraft.Presentation.Content;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Skills;
using BeastCraft.Tutorial;

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
        Background,

        /// <summary>Idle rewards were claimed (on Continue, on resume, or from the map's idle chip).</summary>
        IdleClaim,

        /// <summary>A beast's skills or gear were changed on its detail screen (a swap, an upgrade, gear on or off).</summary>
        BeastEdit,

        /// <summary>A player-facing change outside a battle: an equipped title, a look bought with tokens.</summary>
        PlayerEdit
    }

    /// <summary>
    /// The clocks the idle rewards are measured with: the wall clock (UTC) and a monotonic clock
    /// (time since the device booted, which the player cannot set; <see cref="IdleRewardCalculator"/>).
    /// </summary>
    public interface IGameClock
    {
        DateTime UtcNow { get; }

        /// <summary>Time since boot (negative: not available).</summary>
        TimeSpan Monotonic { get; }
    }

    /// <summary>
    /// The device's clocks: <see cref="DateTime.UtcNow"/> and, for the monotonic one, the host's
    /// (Android's <c>elapsedRealtime</c>, which counts deep sleep) or else <see cref="Stopwatch"/>'s
    /// timestamp (time since boot on desktop).
    /// </summary>
    public sealed class SystemGameClock : IGameClock
    {
        private readonly Func<TimeSpan> _monotonic;

        public SystemGameClock(Func<TimeSpan> monotonic = null)
        {
            _monotonic = monotonic;
        }

        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        public TimeSpan Monotonic
        {
            get { return _monotonic != null ? _monotonic() : TimeSpan.FromSeconds(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency); }
        }
    }

    /// <summary>A clock that moves only when told (tests, scripted runs).</summary>
    public sealed class ManualGameClock : IGameClock
    {
        public ManualGameClock(DateTime utcNow, TimeSpan monotonic)
        {
            UtcNow = utcNow;
            Monotonic = monotonic;
        }

        public DateTime UtcNow { get; private set; }

        public TimeSpan Monotonic { get; private set; }

        public void Advance(TimeSpan by)
        {
            UtcNow += by;
            Monotonic += by;
        }
    }

    /// <summary>What an idle claim paid, for the toast.</summary>
    public sealed class IdleClaimView
    {
        public IdleClaimResult Result;
        public int Gold;
        public int Xp;
        public int Levels;
        public int Materials;
        public string Look;

        /// <summary>The toast text; null when nothing was paid (a claim of nothing is silent).</summary>
        public string Message;
    }

    /// <summary>The map's idle chip: how long the rewards have been piling up, or that they are full.</summary>
    public sealed class IdleStatusView
    {
        public bool Started;
        public double Hours;
        public int CapHours;
        public bool Capped;

        /// <summary>Whether a claim now would pay anything (time has passed and there is a progress level).</summary>
        public bool Claimable;

        public string Text;
    }

    /// <summary>What <see cref="GameSession.Continue"/> found.</summary>
    public sealed class LoadOutcome
    {
        public bool Success;

        /// <summary>The main save could not be used and the backup (<c>.bak</c>) was loaded instead.</summary>
        public bool FromBackup;

        /// <summary>A message for the player (the backup notice, or why nothing could be loaded); null when all was well.</summary>
        public string Message;

        /// <summary>
        /// The consumables handed back because the last battle never finished (the app closed or
        /// crashed mid-battle; <see cref="Economy.BattleConsumableRefund"/>), for a toast; null when none.
        /// </summary>
        public string RefundMessage;
    }

    /// <summary>One save slot as the slot list shows it (<see cref="GameSession.DescribeSlot"/>).</summary>
    public sealed class SaveSlotSummary
    {
        /// <summary>The slot id (<see cref="GameSession.SlotIds"/>).</summary>
        public string Slot;

        /// <summary>1-based, for "Slot 1".</summary>
        public int Number;

        /// <summary>Whether the slot holds a save (readable or not).</summary>
        public bool HasSave;

        /// <summary>Whether the save loads (<see cref="Problem"/> says why not).</summary>
        public bool Readable;

        /// <summary>When the save was last written (UTC); <see cref="DateTime.MinValue"/> when the storage does not say.</summary>
        public DateTime SavedUtc;

        public int AvatarLevel;

        public int BeastCount;

        /// <summary>The region being played, or null.</summary>
        public string RegionName;

        /// <summary>Why the save does not load; null when it does.</summary>
        public string Problem;
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
        /// <summary>The first save slot: the one a session starts on.</summary>
        public const string SlotName = "slot1";

        /// <summary>How many save slots the game offers (<see cref="SlotIds"/>).</summary>
        public const int SlotCount = 3;

        /// <summary>The save slots, in order: <c>slot1</c> to <c>slot3</c> (<see cref="SlotName"/> first).</summary>
        public static readonly IReadOnlyList<string> SlotIds = new[] { SlotName, "slot2", "slot3" };

        /// <summary>
        /// How many beasts a campaign battle fields beside the Beastbinder
        /// (<see cref="CampaignRules.PartySize"/>: three, the size the difficulty is calibrated with).
        /// </summary>
        public const int PartySize = CampaignRules.PartySize;

        private readonly ISaveStorage _storage;
        private readonly SaveStore _store;
        private readonly PlayerSettingsStore _settingsStore;
        private readonly Func<int> _seeds;

        /// <param name="content">The loaded content (regions, drops, economy included).</param>
        /// <param name="storage">Where the save and the settings live.</param>
        /// <param name="seeds">Where new expedition map seeds come from (the clock in the game; fixed in tests).</param>
        /// <param name="clock">The clocks idle rewards are measured with (the device's by default).</param>
        public GameSession(GameContent content, ISaveStorage storage, Func<int> seeds = null, IGameClock clock = null)
        {
            Clock = clock ?? new SystemGameClock();
            Content = content ?? throw new ArgumentNullException(nameof(content));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            JsonSaveSerializer json = new JsonSaveSerializer(true);
            _store = new SaveStore(storage, new SaveSerializer(json));
            _settingsStore = new PlayerSettingsStore(storage, json);
            _seeds = seeds ?? (() => Environment.TickCount);
            Settings = _settingsStore.Load();
        }

        public GameContent Content { get; }

        /// <summary>The clocks idle rewards are measured with.</summary>
        public IGameClock Clock { get; set; }

        /// <summary>What the claim on the last Continue paid (its toast), or null.</summary>
        public IdleClaimView LastContinueClaim { get; private set; }

        /// <summary>The app came back from the background: the map claims the idle rewards when it next shows (cleared by every claim).</summary>
        public bool ResumeClaimPending { get; set; }

        /// <summary>The save being played, or null before New Game or Continue.</summary>
        public PlayerSave Save { get; private set; }

        public PlayerSettings Settings { get; private set; }

        /// <summary>The slot New Game, Continue and every autosave use (<see cref="UseSlot"/>).</summary>
        public string Slot { get; private set; } = SlotName;

        /// <summary>Whether the current <see cref="Slot"/> holds a save.</summary>
        public bool HasSave
        {
            get { return _store.Exists(Slot); }
        }

        /// <summary>Whether any slot holds a save (the title screen's Continue).</summary>
        public bool AnySave
        {
            get
            {
                foreach (string slot in SlotIds)
                {
                    if (_store.Exists(slot))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Switches to <paramref name="slot"/> (one of <see cref="SlotIds"/>). The game in play, if any,
        /// is put down first (it was autosaved as it went), so nothing of it is written into the new
        /// slot. False (nothing changes) for a slot that is not one of the game's.
        /// </summary>
        public bool UseSlot(string slot)
        {
            if (!IsSlot(slot))
            {
                return false;
            }

            if (!string.Equals(slot, Slot, StringComparison.Ordinal))
            {
                Save = null;
                Slot = slot;
            }

            return true;
        }

        /// <summary>Whether <paramref name="slot"/> is one of <see cref="SlotIds"/>.</summary>
        public static bool IsSlot(string slot)
        {
            foreach (string id in SlotIds)
            {
                if (string.Equals(id, slot, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The first slot with no save, or null when every slot holds one.</summary>
        public string FirstEmptySlot()
        {
            foreach (string slot in SlotIds)
            {
                if (!_store.Exists(slot))
                {
                    return slot;
                }
            }

            return null;
        }

        /// <summary>
        /// The slot Continue loads: the most recently written save (when the storage knows write times),
        /// else the first slot that holds one; null when there is none.
        /// </summary>
        public string MostRecentSlot()
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string slot in SlotIds)
            {
                if (!_store.Exists(slot))
                {
                    continue;
                }

                DateTime written = _storage is IBackupSaveStorage files ? files.Read(slot).LastWriteUtc : DateTime.MinValue;
                if (best == null || written > bestTime)
                {
                    best = slot;
                    bestTime = written;
                }
            }

            return best;
        }

        /// <summary>
        /// What <paramref name="slot"/> holds, for the slot list: loaded (and migrated in memory, never
        /// written) to read its summary. Never throws.
        /// </summary>
        public SaveSlotSummary DescribeSlot(string slot)
        {
            SaveSlotSummary summary = new SaveSlotSummary { Slot = slot, Number = IndexOfSlot(slot) + 1 };
            if (!_store.Exists(slot))
            {
                return summary;
            }

            summary.HasSave = true;
            if (_storage is IBackupSaveStorage files)
            {
                summary.SavedUtc = files.Read(slot).LastWriteUtc;
            }

            SaveLoadResult loaded = _store.Load(slot);
            if (!loaded.Success)
            {
                summary.Problem = loaded.Error;
                return summary;
            }

            PlayerSave save = loaded.Save;
            summary.Readable = true;
            summary.AvatarLevel = save.Avatar?.Level ?? 1;
            summary.BeastCount = save.Beasts?.Count ?? 0;
            string regionId = save.Campaign?.ActiveRun != null && save.Campaign.HasActiveRun ? save.Campaign.ActiveRun.RegionId : save.Campaign?.CurrentRegionId;
            summary.RegionName = string.IsNullOrEmpty(regionId) ? null : Content.Campaign.GetRegion(regionId)?.DisplayName;
            return summary;
        }

        /// <summary>
        /// Deletes <paramref name="slot"/>'s save and its backup. When it is the slot in play, the game
        /// is put down (nothing autosaves into the emptied slot). False when there was nothing to delete.
        /// </summary>
        public bool DeleteSlot(string slot)
        {
            if (!IsSlot(slot) || !_store.Exists(slot))
            {
                return false;
            }

            if (string.Equals(slot, Slot, StringComparison.Ordinal))
            {
                Save = null;
            }

            return _storage.Delete(slot);
        }

        /// <summary>
        /// <paramref name="slot"/>'s save as current-schema JSON, for an export file: loaded, migrated and
        /// validated like Continue, so a file written from a backup or an older schema is still a clean,
        /// current save. Null with <paramref name="error"/> when the slot has nothing loadable.
        /// </summary>
        public string ExportSlot(string slot, out string error)
        {
            error = null;
            if (!IsSlot(slot) || !_store.Exists(slot))
            {
                error = "There is no save in that slot.";
                return null;
            }

            SaveLoadResult loaded = _store.Load(slot);
            if (!loaded.Success)
            {
                error = loaded.Error;
                return null;
            }

            return new SaveSerializer(new JsonSaveSerializer(true)).Serialize(loaded.Save);
        }

        /// <summary>
        /// Imports <paramref name="json"/> (an exported save) into <paramref name="slot"/>. The text is
        /// read, migrated and validated first; only a save that loads cleanly is written, so a bad file
        /// never touches the slot (its old save, if any, stays as the slot's backup). When it is the slot
        /// in play, the game is put down so the next Continue reads the import. False with
        /// <paramref name="error"/> (nothing written) otherwise.
        /// </summary>
        public bool ImportSlot(string slot, string json, out string error)
        {
            error = null;
            if (!IsSlot(slot))
            {
                error = "That is not a save slot.";
                return false;
            }

            SaveLoadResult loaded = new SaveSerializer(new JsonSaveSerializer(true)).Deserialize(json);
            if (!loaded.Success)
            {
                error = loaded.Error;
                return false;
            }

            if (!_store.Save(slot, loaded.Save))
            {
                error = "The save could not be written.";
                return false;
            }

            if (string.Equals(slot, Slot, StringComparison.Ordinal))
            {
                Save = null;
            }

            return true;
        }

        /// <summary>0-based position of <paramref name="slot"/> in <see cref="SlotIds"/>, or -1.</summary>
        public static int IndexOfSlot(string slot)
        {
            for (int i = 0; i < SlotIds.Count; i++)
            {
                if (string.Equals(SlotIds[i], slot, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        public int AutosaveCount { get; private set; }

        public AutosaveReason? LastAutosaveReason { get; private set; }

        /// <summary>Whether the last autosave was written.</summary>
        public bool LastAutosaveOk { get; private set; }

        /// <summary>The level New Game's picks join at (<see cref="StarterPicks.JoinLevel"/>; a debug flag may raise it).</summary>
        public int StarterLevel { get; set; } = StarterPicks.JoinLevel;

        /// <summary>The team last taken into a battle this session (the encounter screen starts from it).</summary>
        public List<string> LastTeam { get; } = new List<string>();

        /// <summary>Encounter locations whose team suggestion the player dismissed this session.</summary>
        public HashSet<string> DismissedSuggestions { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Grove/Garden/Board readiness already toasted this "ready" spell (a plot id or destination
        /// id), so <see cref="RefreshGrove"/> does not repeat the toast on every Home visit while the
        /// player has simply not collected it yet; pruned back to what is still ready each call, so a
        /// harvest or collect re-arms it for next time.
        /// </summary>
        private readonly HashSet<string> _groveReadyToasted = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// A brand-new game in Hearthglen (<see cref="StarterPicks.NewGame"/>): the avatar's starting
        /// kit and the New Game pick <paramref name="firstSpeciesId"/>, the Hearthglen expedition
        /// started, saved. False (nothing changes) for a species that is not a legal first pick.
        /// </summary>
        public bool NewGame(string firstSpeciesId)
        {
            PlayerSave save = StarterPicks.NewGame(firstSpeciesId, Content.Species, Content.SkillLibrary, out string _, StarterLevel);
            if (save == null)
            {
                return false;
            }

            StartWith(save);
            return true;
        }

        /// <summary>
        /// A brand-new game that skips Hearthglen (<see cref="StarterPicks.NewGameSkippingTutorial"/>):
        /// the three picks (one per stance, in the stance cycle's order), Hearthglen's completion
        /// rewards, an expedition into the first campaign region, saved. False (nothing changes) when
        /// the picks are not legal.
        /// </summary>
        public bool NewGameSkippingTutorial(IReadOnlyList<string> species)
        {
            PlayerSave save = StarterPicks.NewGameSkippingTutorial(species, Content.Species, Content.SkillLibrary, Content.Campaign, Content.Battle.GetConsumable,
                                                                   out string _, StarterLevel);
            if (save == null)
            {
                return false;
            }

            StartWith(save);
            return true;
        }

        /// <summary>
        /// Plays <paramref name="save"/> as a new game: the session's per-game state reset, an
        /// expedition started where it belongs (<see cref="EnsureExpedition"/>), the idle clock started
        /// (the first claim only starts it), saved. New Game's two paths end here; tests and debug
        /// runs start from a save they built.
        /// </summary>
        public void StartWith(PlayerSave save)
        {
            Save = save ?? throw new ArgumentNullException(nameof(save));
            Save.EnsureInitialized();
            LastTeam.Clear();
            DismissedSuggestions.Clear();
            EnsureExpedition();
            IdleRewardCalculator.Claim(Save, Content.Idle, Clock.UtcNow, Clock.Monotonic, Party());
            EvaluateAchievementsOnSessionStart();
            RefreshGrove();
            Autosave(AutosaveReason.NewGame);
        }

        /// <summary>The pick waiting to be made (1-3; <see cref="StarterPicks.PendingStep"/>), or 0.</summary>
        public int PendingPick
        {
            get { return Save == null ? 0 : StarterPicks.PendingStep(Save, Content.Campaign); }
        }

        /// <summary>
        /// Makes the pending pick (<see cref="StarterPicks.Pick"/>): <paramref name="speciesId"/> joins at
        /// <see cref="StarterLevel"/>, and the game is saved. The result says why when refused.
        /// </summary>
        public PickResult Pick(string speciesId)
        {
            PickResult result = StarterPicks.Pick(Save, Content.Campaign, Content.Species, Content.SkillLibrary, speciesId, StarterLevel);
            if (result.Success)
            {
                Autosave(AutosaveReason.Results);
            }

            return result;
        }

        /// <summary>
        /// A map location's display name: a tutorial location's authored name
        /// (<see cref="FixedNodeData.Name"/>), else the location-name table's (<see cref="LocationNameTable"/>).
        /// </summary>
        public string LocationName(MapNode node)
        {
            if (node == null)
            {
                return string.Empty;
            }

            MapRun run = Save?.Campaign?.ActiveRun;
            FixedNodeData authored = run == null ? null : Content.Campaign.FixedNode(run.RegionId, node.NodeId);
            if (authored != null && !string.IsNullOrEmpty(authored.Name))
            {
                return authored.Name;
            }

            return Content.LocationNames?.Resolve(node) ?? LocationNameTable.FallbackName(node.Kind);
        }

        /// <summary>
        /// Loads the slot. When the main save is corrupt the backup is loaded and
        /// <see cref="LoadOutcome.Message"/> says so; when neither loads, nothing changes and the
        /// message says why. A loaded save with no expedition in progress starts one.
        /// </summary>
        public LoadOutcome Continue()
        {
            SaveLoadResult loaded = _store.Load(Slot);
            if (!loaded.Success)
            {
                return new LoadOutcome { Success = false, Message = "Your save could not be loaded (" + loaded.Error + ")." };
            }

            Save = loaded.Save;
            LastTeam.Clear();
            DismissedSuggestions.Clear();
            bool fromBackup = loaded.StorageSource == SaveFileSource.Backup;
            bool started = !Save.Campaign.HasActiveRun;

            // A battle the app died in (never resolved) hands back what it spent; the refund clears the
            // record, and the autosave below writes that, so it can never pay twice.
            List<string> refunded = BattleConsumableRefund.RefundPending(Save, id => Content.Battle.GetConsumable(id)?.MaxStack);
            EnsureExpedition();

            // Welcome back: the idle rewards are claimed straight away, before achievements are
            // evaluated, so a level the claim itself crosses (ClaimIdle evaluates it too, its own
            // toast folded into LastContinueClaim.Message) is already reflected when the catch-all,
            // once-per-session-start check below runs (idempotent, so it never toasts it twice).
            LastContinueClaim = ClaimIdle();
            bool achievementsChanged = EvaluateAchievementsOnSessionStart();
            bool groveReady = RefreshGrove();
            if (started || fromBackup || achievementsChanged || groveReady || refunded.Count > 0)
            {
                // Keep the main file current: a started expedition, a retroactively earned achievement,
                // a Grove unlock/gift the offline clock just rolled forward, or the restored backup
                // made main again. A refund is written straight away too.
                Autosave(AutosaveReason.Results);
            }

            return new LoadOutcome
            {
                Success = true,
                FromBackup = fromBackup,
                Message = fromBackup
                              ? "Your latest save could not be read (" + (loaded.MainFileProblem ?? "corrupt") + "), so the backup was loaded. A little progress may be lost."
                              : null,
                RefundMessage = RefundText(refunded)
            };
        }

        /// <summary>The refund toast: "Your last battle did not finish, so your Fury Draught was returned." Null for none.</summary>
        private string RefundText(List<string> refunded)
        {
            if (refunded == null || refunded.Count == 0)
            {
                return null;
            }

            List<string> names = refunded.ConvertAll(id => Content.Battle.GetConsumable(id)?.DisplayName ?? id);
            return "Your last battle did not finish, so your " + string.Join(" and ", names) + (names.Count == 1 ? " was" : " were") + " returned.";
        }

        /// <summary>Writes the save to the slot (nothing to do before a game is loaded). Returns whether it was written.</summary>
        public bool Autosave(AutosaveReason reason)
        {
            if (Save == null)
            {
                return false;
            }

            LastAutosaveOk = _store.Save(Slot, Save);
            LastAutosaveReason = reason;
            if (LastAutosaveOk)
            {
                AutosaveCount++;
            }

            return LastAutosaveOk;
        }

        /// <summary>
        /// The party idle XP goes to: the team last taken into battle, else the first
        /// <see cref="PartySize"/> beasts.
        /// </summary>
        public List<string> Party()
        {
            List<string> party = LastTeam.FindAll(id => Save?.FindBeast(id) != null);
            if (party.Count == 0 && Save != null)
            {
                for (int i = 0; i < Save.Beasts.Count && party.Count < PartySize; i++)
                {
                    party.Add(Save.Beasts[i].BeastId);
                }
            }

            return party;
        }

        /// <summary>
        /// Claims the idle rewards now (<see cref="IdleRewardCalculator.Claim"/> with <see cref="Clock"/>
        /// and <see cref="Party"/>, and <see cref="GameContent.Achievements"/> so a beast or avatar level
        /// the claim crosses earns its title straight away — <see cref="ExtraRewardText"/> in the same
        /// message) and autosaves when it paid anything, including a title alone. The view's message is
        /// null when nothing was paid (the first claim only starts the clock; before the first clear
        /// there is no rate). Null before a game is loaded.
        /// </summary>
        public IdleClaimView ClaimIdle()
        {
            if (Save == null)
            {
                return null;
            }

            ResumeClaimPending = false;
            IdleClaimResult result = IdleRewardCalculator.Claim(Save, Content.Idle, Clock.UtcNow, Clock.Monotonic, Party(), Content.Achievements);
            IdleClaimView view = new IdleClaimView { Result = result, Gold = result.GoldGained, Look = result.CosmeticDropped };
            foreach (KeyValuePair<string, int> offered in result.XpOffered)
            {
                int percent = result.FalloffPercent.TryGetValue(offered.Key, out int p) ? p : 100;
                view.Xp += offered.Value * percent / 100;
            }

            foreach (int levels in result.LevelsGained.Values)
            {
                view.Levels += levels;
            }

            foreach (MaterialStack stack in result.Loot.Drops)
            {
                view.Materials += stack.Quantity;
            }

            List<string> parts = new List<string>();
            if (view.Gold > 0)
            {
                parts.Add("+" + view.Gold + " gold");
            }

            if (view.Xp > 0)
            {
                parts.Add("+" + view.Xp + " XP" + (view.Levels > 0 ? " (" + view.Levels + (view.Levels == 1 ? " level up)" : " level ups)") : string.Empty));
            }

            if (view.Materials > 0)
            {
                parts.Add(view.Materials + (view.Materials == 1 ? " material" : " materials"));
            }

            if (view.Look != null)
            {
                parts.Add("a new look");
            }

            string extra = ExtraRewardText(result.TitlesEarned, 0);
            if (parts.Count > 0)
            {
                view.Message = "While you were away: " + string.Join(", ", parts) + (result.Capped ? " (idle was full)" : string.Empty) + "." + extra;
                Autosave(AutosaveReason.IdleClaim);
            }
            else if (extra.Length > 0)
            {
                // Nothing else to report (a beast at its level cap, say) but a title was still earned.
                view.Message = extra.Trim();
                Autosave(AutosaveReason.IdleClaim);
            }

            return view;
        }

        /// <summary>The map's idle chip: see <see cref="IdleStatusView"/>. Changes nothing.</summary>
        public IdleStatusView IdleStatus()
        {
            IdleStatusView status = new IdleStatusView { Text = "Idle" };
            if (Save == null || Content.Idle?.Rewards == null)
            {
                return status;
            }

            IdleClaimPreview preview = IdleRewardCalculator.Preview(Save, Content.Idle, Clock.UtcNow, Clock.Monotonic);
            status.Started = Save.Idle.HasStarted;
            status.Hours = preview.Hours;
            status.CapHours = preview.CapHours;
            status.Capped = preview.Capped;
            status.Claimable = status.Started && preview.ProgressLevel > 0 && (preview.Gold > 0 || preview.XpPerPartyBeast > 0);
            int minutes = (int)Math.Floor(preview.Hours * 60.0);
            if (preview.ProgressLevel <= 0)
            {
                status.Text = "Idle: win a battle to start";
            }
            else if (status.Capped)
            {
                status.Text = "Idle full (" + preview.CapHours + "h)";
            }
            else
            {
                status.Text = "Idle " + (minutes >= 60 ? minutes / 60 + "h " + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + "m" : minutes + "m");
            }

            return status;
        }

        /// <summary>When the idle rewards will reach their cap (UTC), for a notification; null before the clock starts.</summary>
        public DateTime? IdleCapUtc()
        {
            if (Save == null || Save.Idle == null || !Save.Idle.HasStarted || Content.Idle?.Rewards == null)
            {
                return null;
            }

            return new DateTime(Save.Idle.LastClaimUtcTicks, DateTimeKind.Utc).AddHours(Content.Idle.Rewards.CapHours);
        }

        public bool SaveSettings()
        {
            return _settingsStore.Save(Settings);
        }

        /// <summary>
        /// Makes sure an expedition is in progress: when none is, starts one (a new map seed) into
        /// Hearthglen while it is not behind the player, else the first unlocked region whose boss
        /// still stands, else the region last played.
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
            RegionData tutorial = Content.Campaign.Tutorial;
            if (tutorial != null && Save?.Tutorial != null && !Save.Tutorial.HearthglenCleared && Save.Campaign.IsUnlocked(tutorial.RegionId))
            {
                return tutorial.RegionId;
            }

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

        // ------------------------------------------------------------------ The Grove (docs/design/grove.md, D4)

        /// <summary>
        /// Refreshes the Grove's live-condition unlocks (<see cref="GroveRules.RefreshUnlocks"/>) and
        /// rolls every owned beast's gift clock forward (<see cref="GroveRules.RefreshGifts"/>), then
        /// checks the Wildgarden's plots and the Board's expeditions for readiness, queuing one
        /// consolidated <see cref="PendingToasts"/> entry the first time something new is ready since
        /// the last check (never repeated while it just sits there uncollected — see
        /// <see cref="_groveReadyToasted"/>). Idempotent and safe to call often (session start, every
        /// time Home shows): nothing here is ever lost by checking late, the same "banked, not spent"
        /// stance every Grove timer already has. Does nothing before a game is loaded. Returns whether
        /// anything new became ready.
        /// </summary>
        public bool RefreshGrove()
        {
            if (Save == null)
            {
                return false;
            }

            Save.EnsureInitialized();
            GroveRules.RefreshUnlocks(Save, Content.GroveLibrary);
            bool somethingNew = false;
            foreach (OwnedBeast beast in Save.Beasts)
            {
                if (beast != null && GroveRules.RefreshGifts(Save, Content.GroveLibrary, beast.BeastId, Clock.UtcNow, Clock.Monotonic) > 0)
                {
                    somethingNew = true;
                }
            }

            HashSet<string> stillReady = new HashSet<string>(StringComparer.Ordinal);
            foreach (PlotState plot in Save.Garden.Plots)
            {
                if (plot == null || !GardenRules.IsReady(Content.GardenLibrary, plot, Clock.UtcNow, Clock.Monotonic))
                {
                    continue;
                }

                string key = "plot:" + plot.PlotId.ToString(CultureInfo.InvariantCulture);
                stillReady.Add(key);
                somethingNew |= _groveReadyToasted.Add(key);
            }

            foreach (ActiveExpedition active in Save.Expeditions.Active)
            {
                if (active == null || string.IsNullOrEmpty(active.DestinationId) || !ExpeditionRules.IsReturned(Content.ExpeditionLibrary, active, Clock.UtcNow, Clock.Monotonic))
                {
                    continue;
                }

                string key = "expedition:" + active.DestinationId;
                stillReady.Add(key);
                somethingNew |= _groveReadyToasted.Add(key);
            }

            _groveReadyToasted.IntersectWith(stillReady);
            if (somethingNew)
            {
                PendingToasts.Add("Something is ready in the Grove.");
            }

            return somethingNew;
        }

        // ------------------------------------------------------------------ The discovery layer

        /// <summary>
        /// Messages for the map to toast when it next shows (a region's 100% reward earned in a battle, a
        /// visit or a Kinship choice); the map drains them.
        /// </summary>
        public List<string> PendingToasts { get; } = new List<string>();

        /// <summary>Whether a won Kinship trial's choice is waiting (<see cref="KinshipRules.Pending"/>).</summary>
        public bool PendingKinship
        {
            get { return Save != null && Save.Discovery.HasPendingKinship && Content.Discovery.Library.Site(Save.Discovery.PendingKinshipId) != null; }
        }

        /// <summary>
        /// Grants <paramref name="regionId"/>'s (default: the region last played) 100% exploration reward
        /// when it has just been earned (<see cref="DiscoveryRules.TryComplete"/>) and queues its toast.
        /// Returns the reward, or null.
        /// </summary>
        public CompletionReward CheckCompletion(string regionId = null)
        {
            if (Save == null)
            {
                return null;
            }

            regionId = regionId ?? (Save.Campaign.HasActiveRun ? Save.Campaign.ActiveRun.RegionId : Save.Campaign.CurrentRegionId);
            CompletionReward reward = DiscoveryRules.TryComplete(Save, Content.Discovery, regionId);
            if (reward != null)
            {
                string region = Content.Campaign.GetRegion(regionId)?.DisplayName ?? regionId;
                List<string> parts = new List<string>();
                if (reward.Look != null)
                {
                    parts.Add("the " + LookName(reward.Look));
                }

                if (reward.Gold > 0)
                {
                    parts.Add(reward.Gold + " gold");
                }

                string earned = parts.Count > 0 ? "You earned " + string.Join(" and ", parts) + "." : string.Empty;
                PendingToasts.Add(region + " fully explored! " + earned + ExtraRewardText(reward.TitlesEarned, reward.LookTokens));
            }

            return reward;
        }

        /// <summary>A look's display name from its cosmetic key.</summary>
        public string LookName(string key)
        {
            return Content.Economy?.Cosmetics?.GetOption(key)?.DisplayName ?? key;
        }

        /// <summary>
        /// Evaluates achievements once against the save's current state (New Game and Continue), so a
        /// save retroactively earns whatever it already meets — e.g. an older save migrated to schema 9,
        /// or a level or Kinship count reached between sessions — rather than only from a discovery
        /// visit, a Kinship join or a resolved battle. Idempotent (<see cref="AchievementRules.Evaluate"/>);
        /// newly earned titles queue one consolidated toast (<see cref="PendingToasts"/>, shown when the
        /// map next appears) instead of nothing, so the player is told even though nothing they just did
        /// triggered it. Returns whether anything was newly earned (the caller autosaves when it did).
        /// </summary>
        private bool EvaluateAchievementsOnSessionStart()
        {
            if (Save == null)
            {
                return false;
            }

            List<AchievementData> earned = AchievementRules.Evaluate(Save, Content.Achievements);
            if (earned.Count == 0)
            {
                return false;
            }

            List<string> titles = earned.ConvertAll(a => a.TitleText);
            PendingToasts.Add((titles.Count == 1 ? "New title earned: " : "New titles earned: ") + string.Join(", ", titles) + ".");
            return true;
        }

        /// <summary>
        /// Toast text for titles newly earned and look tokens newly granted, appended to a reward's own
        /// message (<see cref="DiscoveryResult"/>, <see cref="CompletionReward"/>, <see cref="KinshipResult"/>,
        /// <see cref="Campaign.CampaignResult"/> each carry these); "" when there is nothing to add.
        /// </summary>
        public static string ExtraRewardText(List<AchievementData> titlesEarned, int lookTokensGranted)
        {
            List<string> parts = new List<string>();
            foreach (AchievementData title in titlesEarned ?? new List<AchievementData>())
            {
                if (title != null)
                {
                    parts.Add("the title \"" + title.TitleText + "\"");
                }
            }

            if (lookTokensGranted > 0)
            {
                parts.Add(lookTokensGranted + " look token" + (lookTokensGranted == 1 ? string.Empty : "s"));
            }

            return parts.Count == 0 ? string.Empty : " You earned " + string.Join(" and ", parts) + ".";
        }

        /// <summary>
        /// Visits point of interest <paramref name="poiId"/> on the map (<see cref="DiscoveryRules.Visit"/>),
        /// then checks the region's 100% and autosaves. The result says why when refused.
        /// </summary>
        public DiscoveryResult VisitPoi(string poiId)
        {
            DiscoveryResult result = DiscoveryRules.Visit(Save, Content.Discovery, poiId);
            if (result.Success)
            {
                CheckCompletion();
                Autosave(AutosaveReason.Results);
            }

            return result;
        }

        /// <summary>
        /// Makes the pending Kinship choice (<see cref="KinshipRules.Choose"/>): <paramref name="speciesId"/>
        /// joins, the site is claimed, the region's 100% checked, the game saved.
        /// </summary>
        public KinshipResult ChooseKinship(string speciesId)
        {
            KinshipResult result = KinshipRules.Choose(Save, Content.Discovery, speciesId);
            if (result.Success)
            {
                CheckCompletion();
                Autosave(AutosaveReason.Results);
            }

            return result;
        }

        /// <summary>
        /// Replays stage <paramref name="stage"/> of the region in progress (to explore what the fog still
        /// hides): the expedition in progress is abandoned (its stage progress stays; the fog never comes
        /// back) and a new one starts on a new map. Refused beyond the first uncleared stage.
        /// </summary>
        public CampaignResult ReplayStage(int stage)
        {
            if (Save == null || !Save.Campaign.HasActiveRun)
            {
                return null;
            }

            string regionId = Save.Campaign.ActiveRun.RegionId;
            if (stage < 0 || stage > CampaignRules.NextStage(Save, Content.Campaign, regionId))
            {
                return null;
            }

            MapRun previous = Save.Campaign.ActiveRun;
            int oldStage = previous.Stage;
            CampaignRules.Retreat(Save);
            CampaignResult started = CampaignRules.StartRun(Save, Content.Campaign, regionId, stage, _seeds());
            if (!started.Success)
            {
                CampaignRules.StartRun(Save, Content.Campaign, regionId, oldStage, _seeds());
            }

            Autosave(AutosaveReason.Results);
            return started;
        }

        /// <summary>A display name for an owned beast: its species' name.</summary>
        public string BeastName(OwnedBeast beast)
        {
            CreatureSpeciesSO species = beast?.Progress == null ? null : Content.Battle.GetSpecies(beast.Progress.SpeciesId);
            return species?.DisplayName ?? beast?.Progress?.SpeciesId ?? "?";
        }
    }
}
