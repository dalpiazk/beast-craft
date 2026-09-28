using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Tutorial
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Tutorial/hints.json</c>: the tutorial hints. Each is shown
    /// once (its id then goes in <see cref="TutorialProgress.SeenHintIds"/>), when its trigger fires and
    /// all its conditions hold, unless the player turned hints off in the settings. Data only;
    /// <see cref="HintBook"/> picks the one to show, <see cref="HintValidator"/> checks the file.
    /// </summary>
    [Serializable]
    public class HintLibraryData
    {
        public const string ProjectRelativePath = "content/data/Tutorial/hints.json";
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;
        public HintData[] Hints = new HintData[0];
    }

    /// <summary>One tutorial hint.</summary>
    [Serializable]
    public class HintData
    {
        /// <summary>Stable lowercase snake_case id (saved in <see cref="TutorialProgress.SeenHintIds"/>).</summary>
        public string HintId;

        /// <summary>When it may show: one of <see cref="HintTriggers.All"/> (a screen opening, a battle moment).</summary>
        public string Trigger;

        /// <summary>
        /// Every one must hold (<see cref="HintContext"/>): <c>region:{id}</c>, <c>node:{n}</c>,
        /// <c>beasts:{n}</c> (exactly n owned), <c>pick:{step}</c>, <c>tutorial</c> (Hearthglen not yet
        /// behind the player), <c>seen:{hintId}</c>.
        /// </summary>
        public string[] Conditions = new string[0];

        /// <summary>A short heading (DRAFT text).</summary>
        public string Title;

        /// <summary>The hint itself, one or two sentences (DRAFT text; content-bible tone).</summary>
        public string Text;

        /// <summary>What it points at: a widget id on the screen it shows over, or <c>top</c>, <c>center</c>, <c>bottom</c>.</summary>
        public string Anchor = "center";

        /// <summary>Among hints ready at once, the higher shows first (then file order).</summary>
        public int Priority;
    }

    /// <summary>The trigger names a hint may use.</summary>
    public static class HintTriggers
    {
        public const string MapOpen = "map_open";
        public const string EncounterOpen = "encounter_open";
        public const string BattleStart = "battle_start";
        public const string BattleCrit = "battle_crit";
        public const string BattleStatus = "battle_status";
        public const string ResultsOpen = "results_open";
        public const string PickOpen = "pick_open";
        public const string CampOpen = "camp_open";
        public const string StoryDone = "story_done";

        public static readonly string[] All = { MapOpen, EncounterOpen, BattleStart, BattleCrit, BattleStatus, ResultsOpen, PickOpen, CampOpen, StoryDone };
    }

    /// <summary>What a hint's conditions are checked against.</summary>
    public sealed class HintContext
    {
        public string RegionId = string.Empty;

        /// <summary>The map location in play (the encounter's, the battle's), or −1.</summary>
        public int NodeId = -1;

        public int Beasts;

        /// <summary>The beast pick being made (1-3), or 0.</summary>
        public int PickStep;

        /// <summary>Whether Hearthglen is still ahead of or around the player.</summary>
        public bool InTutorial;
    }

    /// <summary>
    /// The built hint library: the next hint to show for a trigger (<see cref="Next"/>), and the
    /// settings switch. Deterministic; changes nothing (the screen marks the hint seen once dismissed).
    /// </summary>
    public sealed class HintBook
    {
        private readonly List<HintData> _hints = new List<HintData>();

        private HintBook()
        {
        }

        public IReadOnlyList<HintData> Hints
        {
            get { return _hints; }
        }

        public static HintBook Build(HintLibraryData data)
        {
            HintBook book = new HintBook();
            foreach (HintData hint in data == null ? new HintData[0] : data.Hints ?? new HintData[0])
            {
                if (hint != null && !string.IsNullOrEmpty(hint.HintId) && !book._hints.Exists(h => h.HintId == hint.HintId))
                {
                    book._hints.Add(hint);
                }
            }

            return book;
        }

        public HintData Get(string hintId)
        {
            return _hints.Find(h => h.HintId == hintId);
        }

        /// <summary>
        /// The hint to show now for <paramref name="trigger"/>: the highest-priority (then first in file)
        /// unseen hint of that trigger whose conditions all hold; null when hints are off
        /// (<paramref name="enabled"/> false) or none is due.
        /// </summary>
        public HintData Next(string trigger, HintContext context, TutorialProgress progress, bool enabled = true)
        {
            if (!enabled || context == null)
            {
                return null;
            }

            HintData best = null;
            foreach (HintData hint in _hints)
            {
                if (hint.Trigger != trigger || (progress != null && progress.HasSeen(hint.HintId)) || !Holds(hint, context, progress))
                {
                    continue;
                }

                if (best == null || hint.Priority > best.Priority)
                {
                    best = hint;
                }
            }

            return best;
        }

        /// <summary>Whether every condition of <paramref name="hint"/> holds.</summary>
        public static bool Holds(HintData hint, HintContext context, TutorialProgress progress)
        {
            foreach (string condition in hint.Conditions ?? new string[0])
            {
                if (!Holds(condition, context, progress))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Holds(string condition, HintContext context, TutorialProgress progress)
        {
            if (condition == "tutorial")
            {
                return context.InTutorial;
            }

            int colon = condition == null ? -1 : condition.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }

            string key = condition.Substring(0, colon);
            string value = condition.Substring(colon + 1);
            switch (key)
            {
                case "region":
                    return value == context.RegionId;
                case "node":
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int node) && node == context.NodeId;
                case "beasts":
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int beasts) && beasts == context.Beasts;
                case "pick":
                    return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int pick) && pick == context.PickStep;
                case "seen":
                    return progress != null && progress.HasSeen(value);
                default:
                    return false;
            }
        }

        /// <summary>Whether <paramref name="condition"/> is one <see cref="Holds(HintData, HintContext, TutorialProgress)"/> understands.</summary>
        public static bool IsKnownCondition(string condition)
        {
            if (condition == "tutorial")
            {
                return true;
            }

            int colon = condition == null ? -1 : condition.IndexOf(':');
            if (colon <= 0 || colon == condition.Length - 1)
            {
                return false;
            }

            string key = condition.Substring(0, colon);
            string value = condition.Substring(colon + 1);
            bool number = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int _);
            return key == "region" || key == "seen" || ((key == "node" || key == "beasts" || key == "pick") && number);
        }
    }

    /// <summary>Checks <see cref="HintLibraryData"/>: ids, triggers, conditions (and seen: references), text and anchors.</summary>
    public static class HintValidator
    {
        public const int MaxTitleLength = 32;
        public const int MaxTextLength = 220;

        public static List<string> Validate(HintLibraryData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Hint library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != HintLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + HintLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (HintData hint in data.Hints ?? new HintData[0])
            {
                if (hint != null && !string.IsNullOrEmpty(hint.HintId))
                {
                    ids.Add(hint.HintId);
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            HintData[] hints = data.Hints ?? new HintData[0];
            for (int i = 0; i < hints.Length; i++)
            {
                HintData hint = hints[i];
                if (hint == null)
                {
                    errors.Add("Hint #" + i + " is null.");
                    continue;
                }

                string where = "Hint '" + hint.HintId + "'";
                if (!BeastRosterValidator.IsSnakeCaseId(hint.HintId))
                {
                    errors.Add(where + ": HintId must be lowercase snake_case.");
                }
                else if (!seen.Add(hint.HintId))
                {
                    errors.Add(where + ": duplicate HintId.");
                }

                if (Array.IndexOf(HintTriggers.All, hint.Trigger) < 0)
                {
                    errors.Add(where + ": Trigger '" + hint.Trigger + "' must be one of " + string.Join(", ", HintTriggers.All) + ".");
                }

                foreach (string condition in hint.Conditions ?? new string[0])
                {
                    if (!HintBook.IsKnownCondition(condition))
                    {
                        errors.Add(where + ": unknown condition '" + condition + "'.");
                    }
                    else if (condition.StartsWith("seen:", StringComparison.Ordinal) && !ids.Contains(condition.Substring(5)))
                    {
                        errors.Add(where + ": condition '" + condition + "' names no hint.");
                    }
                }

                if (string.IsNullOrWhiteSpace(hint.Title) || hint.Title.Length > MaxTitleLength)
                {
                    errors.Add(where + ": Title must be 1-" + MaxTitleLength + " characters.");
                }

                if (string.IsNullOrWhiteSpace(hint.Text) || hint.Text.Length > MaxTextLength)
                {
                    errors.Add(where + ": Text must be 1-" + MaxTextLength + " characters.");
                }

                if (string.IsNullOrWhiteSpace(hint.Anchor))
                {
                    errors.Add(where + ": Anchor is empty (a widget id, or top, center or bottom).");
                }

                if (!ToneCheck.IsClean((hint.Title ?? string.Empty) + " " + (hint.Text ?? string.Empty)))
                {
                    errors.Add(where + ": no one dies in this world (content bible): say defeated, knocked out or gloamed.");
                }
            }

            return errors;
        }
    }

    /// <summary>The content bible's one hard tone rule for player text: no one dies in this world.</summary>
    public static class ToneCheck
    {
        private static readonly Regex Forbidden = new Regex(@"\b(kill|kills|killed|killing|die|dies|died|dying|dead|death)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Whether <paramref name="text"/> avoids death words (say defeated, knocked out, gloamed).</summary>
        public static bool IsClean(string text)
        {
            return string.IsNullOrEmpty(text) || !Forbidden.IsMatch(text);
        }
    }
}
