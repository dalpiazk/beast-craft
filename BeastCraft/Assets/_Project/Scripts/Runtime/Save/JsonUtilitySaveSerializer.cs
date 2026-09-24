using UnityEngine;

namespace BeastCraft.Save
{
    /// <summary>
    /// <see cref="ISaveJsonSerializer"/> over Unity's <c>JsonUtility</c> — the serializer the game
    /// uses. (Outside Unity, <c>Tooling/EditModeTests</c> builds the UnityStub with a System.Text.Json
    /// <c>JsonUtility</c> that follows the same field rules, so the save tests run there too.)
    /// </summary>
    public class JsonUtilitySaveSerializer : ISaveJsonSerializer
    {
        private readonly bool _prettyPrint;

        /// <param name="prettyPrint">Indented output: easier to read and diff, slightly larger.</param>
        public JsonUtilitySaveSerializer(bool prettyPrint = false)
        {
            _prettyPrint = prettyPrint;
        }

        public string ToJson(object value)
        {
            return JsonUtility.ToJson(value, _prettyPrint);
        }

        public T FromJson<T>(string json)
        {
            return JsonUtility.FromJson<T>(json);
        }
    }
}
