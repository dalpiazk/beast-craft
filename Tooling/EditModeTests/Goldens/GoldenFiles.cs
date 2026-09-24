using System;
using System.IO;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Locates and compares the committed golden files beside this source (<c>Tooling/EditModeTests/Goldens</c>).
    /// The goldens were captured on the Unity-era code before the runtime was made engine-neutral; a
    /// mismatch means the port changed observable behaviour. Set <c>BEASTCRAFT_UPDATE_GOLDENS=1</c> to
    /// rewrite them (only when a behaviour change is intended and reviewed).
    /// </summary>
    internal static class GoldenFiles
    {
        public const string RepoRelativeDirectory = "Tooling/EditModeTests/Goldens";

        public static bool Updating
        {
            get { return Environment.GetEnvironmentVariable("BEASTCRAFT_UPDATE_GOLDENS") == "1"; }
        }

        public static string Directory
        {
            get
            {
                string[] starts = { System.IO.Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

                foreach (string start in starts)
                {
                    for (DirectoryInfo dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                    {
                        string candidate = Path.Combine(dir.FullName, RepoRelativeDirectory);

                        if (System.IO.Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }

                Assert.Fail("Could not find " + RepoRelativeDirectory);
                return null;
            }
        }

        public static string Read(string relativePath)
        {
            string path = Path.Combine(Directory, relativePath);
            Assert.IsTrue(File.Exists(path), "Missing golden file " + path + " (set BEASTCRAFT_UPDATE_GOLDENS=1 to capture it).");
            return File.ReadAllText(path);
        }

        public static void Write(string relativePath, string text)
        {
            string path = Path.Combine(Directory, relativePath);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }

        /// <summary>Asserts <paramref name="actual"/> equals the golden file byte for byte, or rewrites it in update mode.</summary>
        public static void AssertMatches(string relativePath, string actual)
        {
            if (Updating)
            {
                Write(relativePath, actual);
                return;
            }

            string expected = Read(relativePath);

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                int at = 0;
                int limit = Math.Min(expected.Length, actual.Length);

                while (at < limit && expected[at] == actual[at])
                {
                    at++;
                }

                Assert.Fail(relativePath + " differs from its golden at character " + at + " (expected length " + expected.Length +
                            ", actual " + actual.Length + "):\n  expected: " + Excerpt(expected, at) + "\n  actual:   " + Excerpt(actual, at));
            }
        }

        private static string Excerpt(string text, int at)
        {
            int start = Math.Max(0, at - 40);
            int length = Math.Min(text.Length - start, 120);
            return length <= 0 ? "<end>" : text.Substring(start, length);
        }
    }
}
