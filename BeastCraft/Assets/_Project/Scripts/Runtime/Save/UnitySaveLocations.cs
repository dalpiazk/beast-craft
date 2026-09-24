using System.IO;
using UnityEngine;

namespace BeastCraft.Save
{
    /// <summary>
    /// Where the game keeps its saves: the Unity adapter that gives <see cref="FileSaveStorage"/> a
    /// directory under <c>Application.persistentDataPath</c>. Everything else in the save system is
    /// Unity-free; this is the one place it reads a Unity path. (Outside Unity the UnityStub's
    /// <c>Application.persistentDataPath</c> points at a temp directory.)
    /// </summary>
    public static class UnitySaveLocations
    {
        /// <summary>The saves folder's name under <c>Application.persistentDataPath</c>.</summary>
        public const string SavesFolderName = "saves";

        /// <summary><c>Application.persistentDataPath</c>/<see cref="SavesFolderName"/>. Call from the main thread (a Unity rule).</summary>
        public static string DefaultDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SavesFolderName);
        }

        /// <summary>A <see cref="FileSaveStorage"/> over <see cref="DefaultDirectory"/>.</summary>
        public static FileSaveStorage Default()
        {
            return new FileSaveStorage(DefaultDirectory());
        }
    }
}
