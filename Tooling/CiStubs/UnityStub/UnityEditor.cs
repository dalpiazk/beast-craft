// -----------------------------------------------------------------------------------------------
// Minimal compile-only stand-ins for the UnityEditor APIs that the Beast Craft Editor scripts use
// (currently only the beast-roster importer). Same rules as UnityEngine.cs: only add surface a real
// script references. These are not executed by CI -- it only compiles -- so members that would
// need a real AssetDatabase throw rather than pretend to succeed.
//
// Note: living in the same stub assembly as UnityEngine means CiLint would not catch a Runtime
// script wrongly using UnityEditor; Unity's own assembly definitions still would.
// -----------------------------------------------------------------------------------------------

using System;

namespace UnityEditor
{
    /// <summary>Stand-in for <c>[MenuItem("Path/To/Item")]</c>.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItemAttribute : Attribute
    {
        public readonly string menuItem;

        public MenuItemAttribute(string itemName)
        {
            menuItem = itemName;
        }
    }

    /// <summary>Stand-in for <c>UnityEditor.AssetDatabase</c>; compile-only.</summary>
    public static class AssetDatabase
    {
        public static string[] FindAssets(string filter)
        {
            throw NotAvailable();
        }

        public static string GUIDToAssetPath(string guid)
        {
            throw NotAvailable();
        }

        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object
        {
            throw NotAvailable();
        }

        public static void CreateAsset(UnityEngine.Object asset, string path)
        {
            throw NotAvailable();
        }

        public static bool IsValidFolder(string path)
        {
            throw NotAvailable();
        }

        public static string CreateFolder(string parentFolder, string newFolderName)
        {
            throw NotAvailable();
        }

        public static void SaveAssets()
        {
            throw NotAvailable();
        }

        public static void Refresh()
        {
            throw NotAvailable();
        }

        private static NotSupportedException NotAvailable()
        {
            return new NotSupportedException("UnityStub.AssetDatabase is compile-only; run this in the Unity Editor.");
        }
    }

    /// <summary>Stand-in for <c>UnityEditor.EditorUtility</c>; compile-only.</summary>
    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target)
        {
            throw new NotSupportedException("UnityStub.EditorUtility is compile-only; run this in the Unity Editor.");
        }
    }
}
