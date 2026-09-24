namespace BeastCraft.Save
{
    /// <summary>
    /// <see cref="ISaveJsonSerializer"/> over <see cref="FieldJson"/> — the serializer the game uses
    /// (System.Text.Json restricted to the public-field rules the save DTOs are written to).
    /// </summary>
    public class JsonSaveSerializer : ISaveJsonSerializer
    {
        private readonly bool _prettyPrint;

        /// <param name="prettyPrint">Indented output: easier to read and diff, slightly larger.</param>
        public JsonSaveSerializer(bool prettyPrint = false)
        {
            _prettyPrint = prettyPrint;
        }

        public string ToJson(object value)
        {
            return FieldJson.ToJson(value, _prettyPrint);
        }

        public T FromJson<T>(string json)
        {
            return FieldJson.FromJson<T>(json);
        }
    }
}
