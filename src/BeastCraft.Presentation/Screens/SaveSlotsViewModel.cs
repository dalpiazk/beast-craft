using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One card of the slot list.</summary>
    public sealed class SaveSlotRow
    {
        public string Slot;

        /// <summary>"Slot 1".</summary>
        public string Title;

        /// <summary>"Beastbinder level 12, 5 beasts", "Empty", or why the save cannot be loaded.</summary>
        public string Detail;

        /// <summary>"Verdant Hollow, saved 29 Sep 2026 14:05" (either half may be missing); null for an empty slot.</summary>
        public string Where;

        public bool HasSave;

        /// <summary>Whether the save loads (Continue and Export are offered).</summary>
        public bool Readable;

        /// <summary>The slot the session is on (the last one played or started this run).</summary>
        public bool Current;

        /// <summary>The play button's text: "Continue" for a save that loads, else "New game".</summary>
        public string PlayText;
    }

    /// <summary>
    /// The save slot list (<see cref="GameSession.SlotIds"/>, three slots), reached from the title:
    /// continue a slot, start a new game in one (the screen asks first when that replaces a save),
    /// delete one (the screen asks first), and export a slot to a file or import one into it through
    /// the host's <see cref="ISaveTransfer"/> (hidden when the host has none). Import validates the
    /// file before anything is written (<see cref="GameSession.ImportSlot"/>). Engine-neutral.
    /// </summary>
    public sealed class SaveSlotsViewModel
    {
        private readonly GameSession _session;
        private readonly ISaveTransfer _transfer;
        private readonly Func<DateTime> _utcNow;

        public SaveSlotsViewModel(GameSession session, ISaveTransfer transfer = null, Func<DateTime> utcNow = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _transfer = transfer;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        /// <summary>Whether Export and Import are offered.</summary>
        public bool CanTransfer
        {
            get { return _transfer != null; }
        }

        public List<SaveSlotRow> Rows()
        {
            List<SaveSlotRow> rows = new List<SaveSlotRow>();
            foreach (string slot in GameSession.SlotIds)
            {
                SaveSlotSummary summary = _session.DescribeSlot(slot);
                SaveSlotRow row = new SaveSlotRow
                {
                    Slot = slot,
                    Title = "Slot " + summary.Number.ToString(CultureInfo.InvariantCulture),
                    HasSave = summary.HasSave,
                    Readable = summary.Readable,
                    Current = string.Equals(slot, _session.Slot, StringComparison.Ordinal) && summary.HasSave,
                    PlayText = summary.Readable ? "Continue" : "New game"
                };
                if (!summary.HasSave)
                {
                    row.Detail = "Empty";
                }
                else if (!summary.Readable)
                {
                    row.Detail = "This save cannot be loaded (" + summary.Problem + ").";
                }
                else
                {
                    row.Detail = "Beastbinder level " + summary.AvatarLevel.ToString(CultureInfo.InvariantCulture) + ", " +
                                 summary.BeastCount.ToString(CultureInfo.InvariantCulture) + (summary.BeastCount == 1 ? " beast" : " beasts");
                    string saved = summary.SavedUtc == DateTime.MinValue ? null : "saved " + summary.SavedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
                    row.Where = summary.RegionName == null ? saved : saved == null ? summary.RegionName : summary.RegionName + ", " + saved;
                }

                rows.Add(row);
            }

            return rows;
        }

        /// <summary>Whether starting a new game in <paramref name="slot"/> replaces a save (the screen asks first).</summary>
        public bool NewGameReplaces(string slot)
        {
            return _session.DescribeSlot(slot).HasSave;
        }

        /// <summary>Continues <paramref name="slot"/>.</summary>
        public LoadOutcome Continue(string slot)
        {
            if (!_session.UseSlot(slot))
            {
                return new LoadOutcome { Success = false, Message = "That is not a save slot." };
            }

            return _session.Continue();
        }

        /// <summary>Moves to <paramref name="slot"/> for a new game (the screen then shows the first pick). False for an unknown slot.</summary>
        public bool StartNew(string slot)
        {
            return _session.UseSlot(slot);
        }

        /// <summary>Deletes <paramref name="slot"/>'s save (after the screen's confirm). Returns the player message.</summary>
        public string Delete(string slot)
        {
            int number = GameSession.IndexOfSlot(slot) + 1;
            return _session.DeleteSlot(slot)
                       ? "Slot " + number.ToString(CultureInfo.InvariantCulture) + " deleted."
                       : "Slot " + number.ToString(CultureInfo.InvariantCulture) + " could not be deleted.";
        }

        /// <summary>Exports <paramref name="slot"/> to a file; <paramref name="message"/> gets the player message (once, maybe later).</summary>
        public void Export(string slot, Action<string> message)
        {
            if (_transfer == null)
            {
                message?.Invoke("Export is not available here.");
                return;
            }

            string json = _session.ExportSlot(slot, out string error);
            if (json == null)
            {
                message?.Invoke("Nothing to export: " + error);
                return;
            }

            _transfer.Export(FolderSaveTransfer.ExportName(slot, _utcNow()), json, result =>
            {
                if (result.Cancelled)
                {
                    return;
                }

                message?.Invoke(result.Success ? "Saved a copy to " + result.Location + "." : "Export failed. " + result.Error);
            });
        }

        /// <summary>
        /// Imports a save file into <paramref name="slot"/> (after the screen's confirm when it holds a
        /// save). The file is checked first; a bad one leaves the slot as it was.
        /// </summary>
        public void Import(string slot, Action<string> message)
        {
            if (_transfer == null)
            {
                message?.Invoke("Import is not available here.");
                return;
            }

            int number = GameSession.IndexOfSlot(slot) + 1;
            _transfer.Import(result =>
            {
                if (result.Cancelled)
                {
                    return;
                }

                if (!result.Success)
                {
                    message?.Invoke("Import failed. " + result.Error);
                    return;
                }

                message?.Invoke(_session.ImportSlot(slot, result.Contents, out string error)
                                    ? "Imported into slot " + number.ToString(CultureInfo.InvariantCulture) + "."
                                    : "That file is not a save this game can load (" + error + "). Slot " + number.ToString(CultureInfo.InvariantCulture) +
                                      " is unchanged.");
            });
        }
    }
}
