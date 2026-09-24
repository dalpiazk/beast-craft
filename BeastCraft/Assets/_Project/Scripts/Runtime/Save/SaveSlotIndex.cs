using System;
using System.Collections.Generic;

namespace BeastCraft.Save
{
    /// <summary>One slot as listed by <see cref="SaveSlotIndex.Build"/>, for a load/continue menu.</summary>
    public class SaveSlotInfo
    {
        public SaveSlotInfo(string slot, bool readable, SaveFileSource source, DateTime lastWriteUtc, int schemaVersion, string problem)
        {
            Slot = slot;
            Readable = readable;
            Source = source;
            LastWriteUtc = lastWriteUtc;
            SchemaVersion = schemaVersion;
            Problem = problem;
        }

        /// <summary>The slot name.</summary>
        public string Slot { get; private set; }

        /// <summary>Whether the storage returned text for the slot (its main file or backup).</summary>
        public bool Readable { get; private set; }

        /// <summary>Which file the listing read; <see cref="SaveFileSource.Backup"/> means the latest save was lost.</summary>
        public SaveFileSource Source { get; private set; }

        /// <summary>When the file read was last written (UTC); <see cref="DateTime.MinValue"/> when unreadable.</summary>
        public DateTime LastWriteUtc { get; private set; }

        /// <summary>The save's <c>SchemaVersion</c> header; 0 when it could not be read.</summary>
        public int SchemaVersion { get; private set; }

        /// <summary>Why the slot is unreadable, or why its main file was skipped; null when clean.</summary>
        public string Problem { get; private set; }
    }

    /// <summary>
    /// Lists a <see cref="FileSaveStorage"/>'s slots with their timestamps and schema versions,
    /// reading only each save's <see cref="SaveSerializer.SaveHeader"/> — no migration or
    /// validation (that happens on <see cref="SaveStore.Load"/>). Never throws on bad files.
    /// </summary>
    public static class SaveSlotIndex
    {
        /// <summary>Every slot in <paramref name="storage"/>, newest write first (ties by name).</summary>
        public static List<SaveSlotInfo> Build(FileSaveStorage storage, ISaveJsonSerializer json)
        {
            if (storage == null)
            {
                throw new ArgumentNullException(nameof(storage));
            }

            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            List<SaveSlotInfo> slots = new List<SaveSlotInfo>();

            foreach (string slot in storage.ListSlots())
            {
                SaveFileResult read = storage.Read(slot);

                if (!read.Success)
                {
                    slots.Add(new SaveSlotInfo(slot, false, SaveFileSource.None, DateTime.MinValue, 0, read.Error));
                    continue;
                }

                int version = 0;
                string problem = read.MainFileProblem == null ? null : "Main file " + read.MainFileProblem + "; listed from the backup.";

                try
                {
                    SaveSerializer.SaveHeader header = json.FromJson<SaveSerializer.SaveHeader>(read.Contents);
                    version = header == null ? 0 : header.SchemaVersion;
                }
                catch (Exception e)
                {
                    problem = (problem == null ? string.Empty : problem + " ") + "Header unreadable: " + e.Message;
                }

                slots.Add(new SaveSlotInfo(slot, true, read.Source, read.LastWriteUtc, version, problem));
            }

            slots.Sort((a, b) =>
            {
                int byTime = b.LastWriteUtc.CompareTo(a.LastWriteUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Slot, b.Slot);
            });

            return slots;
        }
    }
}
