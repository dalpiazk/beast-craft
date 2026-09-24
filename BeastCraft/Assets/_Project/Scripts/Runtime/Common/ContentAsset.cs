namespace BeastCraft
{
    /// <summary>
    /// Base for the game's content definitions (species, skills, gear, the content libraries, ...),
    /// which were Unity ScriptableObject assets. Plain C# now: built with <c>new</c> from the JSON
    /// data (the <c>*Builder</c>/<c>*Library</c> types) and owned by whoever built them.
    /// <para>
    /// <see cref="name"/> keeps the asset-name role those types relied on (log messages, carrier
    /// skills named after their source). It is a property, not a field, so the JSON rules
    /// (public fields only) never write it, exactly as before.
    /// </para>
    /// </summary>
    public abstract class ContentAsset
    {
        // Lower-cased deliberately: existing call sites read and write `name`.
        public string name { get; set; }

        public override string ToString()
        {
            return name ?? base.ToString();
        }
    }
}
