using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BeastCraft.Localization;

namespace BeastCraft.Tooling.ContentKeys
{
    /// <summary>
    /// The string-key tool (#50). For every content data file with player-facing text
    /// (<see cref="ContentTextRules"/>, which also states the rule for what counts as player-facing):
    /// <list type="bullet">
    /// <item>a field holding an English literal gets its stable key instead, and the literal goes to
    /// <c>content/data/Localization/en.json</c> under that key;</item>
    /// <item>a field already holding its own key is left alone (so the tool is re-runnable: run it after
    /// adding content with plain English text and it keys just the new text);</item>
    /// <item>a field holding a key <c>en.json</c> lacks, or a key that two fields would share, is an error.</item>
    /// </list>
    /// The data files are edited in place, token by token, so their formatting is untouched; <c>en.json</c>
    /// is rewritten with its keys sorted. <c>--check</c> reports without writing and exits 1 when a
    /// literal or a missing key is found. Keys <c>en.json</c> holds that no content field uses are
    /// listed as orphans (<c>ui.*</c> keys belong to the screens and are not counted).
    /// </summary>
    public static class Program
    {
        private const string Readme =
            "The English text of every player-facing content field, by stable key: the single source of English. " +
            "The content data files hold these keys (the fields and the key scheme are in src/BeastCraft.Core/Localization/ContentTextRules.cs); " +
            "the game resolves them as it loads the content. Edit text here. To add content, write the English into the data file as usual and run " +
            "`dotnet run --project Tooling/ContentKeys`: it moves the new text here and puts its key in the data file. Keys never change once shipped. " +
            "ui.* keys are the screens' own text. Other locales go beside this file as <locale>.json with the same keys (languages are decided after launch).";

        public static int Main(string[] args)
        {
            bool check = Array.IndexOf(args, "--check") >= 0;
            int contentAt = Array.IndexOf(args, "--content");
            string root = contentAt >= 0 && contentAt + 1 < args.Length ? Path.GetFullPath(args[contentAt + 1]) : FindContentRoot();
            if (root == null)
            {
                Console.Error.WriteLine("No content/ folder found (run from the repo, or pass --content DIR).");
                return 2;
            }

            string tablePath = Path.Combine(root, "data", "Localization", StringTable.SourceLocale + ".json");
            SortedDictionary<string, string> strings = new SortedDictionary<string, string>(StringComparer.Ordinal);
            JsonObject existing = null;
            if (File.Exists(tablePath))
            {
                string json = File.ReadAllText(tablePath);
                existing = JsonNode.Parse(json) as JsonObject;
                StringTable table = StringTable.Parse(json);
                foreach (string key in table.Keys)
                {
                    table.TryGet(key, out string text);
                    strings[key] = text;
                }
            }

            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            List<string> errors = new List<string>();
            int literals = 0;
            int keyed = 0;
            int changedFiles = 0;

            foreach (string file in ContentTextRules.Files)
            {
                string path = Path.Combine(root, file.Substring("content/".Length).Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = File.ReadAllBytes(path);
                JsonNode node = JsonNode.Parse(bytes);
                Dictionary<string, string> replacements = new Dictionary<string, string>(StringComparer.Ordinal);
                ContentTextRules.Visit(node, file, (key, value, jsonPath) =>
                {
                    if (!used.Add(key))
                    {
                        errors.Add(file + ": the key '" + key + "' is used twice (" + jsonPath + ").");
                        return value;
                    }

                    if (string.Equals(value, key, StringComparison.Ordinal))
                    {
                        keyed++;
                        if (!strings.ContainsKey(key))
                        {
                            errors.Add(file + ": " + jsonPath + " holds the key '" + key + "', which en.json lacks.");
                        }

                        return value;
                    }

                    literals++;
                    strings[key] = value;
                    replacements[jsonPath] = key;
                    return key;
                });

                if (replacements.Count == 0)
                {
                    continue;
                }

                Console.WriteLine(file + ": " + replacements.Count + " literal(s) " + (check ? "to key" : "keyed"));
                if (!check)
                {
                    File.WriteAllBytes(path, Splice(bytes, replacements, file, errors));
                    changedFiles++;
                }
            }

            List<string> orphans = new List<string>();
            foreach (string key in strings.Keys)
            {
                if (!used.Contains(key) && !key.StartsWith("ui.", StringComparison.Ordinal))
                {
                    orphans.Add(key);
                }
            }

            foreach (string error in errors)
            {
                Console.Error.WriteLine("ERROR " + error);
            }

            foreach (string orphan in orphans)
            {
                Console.WriteLine("orphan (no content field uses it): " + orphan);
            }

            Console.WriteLine(used.Count + " text field(s): " + keyed + " already keyed, " + literals + " literal(s)" + (check ? " found" : " keyed") + "; " +
                              strings.Count + " string(s) in en.json; " + orphans.Count + " orphan(s).");

            if (check)
            {
                return errors.Count > 0 || literals > 0 ? 1 : 0;
            }

            if (errors.Count > 0)
            {
                Console.Error.WriteLine("Nothing written to en.json: fix the errors first." + (changedFiles > 0 ? " (" + changedFiles + " data file(s) were already rewritten.)" : string.Empty));
                return 1;
            }

            WriteTable(tablePath, strings, existing);
            return 0;
        }

        /// <summary>The repo's <c>content/</c>, walking up from the working directory.</summary>
        private static string FindContentRoot()
        {
            for (DirectoryInfo dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "content");
                if (File.Exists(Path.Combine(candidate, "data", "Creatures", "beast-roster.json")))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces the string tokens at <paramref name="replacements"/>' JSON paths in the original bytes,
        /// leaving every other byte (whitespace, line breaks, number formatting, key order) as it was.
        /// </summary>
        private static byte[] Splice(byte[] bytes, Dictionary<string, string> replacements, string file, List<string> errors)
        {
            List<(int Start, int End, string Key)> splices = new List<(int, int, string)>();
            List<(bool Array, int Index, string Property)> stack = new List<(bool, int, string)>();
            Utf8JsonReader reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        stack[stack.Count - 1] = (false, -1, reader.GetString());
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        Advance(stack);
                        stack.Add((reader.TokenType == JsonTokenType.StartArray, -1, null));
                        break;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        stack.RemoveAt(stack.Count - 1);
                        break;
                    default:
                        Advance(stack);
                        if (reader.TokenType == JsonTokenType.String && replacements.TryGetValue(PathOf(stack), out string key))
                        {
                            splices.Add(((int)reader.TokenStartIndex, (int)reader.BytesConsumed, key));
                        }

                        break;
                }
            }

            if (splices.Count != replacements.Count)
            {
                errors.Add(file + ": found " + splices.Count + " of " + replacements.Count + " fields to key while rewriting.");
            }

            List<byte> output = new List<byte>(bytes.Length);
            int at = 0;
            foreach ((int start, int end, string key) in splices)
            {
                for (int i = at; i < start; i++)
                {
                    output.Add(bytes[i]);
                }

                output.AddRange(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(key)));
                at = end;
            }

