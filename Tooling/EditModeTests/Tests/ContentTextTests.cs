using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BeastCraft.Campaign;
using BeastCraft.Localization;
using BeastCraft.Presentation.Content;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The string-key layer (#50): every player-facing content field holds its own stable key (no raw
    /// English literal), every key is in <c>en.json</c>, <c>en.json</c> has no orphans, every other
    /// locale's table covers every English key, no player-facing field sits outside the rules, and the
    /// table's missing-key fallback. See <see cref="ContentTextRules"/> for the rule.
    /// </summary>
    public class ContentTextTests
    {
        /// <summary>Field names that hold player-facing text wherever they appear (a data file using one outside <see cref="ContentTextRules"/> fails).</summary>
        private static readonly HashSet<string> TextFieldNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "DisplayName", "Name", "Description", "Text", "Title", "TitleText", "Intro", "BondText", "Definition", "Term", "HerbariumEntry", "Lore", "Body",
            "Subtitle", "Label"
        };

        /// <summary>Files whose text-named fields are not player-facing: the UI style's button colour keys (<c>Buttons[].Text</c>).</summary>
        private static readonly HashSet<string> NoTextFiles = new HashSet<string>(StringComparer.Ordinal) { "content/data/Ui/ui-style.json" };

        /// <summary>
        /// Designer-only fields with a text-like name, never shown (see <see cref="ContentTextRules"/>'s
        /// remarks): an encounter shape variant's <c>Label</c>.
        /// </summary>
        private static readonly HashSet<string> DesignerOnlyPaths = new HashSet<string>(StringComparer.Ordinal) { "content/data/Encounters/encounter-library.json:Shapes[].Variants[].Label" };

        private static string Root
        {
            get { return GameContent.FindRoot(); }
        }

        private static StringTable English
        {
            get { return StringTable.Parse(File.ReadAllText(GameContent.PathOf(Root, StringTable.SourceProjectRelativePath))); }
        }

        private static JsonNode Raw(string projectRelativePath)
        {
            return JsonNode.Parse(File.ReadAllText(GameContent.PathOf(Root, projectRelativePath)));
        }

        [Test]
        public void EveryPlayerFacingField_HoldsItsOwnKey_NotALiteral()
        {
            List<string> literals = new List<string>();
            int fields = 0;
            foreach (string file in ContentTextRules.Files)
            {
                fields += ContentTextRules.Visit(Raw(file), file, (key, value, path) =>
                {
                    if (!string.Equals(key, value, StringComparison.Ordinal))
                    {
                        literals.Add(file + " " + path + ": \"" + value + "\" should be the key \"" + key + "\"");
                    }

                    return value;
                });
            }

            Assert.Greater(fields, 2000, "the content's text fields were all visited");
            Assert.IsEmpty(literals, "Un-keyed text (run `dotnet run --project Tooling/ContentKeys` to key it):\n" + string.Join("\n", literals.Take(30)));
        }

        [Test]
        public void EveryKeyTheContentUses_IsInTheEnglishTable()
        {
            StringTable english = English;
            List<string> missing = new List<string>();
            foreach (string file in ContentTextRules.Files)
            {
                ContentTextRules.Visit(Raw(file), file, (key, value, path) =>
                {
                    if (!english.Contains(value))
                    {
                        missing.Add(file + " " + path + ": " + value);
                    }

                    return value;
                });
            }

            Assert.IsEmpty(missing, string.Join("\n", missing.Take(30)));
        }

        [Test]
        public void TheEnglishTable_HasNoOrphanKeys()
        {
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ContentTextRules.Files)
            {
                ContentTextRules.Visit(Raw(file), file, (key, value, path) =>
                {
                    used.Add(value);
                    return value;
                });
            }

            // The screens' own keys are string literals in the source.
            string src = Path.Combine(Path.GetDirectoryName(Root), "src");
            Regex uiKey = new Regex("\"(ui\\.[a-z0-9_.]+)\"");
            foreach (string source in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in uiKey.Matches(File.ReadAllText(source)))
                {
                    used.Add(match.Groups[1].Value);
                }
            }

            List<string> orphans = English.Keys.Where(key => !used.Contains(key)).OrderBy(key => key, StringComparer.Ordinal).ToList();
            Assert.IsEmpty(orphans, "Keys nothing uses:\n" + string.Join("\n", orphans.Take(30)));
        }

        [Test]
        public void EveryUiKeyTheScreensUse_IsInTheEnglishTable()
        {
            StringTable english = English;
            string src = Path.Combine(Path.GetDirectoryName(Root), "src");
            Regex uiKey = new Regex("\"(ui\\.[a-z0-9_.]+)\"");
            List<string> missing = new List<string>();
            int found = 0;
            foreach (string source in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in uiKey.Matches(File.ReadAllText(source)))
                {
                    found++;
                    if (!english.Contains(match.Groups[1].Value))
                    {
                        missing.Add(Path.GetFileName(source) + ": " + match.Groups[1].Value);
                    }
                }
            }

            Assert.Greater(found, 0, "the screens look their own text up by key");
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [Test]
        public void EveryLocaleTable_CoversEveryEnglishKey()
        {
            StringTable english = English;
            string folder = GameContent.PathOf(Root, StringTable.FolderProjectRelativePath);
            List<string> locales = Directory.GetFiles(folder, "*.json").Select(Path.GetFileNameWithoutExtension).ToList();
            CollectionAssert.Contains(locales, StringTable.SourceLocale, "English is the source table");

            // English is the only locale today (languages are decided after launch); each one added later
            // must carry every English key, and nothing English lacks.
            foreach (string locale in locales.Where(l => l != StringTable.SourceLocale))
            {
                StringTable table = StringTable.Parse(File.ReadAllText(Path.Combine(folder, locale + ".json")));
                Assert.AreEqual(locale, table.Locale, locale + ".json names its own locale");
                List<string> missing = english.Keys.Where(key => !table.Contains(key)).ToList();
                List<string> extra = table.Keys.Where(key => !english.Contains(key)).ToList();
                Assert.IsEmpty(missing, locale + ".json lacks:\n" + string.Join("\n", missing.Take(30)));
                Assert.IsEmpty(extra, locale + ".json has keys English lacks:\n" + string.Join("\n", extra.Take(30)));
            }
        }

        [Test]
        public void NoTextField_SitsOutsideTheRules()
        {
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in ContentTextRules.Files)
            {
                ContentTextRules.Visit(Raw(file), file, (key, value, path) =>
                {
                    covered.Add(file + ":" + Pattern(path));
                    return value;
                });
            }

            List<string> uncovered = new List<string>();
            string data = GameContent.PathOf(Root, "content/data");
            foreach (string path in Directory.GetFiles(data, "*.json", SearchOption.AllDirectories))
            {
                string file = "content/data/" + Path.GetRelativePath(data, path).Replace('\\', '/');
                if (NoTextFiles.Contains(file) || file.StartsWith(StringTable.FolderProjectRelativePath + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                Walk(JsonNode.Parse(File.ReadAllText(path)), node =>
                {
                    string where = file + ":" + Pattern(node.GetPath());
                    if (!covered.Contains(where) && !DesignerOnlyPaths.Contains(where) && !uncovered.Contains(where))
                    {
                        uncovered.Add(where);
                    }
                });
            }

            Assert.IsEmpty(uncovered, "Text fields with no rule in ContentTextRules (add one, or list a designer-only field here):\n" + string.Join("\n", uncovered));
        }

        [Test]
        public void EveryMapLocationName_IsKeyedByItsLabelKey()
        {
            StringTable english = English;
            RegionLibraryData regions = FieldJson.FromJson<RegionLibraryData>(ContentText.ReadFile(GameContent.PathOf(Root, RegionLibraryData.ProjectRelativePath)));
            int checkedKeys = 0;
            foreach (RegionData region in regions.Regions)
            {
                foreach (LocationKind kind in new[] { LocationKind.Wilds, LocationKind.Den, LocationKind.Camp, LocationKind.TradingPost, LocationKind.Pass, LocationKind.Lair })
                {
                    for (int variant = 0; variant < NodeMapGenerator.LabelVariants; variant++)
                    {
                        string labelKey = region.RegionId + "/" + LocationKinds.Key(kind) + "/" + variant;
                        Assert.IsTrue(english.Contains("location." + labelKey.Replace('/', '.')), labelKey);
                        checkedKeys++;
                    }
                }
            }

            Assert.Greater(checkedKeys, 0);
        }

        [Test]
        public void LoadedContent_ReadsText_NotKeys()
        {
            GameContent content = VfxLibraryTests.Content;
            Assert.IsNotNull(content.Text);
            Assert.AreEqual(content.Text.Get("creature.phoenix.name"), content.Roster.Species.First(s => s.SpeciesId == "phoenix").DisplayName);
            Assert.AreEqual("Phoenix", content.Text.Get("creature.phoenix.name"));
            Assert.IsFalse(content.Roster.Species.Any(s => s.DisplayName.StartsWith("creature.", StringComparison.Ordinal)));
        }

        [Test]
        public void AMissingKey_IsLoggedOnce_AndFallsBackVisiblyOrToTheKey()
        {
            bool visible = StringTable.VisibleMissingKeys;
            Action<string> log = StringTable.MissingKeyLog;
            List<string> logged = new List<string>();
            try
            {
                StringTable.MissingKeyLog = logged.Add;
                StringTable table = new StringTable("en", new Dictionary<string, string> { ["a.b"] = "Text" });

                StringTable.VisibleMissingKeys = true;
                Assert.AreEqual("[missing: no.such]", table.Get("no.such"), "a Debug build shows it plainly");
                StringTable.VisibleMissingKeys = false;
                Assert.AreEqual("no.such", table.Get("no.such"), "a Release build shows the key");
                Assert.AreEqual(1, logged.Count, "logged once per key");
                StringAssert.Contains("no.such", logged[0]);

                Assert.AreEqual("Text", table.Get("a.b"));
                Assert.AreEqual(string.Empty, table.Get(null));
                Assert.AreEqual("{\"X\":[{\"Id\":\"k\",\"Name\":\"no.such\"}]}",
                                ContentText.Resolve("{\"X\":[{\"Id\":\"k\",\"Name\":\"no.such\"}]}", "content/data/Unruled/file.json", table), "a file with no rule is untouched");
            }
            finally
            {
                StringTable.VisibleMissingKeys = visible;
                StringTable.MissingKeyLog = log;
            }
        }

        /// <summary>A JSON path with its array indices dropped: <c>$.Species[3].DisplayName</c> is <c>Species[].DisplayName</c>, <c>$.Regions[0].Wilds[2]</c> is <c>Regions[].Wilds</c>.</summary>
        private static string Pattern(string path)
        {
            string pattern = Regex.Replace(path, "\\[\\d+\\]", "[]");
            pattern = pattern.StartsWith("$.", StringComparison.Ordinal) ? pattern.Substring(2) : pattern;
            return pattern.EndsWith("[]", StringComparison.Ordinal) ? pattern.Substring(0, pattern.Length - 2) : pattern;
        }

        /// <summary>Calls <paramref name="found"/> for every non-empty string held by a text-named field (or an array of them).</summary>
        private static void Walk(JsonNode node, Action<JsonNode> found)
        {
            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    if (TextFieldNames.Contains(pair.Key))
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue(out string text) && text.Length > 0)
                        {
                            found(value);
                        }
                        else if (pair.Value is JsonArray array)
                        {
                            foreach (JsonNode entry in array)
                            {
                                if (entry is JsonValue item && item.TryGetValue(out string s) && s.Length > 0)
                                {
                                    found(item);
                                }
                            }
                        }
                    }

                    Walk(pair.Value, found);
                }
            }
            else if (node is JsonArray list)
            {
                foreach (JsonNode entry in list)
                {
                    Walk(entry, found);
                }
            }
        }
    }
}
