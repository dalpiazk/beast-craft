using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace BeastCraft.Localization
{
    /// <summary>
    /// Where the player-facing text lives in one content data file: the objects of one array (a
    /// <see cref="Path"/> such as <c>Species</c> or <c>Enemies[].Skills</c>), the key each object's text
    /// gets (<see cref="KeyTemplate"/>), and which of its fields are player-facing
    /// (<see cref="Fields"/>).
    /// </summary>
    public sealed class ContentTextRule
    {
        public ContentTextRule(string file, string path, string keyTemplate, params string[] fields)
        {
            File = file;
            Path = path;
            KeyTemplate = keyTemplate;
            Fields = fields;
        }

        /// <summary>The data file's ProjectRelativePath (<c>content/data/...</c>).</summary>
        public string File { get; }

        /// <summary>The array of objects, from the file's root: property names joined by <c>[].</c> (<c>Enemies[].Skills</c>).</summary>
        public string Path { get; }

        /// <summary>
        /// The key of one object's text, before the field's short name: literal text with
        /// <c>{Property}</c> placeholders read from the object or, failing that, its enclosing objects
        /// (<c>enemy.{EnemyId}.skill.{SkillId}</c>), and <c>{#}</c> for the object's index in its array
        /// (for objects without an id).
        /// </summary>
        public string KeyTemplate { get; }

        /// <summary>The player-facing fields (a string, or an array of strings keyed by index).</summary>
        public IReadOnlyList<string> Fields { get; }
    }

    /// <summary>
    /// Which content fields are player-facing text, and the stable key each one gets. The one rule the
    /// string-key layer runs on: the <c>Tooling/ContentKeys</c> tool uses it to turn English literals
    /// into keys (and write <c>en.json</c>), <see cref="ContentText"/> uses it to resolve the keys when a
    /// file is loaded, and the content tests use it to fail on a raw literal or a missing key.
    /// <para>
    /// <strong>The rule.</strong> A field is player-facing when the player reads its text: names
    /// (<c>DisplayName</c>, <c>Name</c>, <c>TitleText</c>, <c>Term</c>, map location names), descriptions
    /// and definitions, dialogue, lore, story and hint text and titles, bond and intro lines, herbarium
    /// entries, codex categories, and the glossary's matching word forms (language data: a translation
    /// needs its own). Everything else stays a literal: ids and references (<c>...Id</c>, <c>Look</c>,
    /// <c>Conditions</c>), asset and art keys (<c>ArtKey</c>, <c>Sheet</c>, <c>Icon</c>), enum names
    /// (<c>Kind</c>, <c>Stance</c>, <c>Element</c>, <c>Type</c>), colours and style keys, designer-only labels
    /// no screen shows (an encounter shape variant's <c>Label</c>, an enemy's <c>Role</c>) and every
    /// <c>_readme</c>. <c>ui-style.json</c>, the VFX and battle-art files and the number tables hold no
    /// player-facing text.
    /// </para>
    /// <para>
    /// <strong>Keys.</strong> <c>{template}.{field}</c>, the template built from ids already in the data
    /// (<c>creature.phoenix.name</c>, <c>skill.ember_shot.desc</c>, <c>enemy.giant.skill.crush.name</c>),
    /// the field by its short name (<see cref="FieldKey"/>); a string array's entries add their index
    /// (<c>location.r01.wilds.3</c>, which is <c>location.</c> plus the map node's <c>LabelKey</c>
    /// <c>r01/wilds/3</c> with dots). Keys never change once shipped: renaming an id is already forbidden.
    /// An empty field stays empty (nothing to translate).
    /// </para>
    /// </summary>
    public static class ContentTextRules
    {
        /// <summary>Every rule, grouped by file.</summary>
        public static readonly IReadOnlyList<ContentTextRule> All = new[]
        {
            new ContentTextRule("content/data/Campaign/discovery.json", "Caches", "cache.{CacheId}", "Name"),
            new ContentTextRule("content/data/Campaign/discovery.json", "KinshipSites", "kinship.{SiteId}", "Name", "BondText", "Intro"),
            new ContentTextRule("content/data/Campaign/discovery.json", "Lore", "lore.{LoreId}", "Title", "Text"),
            new ContentTextRule("content/data/Campaign/discovery.json", "Shrines", "shrine.{ShrineId}", "Name", "Text"),

            new ContentTextRule("content/data/Campaign/location-names.json", "Regions", "location.{RegionId}", "Wilds", "Den", "Camp", "TradingPost", "Pass", "Lair"),

            new ContentTextRule("content/data/Campaign/regions.json", "Regions", "region.{RegionId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Campaign/regions.json", "TutorialRegions", "region.{RegionId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Campaign/regions.json", "TutorialRegions[].FixedNodes", "region.{RegionId}.node.{#}", "Name"),
            new ContentTextRule("content/data/Campaign/regions.json", "Seals", "seal.{SealId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Cosmetics/cosmetic-library.json", "Categories", "cosmetic.{CategoryId}", "DisplayName"),
            new ContentTextRule("content/data/Cosmetics/cosmetic-library.json", "Categories[].Options", "cosmetic.{CategoryId}.{OptionId}", "DisplayName"),
            new ContentTextRule("content/data/Cosmetics/cosmetic-library.json", "Milestones", "milestone.{MilestoneId}", "DisplayName"),

            new ContentTextRule("content/data/Creatures/beast-roster.json", "Species", "creature.{SpeciesId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Encounters/encounter-library.json", "Shapes", "encounter_shape.{ShapeId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Encounters/encounter-library.json", "Templates", "encounter.{EncounterId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Encounters/enemy-library.json", "Enemies", "enemy.{EnemyId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Encounters/enemy-library.json", "Enemies[].Skills", "enemy.{EnemyId}.skill.{SkillId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Glossary/glossary.json", "Terms", "glossary.{TermId}", "Term", "Forms", "Definition"),

            new ContentTextRule("content/data/Grove/expedition-library.json", "Destinations", "expedition.{DestinationId}", "DisplayName"),
            new ContentTextRule("content/data/Grove/expedition-library.json", "Stories", "expedition_story.{StoryId}", "Text", "CodexCategory"),

            new ContentTextRule("content/data/Grove/garden-library.json", "Seeds", "seed.{SeedId}", "DisplayName"),
            new ContentTextRule("content/data/Grove/garden-library.json", "Varieties", "variety.{VarietyId}", "DisplayName", "HerbariumEntry"),

            new ContentTextRule("content/data/Grove/grove-library.json", "ColourForms", "colour_form.{ColourFormId}", "DisplayName"),
            new ContentTextRule("content/data/Grove/grove-library.json", "Decor", "decor.{DecorId}", "DisplayName"),
            new ContentTextRule("content/data/Grove/grove-library.json", "Habitats", "habitat.{HabitatId}", "DisplayName"),
            new ContentTextRule("content/data/Grove/grove-library.json", "Lore", "grove_lore.{LoreId}", "Title", "Text"),

            new ContentTextRule("content/data/Items/consumable-library.json", "Consumables", "consumable.{ConsumableId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Items/gear-library.json", "BeastGear", "gear.{GearId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Items/gear-library.json", "AvatarGear", "avatar_gear.{GearId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Npc/dialogue.json", "Npcs", "npc.{NpcId}", "DisplayName"),
            new ContentTextRule("content/data/Npc/dialogue.json", "Scenes", "scene.{SceneId}", "Title"),
            new ContentTextRule("content/data/Npc/dialogue.json", "Lines", "line.{LineId}", "Text"),
            new ContentTextRule("content/data/Npc/dialogue.json", "Requests", "request.{RequestId}", "Text"),
            new ContentTextRule("content/data/Npc/dialogue.json", "Lore", "npc_lore.{LoreId}", "Title", "Text"),
            new ContentTextRule("content/data/Npc/dialogue.json", "SideStories", "story.{StoryId}", "DisplayName"),
            new ContentTextRule("content/data/Npc/dialogue.json", "SideStories[].Chapters", "story.{StoryId}.chapter.{ChapterId}", "Text"),

            new ContentTextRule("content/data/Progression/achievements.json", "Achievements", "achievement.{AchievementId}", "DisplayName", "TitleText"),

            new ContentTextRule("content/data/Skills/skill-library.json", "BeastSkills", "skill.{SkillId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Skills/skill-library.json", "AvatarActives", "skill.{SkillId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Skills/skill-library.json", "AvatarPassives", "passive.{PassiveId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Skills/skill-library.json", "Materials", "material.{MaterialId}", "DisplayName", "Description"),
            new ContentTextRule("content/data/Skills/skill-library.json", "TeamBonds", "bond.{BondId}", "DisplayName", "Description"),

            new ContentTextRule("content/data/Tutorial/hints.json", "Hints", "hint.{HintId}", "Title", "Text")
        };

        /// <summary>The files that hold keyed text.</summary>
        public static IEnumerable<string> Files
        {
            get
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (ContentTextRule rule in All)
                {
                    if (seen.Add(rule.File))
                    {
                        yield return rule.File;
                    }
                }
            }
        }

        /// <summary>The rules for <paramref name="projectRelativePath"/> (none for a file without player-facing text).</summary>
        public static List<ContentTextRule> For(string projectRelativePath)
        {
            return new List<ContentTextRule>(Array.FindAll(ToArray(), rule => string.Equals(rule.File, projectRelativePath, StringComparison.Ordinal)));
        }

        /// <summary>
        /// A field's short name in a key: <c>DisplayName</c> and <c>Name</c> are <c>name</c>,
        /// <c>Description</c> <c>desc</c>, <c>TitleText</c> <c>title</c>, <c>BondText</c> <c>bond</c>,
        /// <c>HerbariumEntry</c> <c>herbarium</c>; anything else in snake case (<c>TradingPost</c> is
        /// <c>trading_post</c>, <c>Text</c> <c>text</c>).
        /// </summary>
        public static string FieldKey(string field)
        {
            switch (field)
            {
                case "DisplayName":
                case "Name":
                    return "name";
                case "Description":
                    return "desc";
                case "TitleText":
                    return "title";
                case "BondText":
                    return "bond";
                case "HerbariumEntry":
                    return "herbarium";
                default:
                    return Snake(field);
            }
        }

        /// <summary>
        /// Calls <paramref name="visit"/> for every player-facing string in <paramref name="root"/> (a
        /// parsed <paramref name="projectRelativePath"/>), with its key and current value, and stores what
        /// it returns in place of the value. Empty strings are skipped. Returns how many were visited.
        /// A template placeholder that no object supplies throws a <see cref="FormatException"/>: the
        /// rule and the data disagree.
        /// </summary>
        public static int Visit(JsonNode root, string projectRelativePath, Func<string, string, string> visit)
        {
            return Visit(root, projectRelativePath, (key, value, path) => visit(key, value));
        }

        /// <summary><see cref="Visit(JsonNode, string, Func{string, string, string})"/>, also passing each value's JSON path (<c>$.Species[0].DisplayName</c>).</summary>
        public static int Visit(JsonNode root, string projectRelativePath, Func<string, string, string, string> visit)
        {
            int count = 0;
            foreach (ContentTextRule rule in For(projectRelativePath))
            {
                string[] segments = rule.Path.Split(new[] { "[]." }, StringSplitOptions.None);
                VisitLevel(root as JsonObject, segments, 0, new List<JsonObject>(), rule, visit, ref count);
            }

            return count;
        }

        private static void VisitLevel(JsonObject parent, string[] segments, int depth, List<JsonObject> chain, ContentTextRule rule, Func<string, string, string, string> visit, ref int count)
        {
            if (parent == null || !(parent[segments[depth]] is JsonArray array))
            {
                return;
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JsonObject item))
                {
                    continue;
                }

                chain.Add(item);
                if (depth + 1 < segments.Length)
                {
                    VisitLevel(item, segments, depth + 1, chain, rule, visit, ref count);
                }
                else
                {
                    string prefix = Expand(rule, chain, i);
                    foreach (string field in rule.Fields)
                    {
                        string key = prefix + "." + FieldKey(field);
                        JsonNode value = item[field];
                        if (value is JsonArray strings)
                        {
                            for (int j = 0; j < strings.Count; j++)
                            {
                                if (strings[j] is JsonValue entry && entry.TryGetValue(out string text) && text.Length > 0)
                                {
                                    strings[j] = JsonValue.Create(visit(key + "." + j.ToString(CultureInfo.InvariantCulture), text, entry.GetPath()));
                                    count++;
                                }
                            }
                        }
                        else if (value is JsonValue single && single.TryGetValue(out string text) && text.Length > 0)
                        {
                            item[field] = JsonValue.Create(visit(key, text, single.GetPath()));
                            count++;
                        }
                    }
                }

                chain.RemoveAt(chain.Count - 1);
            }
        }

        private static string Expand(ContentTextRule rule, List<JsonObject> chain, int index)
        {
            StringBuilder key = new StringBuilder();
            string template = rule.KeyTemplate;
            int at = 0;
            while (at < template.Length)
            {
                int open = template.IndexOf('{', at);
                if (open < 0)
                {
                    key.Append(template, at, template.Length - at);
                    break;
                }

                int close = template.IndexOf('}', open);
                key.Append(template, at, open - at);
                string name = template.Substring(open + 1, close - open - 1);
                key.Append(name == "#" ? index.ToString(CultureInfo.InvariantCulture) : Lookup(rule, chain, name));
                at = close + 1;
            }

            return key.ToString();
        }

        private static string Lookup(ContentTextRule rule, List<JsonObject> chain, string property)
        {
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i][property] is JsonValue value)
                {
                    string text = value.TryGetValue(out string s) ? s : value.ToJsonString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text;
                    }
                }
            }

            throw new FormatException(rule.File + " " + rule.Path + ": an entry has no " + property + " for its text key.");
        }

        private static ContentTextRule[] ToArray()
        {
            ContentTextRule[] rules = new ContentTextRule[All.Count];
            for (int i = 0; i < rules.Length; i++)
            {
                rules[i] = All[i];
            }

            return rules;
        }

        private static string Snake(string name)
        {
            StringBuilder snake = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0)
                {
                    snake.Append('_');
                }

                snake.Append(char.ToLowerInvariant(c));
            }

            return snake.ToString();
        }
    }
}