            for (int i = at; i < bytes.Length; i++)
            {
                output.Add(bytes[i]);
            }

            return output.ToArray();
        }

        /// <summary>A value is about to be read: an array frame moves to its next index.</summary>
        private static void Advance(List<(bool Array, int Index, string Property)> stack)
        {
            if (stack.Count > 0 && stack[stack.Count - 1].Array)
            {
                (bool array, int index, string property) = stack[stack.Count - 1];
                stack[stack.Count - 1] = (array, index + 1, property);
            }
        }

        /// <summary>The path of the value being read, as <c>JsonNode.GetPath</c> writes it (<c>$.Species[0].DisplayName</c>).</summary>
        private static string PathOf(List<(bool Array, int Index, string Property)> stack)
        {
            StringBuilder path = new StringBuilder("$");
            foreach ((bool array, int index, string property) in stack)
            {
                if (array)
                {
                    path.Append('[').Append(index).Append(']');
                }
                else
                {
                    path.Append('.').Append(property);
                }
            }

            return path.ToString();
        }

        private static void WriteTable(string path, SortedDictionary<string, string> strings, JsonObject existing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string readme = existing?["_readme"] is JsonValue value && value.TryGetValue(out string text) ? text : Readme;
            using (MemoryStream stream = new MemoryStream())
            {
                JsonWriterOptions options = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" };
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, options))
                {
                    writer.WriteStartObject();
                    writer.WriteString("_readme", readme);
                    writer.WriteString("Locale", StringTable.SourceLocale);
                    writer.WriteStartObject("Strings");
                    foreach (KeyValuePair<string, string> pair in strings)
                    {
                        writer.WriteString(pair.Key, pair.Value);
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                stream.WriteByte((byte)'\n');
                File.WriteAllBytes(path, stream.ToArray());
            }

            Console.WriteLine("Wrote " + path + " (" + strings.Count + " strings).");
        }
    }
}
