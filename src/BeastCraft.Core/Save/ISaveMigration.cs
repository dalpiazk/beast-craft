namespace BeastCraft.Save
{
    /// <summary>
    /// One schema upgrade step: turns a save written at <see cref="FromVersion"/> into one at
    /// <c>FromVersion + 1</c>. <see cref="SaveSerializer"/> chains steps until the save reaches the
    /// current version, then reads it as a <see cref="PlayerSave"/>.
    /// <para>
    /// Steps work on the JSON text, not on a <see cref="PlayerSave"/>, because the old shape may not
    /// fit the current type: a step typically reads the text into its own frozen DTO of the old
    /// shape (or into <see cref="PlayerSave"/>, when only values change), rewrites it, and writes it
    /// back with the serializer it is handed. It need not update the version field; the serializer
    /// stamps the final version. A step may throw on input it cannot upgrade; the load then fails
    /// with that message instead of crashing.
    /// </para>
    /// </summary>
    public interface ISaveMigration
    {
        /// <summary>The version this step reads. It writes <c>FromVersion + 1</c>.</summary>
        int FromVersion { get; }

        /// <summary>Upgrades <paramref name="json"/> by one version.</summary>
        string Upgrade(string json, ISaveJsonSerializer serializer);
    }
}
