using System;
using System.IO;

namespace BeastCraft.Save
{
    /// <summary>
    /// Where the game keeps its saves: gives <see cref="FileSaveStorage"/> a directory. Engine-neutral:
    /// by default the per-user local application-data folder
    /// (<see cref="Environment.SpecialFolder.LocalApplicationData"/>, e.g. <c>%LOCALAPPDATA%</c> on
    /// Windows, <c>~/.local/share</c> on Linux) under <see cref="AppFolderName"/>/<see cref="SavesFolderName"/>.
    /// A host with its own storage root (a platform save API, a portable install) passes it to
    /// <see cref="DefaultDirectory(string)"/> or builds <see cref="FileSaveStorage"/> directly.
    /// Nothing is created until the first write.
    /// </summary>
    public static class SaveLocations
    {
        /// <summary>The game's folder under the per-user data root.</summary>
        public const string AppFolderName = "BeastCraft";

        /// <summary>The saves folder's name under the game's folder.</summary>
        public const string SavesFolderName = "saves";

        /// <summary>The per-user data root the default directory lives under.</summary>
        public static string DefaultRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);
        }

        /// <summary><see cref="DefaultRoot"/>/<see cref="SavesFolderName"/>.</summary>
        public static string DefaultDirectory()
        {
            return DefaultDirectory(DefaultRoot());
        }

        /// <summary><paramref name="root"/>/<see cref="SavesFolderName"/>, for a host that supplies its own data root.</summary>
        public static string DefaultDirectory(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new ArgumentException("A save root directory is required.", nameof(root));
            }

            return Path.Combine(root, SavesFolderName);
        }

        /// <summary>A <see cref="FileSaveStorage"/> over <see cref="DefaultDirectory()"/>.</summary>
        public static FileSaveStorage Default()
        {
            return new FileSaveStorage(DefaultDirectory());
        }
    }
}
