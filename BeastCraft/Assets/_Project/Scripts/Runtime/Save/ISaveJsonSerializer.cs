namespace BeastCraft.Save
{
    /// <summary>
    /// The JSON engine <see cref="SaveSerializer"/> writes and reads through. Injectable so the save
    /// logic stays pure C#: in the game it is <see cref="JsonUtilitySaveSerializer"/> (Unity's
    /// <c>JsonUtility</c>); anything else that follows JsonUtility's rules — public fields by exact
    /// name, lists not dictionaries, unknown keys ignored — can stand in for it.
    /// <para>
    /// Implementations may throw on malformed input; <see cref="SaveSerializer"/> catches and reports.
    /// </para>
    /// </summary>
    public interface ISaveJsonSerializer
    {
        /// <summary>Writes <paramref name="value"/>'s public fields as a JSON object.</summary>
        string ToJson(object value);

        /// <summary>Reads a JSON object into a new <typeparamref name="T"/>.</summary>
        T FromJson<T>(string json);
    }
}
