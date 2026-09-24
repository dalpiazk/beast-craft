using System;

namespace BeastCraft.Save
{
    /// <summary>
    /// Saves and loads <see cref="PlayerSettings"/> through the same <see cref="ISaveStorage"/> as the
    /// game saves, in the reserved slot <see cref="SlotName"/>. Non-throwing.
    /// <para>
    /// <strong>The reserved slot.</strong> Settings and game saves share one storage (one directory for
    /// <see cref="FileSaveStorage"/>), so <see cref="SlotName"/> is reserved in any letter case (file
    /// names are case-insensitive on Windows and macOS): <see cref="SaveStore"/> refuses to save or
    /// load a <see cref="PlayerSave"/> there, and <see cref="SaveSlotIndex"/> never lists it as a save.
    /// Code that picks game-save slot names must not use it (<see cref="IsReservedSlot"/>).
    /// </para>
    /// <para>
    /// <strong>Defaults on anything wrong.</strong> A missing slot, unreadable or corrupt text (a
    /// <see cref="FileSaveStorage"/> falls back to the backup first) or JSON the serializer rejects
    /// loads as <c>new PlayerSettings()</c>; a key the file lacks keeps its default. A file written by
    /// a newer build is read for the fields this build knows (unknown keys are ignored).
    /// </para>
    /// </summary>
    public class PlayerSettingsStore
    {
        /// <summary>The storage slot the settings live in; reserved (see the class notes).</summary>
        public const string SlotName = "settings";

        private readonly ISaveStorage _storage;
        private readonly ISaveJsonSerializer _json;

        public PlayerSettingsStore(ISaveStorage storage, ISaveJsonSerializer json)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _json = json ?? throw new ArgumentNullException(nameof(json));
        }

        /// <summary>Whether <paramref name="slot"/> is the settings slot, in any letter case: never a game-save slot.</summary>
        public static bool IsReservedSlot(string slot)
        {
            return string.Equals(slot, SlotName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The stored settings, or the defaults when there are none or they cannot be read (see the class notes). Never null.</summary>
        public PlayerSettings Load()
        {
            return TryLoad(out PlayerSettings settings) ? settings : new PlayerSettings();
        }

        /// <summary>
        /// Reads the stored settings; false (and the defaults in <paramref name="settings"/>) when the
        /// slot is missing, unreadable or does not parse.
        /// </summary>
        public bool TryLoad(out PlayerSettings settings)
        {
            settings = new PlayerSettings();
            if (!_storage.TryRead(SlotName, out string text) || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            try
            {
                PlayerSettings read = _json.FromJson<PlayerSettings>(text);
                if (read == null || read.SchemaVersion < 1)
                {
                    return false;
                }

                settings = read;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes <paramref name="settings"/>, stamping <see cref="PlayerSettings.SchemaVersion"/> to
        /// <see cref="PlayerSettings.CurrentSchemaVersion"/>. False for null settings, a serializer
        /// failure or a storage failure.
        /// </summary>
        public bool Save(PlayerSettings settings)
        {
            if (settings == null)
            {
                return false;
            }

            string json;
            try
            {
                settings.SchemaVersion = PlayerSettings.CurrentSchemaVersion;
                json = _json.ToJson(settings);
            }
            catch (Exception)
            {
                return false;
            }

            return json != null && _storage.TryWrite(SlotName, json);
        }
    }
}
