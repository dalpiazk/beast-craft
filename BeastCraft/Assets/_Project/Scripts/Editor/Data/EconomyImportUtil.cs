using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace BeastCraft.Editor.Data
{
    /// <summary>
    /// The asset plumbing the economy importers share (gear, consumables, shop tables, cosmetics):
    /// find-by-id, find-or-create, the single library asset, and folders. Same conventions as the
    /// skill and roster importers: existing assets are updated in place (keeping their GUIDs), new
    /// ones are created under the importer's folder named by id, duplicates are reported.
    /// </summary>
    internal static class EconomyImportUtil
    {
        /// <summary>Creates <paramref name="folder"/> (and its parents under <c>Assets</c>) if missing.</summary>
        public static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            int slash = folder.LastIndexOf('/');
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        /// <summary>Every asset of type <typeparamref name="T"/> keyed by its id (empty ids ignored; a duplicate keeps the first and logs).</summary>
        public static Dictionary<string, T> FindExisting<T>(System.Func<T, string> getId, string logPrefix) where T : ScriptableObject
        {
            Dictionary<string, T> byId = new Dictionary<string, T>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null || string.IsNullOrEmpty(getId(asset)))
                {
                    continue;
                }

                if (byId.ContainsKey(getId(asset)))
                {
                    Debug.LogError(logPrefix + "Duplicate id '" + getId(asset) + "' on " + typeof(T).Name + " at '" + path + "'; only the first is updated.");
                    continue;
                }

                byId.Add(getId(asset), asset);
            }

            return byId;
        }

        /// <summary>The existing asset with <paramref name="id"/>, or a new one created at <c>folder/PascalId.asset</c>.</summary>
        public static T FindOrCreate<T>(Dictionary<string, T> existing, string id, string folder) where T : ScriptableObject
        {
            if (existing.TryGetValue(id, out T asset))
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, folder + "/" + BeastRosterImporter.ToPascalCase(id) + ".asset");
            existing[id] = asset;
            return asset;
        }

        /// <summary>The project's single <typeparamref name="T"/>, or a new one at <paramref name="path"/>.</summary>
        public static T FindOrCreateSingle<T>(string path, string logPrefix) where T : ScriptableObject
        {
            T found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                string at = AssetDatabase.GUIDToAssetPath(guid);
                T asset = AssetDatabase.LoadAssetAtPath<T>(at);
                if (asset == null)
                {
                    continue;
                }

                if (found != null)
                {
                    Debug.LogWarning(logPrefix + "More than one " + typeof(T).Name + "; only the first is updated. Extra at '" + at + "'.");
                    continue;
                }

                found = asset;
            }

            if (found == null)
            {
                found = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(found, path);
            }

            return found;
        }
    }
}
