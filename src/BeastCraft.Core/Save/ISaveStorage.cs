namespace BeastCraft.Save
{
    /// <summary>
    /// Where save text lives, by slot name — the thin IO seam that keeps the save system pure C#.
    /// The game implements it over local files (e.g. under <c>Application.persistentDataPath</c>,
    /// writing to a temp file and swapping it in) and later Cloud Save; tests use an in-memory map.
    /// Implementations should report failure through the return values rather than throw.
    /// </summary>
    public interface ISaveStorage
    {
        /// <summary>Whether <paramref name="slot"/> holds a save.</summary>
        bool Exists(string slot);

        /// <summary>The text in <paramref name="slot"/>; false (and null) when there is none or it cannot be read.</summary>
        bool TryRead(string slot, out string contents);

        /// <summary>Replaces <paramref name="slot"/>'s text. False when it could not be written.</summary>
        bool TryWrite(string slot, string contents);

        /// <summary>Removes <paramref name="slot"/>. False when there was nothing to remove or it could not be.</summary>
        bool Delete(string slot);
    }
}
