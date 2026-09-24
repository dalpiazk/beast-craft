using System;

namespace BeastCraft.Save
{
    /// <summary>
    /// A <see cref="SaveSerializer"/> bound to an <see cref="ISaveStorage"/>: save and load a
    /// <see cref="PlayerSave"/> by slot name. Non-throwing, like the serializer.
    /// </summary>
    public class SaveStore
    {
        private readonly ISaveStorage _storage;
        private readonly SaveSerializer _serializer;

        public SaveStore(ISaveStorage storage, SaveSerializer serializer)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        }

        /// <summary>Whether <paramref name="slot"/> holds a save.</summary>
        public bool Exists(string slot)
        {
            return _storage.Exists(slot);
        }

        /// <summary>Writes <paramref name="save"/> to <paramref name="slot"/>. False for a null save, a serializer failure or a storage failure.</summary>
        public bool Save(string slot, PlayerSave save)
        {
            string json;

            try
            {
                json = _serializer.Serialize(save);
            }
            catch (Exception)
            {
                return false;
            }

            return json != null && _storage.TryWrite(slot, json);
        }

        /// <summary>Reads <paramref name="slot"/>; see <see cref="SaveSerializer.Deserialize"/>. A missing slot is a failed result.</summary>
        public SaveLoadResult Load(string slot)
        {
            if (!_storage.TryRead(slot, out string json))
            {
                return SaveLoadResult.Failed("No save in slot '" + slot + "'.", 0);
            }

            return _serializer.Deserialize(json);
        }
    }
}
