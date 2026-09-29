using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace BeastCraft.Localization
{
    /// <summary>
    /// Resolves a content data file's text keys to a locale's text as the file is loaded, before it is
    /// read into its data types: the player-facing fields (<see cref="ContentTextRules"/>) hold keys on
    /// disk and the loaded data holds the text, so every screen, validator and tool that reads the data
    /// sees the same English as before the keys existed. The game's loader
    /// (<c>Presentation.Content.GameContent</c>) resolves through the table it loads with the content;
    /// tools and tests that read a data file straight from disk use <see cref="ReadFile"/>.
    /// Switching language means loading the content again with another table.
    /// </summary>
    public static class ContentText
    {
        private static readonly Dictionary<string, StringTable> TablesByRoot = new Dictionary<string, StringTable>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// <paramref name="json"/> (the text of <paramref name="projectRelativePath"/>) with its text keys
        /// resolved through <paramref name="table"/>; unchanged for a file without player-facing text.
        /// </summary>
        public static string Resolve(string json, string projectRelativePath, StringTable table)
        {
            if (table == null || ContentTextRules.For(projectRelativePath).Count == 0)
            {
                return json;
            }

            JsonNode root = JsonNode.Parse(json);
            ContentTextRules.Visit(root, projectRelativePath, (key, value) => table.Get(value));
            return root.ToJsonString();
        }

        /// <summary>
        /// Reads a content data file from disk with its text resolved to English: the file's text, with
        /// the keys looked up in the <c>en.json</c> of the content root it sits in (the folder holding
        /// <c>data/</c>). A file outside a content root, or one without player-facing text, is returned
        /// as it is on disk.
        /// </summary>
        public static string ReadFile(string path)
        {
            string text = File.ReadAllText(path);
            string full = Path.GetFullPath(path).Replace('\\', '/');
            int data = full.LastIndexOf("/data/", StringComparison.Ordinal);
            if (data < 0)
            {
                return text;
            }

            string root = full.Substring(0, data);
            string projectRelativePath = "content" + full.Substring(data);
            if (ContentTextRules.For(projectRelativePath).Count == 0)
            {
                return text;
            }

            return Resolve(text, projectRelativePath, TableFor(root));
        }

        /// <summary>The English table of the content root <paramref name="root"/> (cached; null when it has none).</summary>
        public static StringTable TableFor(string root)
        {
            lock (TablesByRoot)
            {
                if (!TablesByRoot.TryGetValue(root, out StringTable table))
                {
                    string path = Path.Combine(root, "data", "Localization", StringTable.SourceLocale + ".json");
                    table = File.Exists(path) ? StringTable.Parse(File.ReadAllText(path)) : null;
                    TablesByRoot[root] = table;
                }

                return table;
            }
        }
    }
}
