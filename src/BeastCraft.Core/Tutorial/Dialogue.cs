using System;
using System.Collections.Generic;
using BeastCraft.Creatures.Roster;

namespace BeastCraft.Tutorial
{
    /// <summary>
    /// The plain-data shape of <c>content/data/Npc/dialogue.json</c>: the NPC dialogue layer (the Grove
    /// design's model, first used by Hearthglen's mentor, the Grove Keeper). Lines are
    /// <see cref="DialogueLineData"/> { NpcId, Conditions, Text, Priority } exactly as the Grove design
    /// proposes, so the Grove's condition-resolved lines (<see cref="DialogueBook.Resolve"/>) and a
    /// story's ordered scene (<see cref="DialogueSceneData"/>) share one file and one validator.
    /// </summary>
    [Serializable]
    public class DialogueLibraryData
    {
        public const string ProjectRelativePath = "content/data/Npc/dialogue.json";
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion;
        public NpcData[] Npcs = new NpcData[0];
        public DialogueLineData[] Lines = new DialogueLineData[0];
        public DialogueSceneData[] Scenes = new DialogueSceneData[0];
    }

    /// <summary>A speaking character.</summary>
    [Serializable]
    public class NpcData
    {
        public string NpcId;
        public string DisplayName;

        /// <summary>The portrait art key (art manifest); "" = a placeholder portrait (their initial on a disc).</summary>
        public string PortraitKey = string.Empty;
    }

    /// <summary>One line an NPC can say.</summary>
    [Serializable]
    public class DialogueLineData
    {
        /// <summary>Stable lowercase snake_case id (scenes list lines by it; a future LinesSeen save list keys on it).</summary>
        public string LineId;

        public string NpcId;

        /// <summary>
        /// Facts that must all hold for <see cref="DialogueBook.Resolve"/> (the Grove's resolution; e.g.
        /// <c>region:r01</c>). Scene lines usually carry none: the scene plays them in order.
        /// </summary>
        public string[] Conditions = new string[0];

        /// <summary>The line (DRAFT text; content-bible tone).</summary>
        public string Text;

        /// <summary>Among lines that hold, the higher wins (<see cref="DialogueBook.Resolve"/>).</summary>
        public int Priority;
    }

    /// <summary>An ordered run of lines a story location plays (<c>FixedNodeData.SceneId</c>).</summary>
    [Serializable]
    public class DialogueSceneData
    {
        public string SceneId;

        /// <summary>A heading for the dialogue box (DRAFT text).</summary>
        public string Title;

        public string[] LineIds = new string[0];
    }

    /// <summary>The built dialogue library: scenes in order, and condition resolution for the Grove's NPC lines.</summary>
    public sealed class DialogueBook
    {
        private readonly Dictionary<string, DialogueLineData> _lines = new Dictionary<string, DialogueLineData>(StringComparer.Ordinal);
        private readonly Dictionary<string, DialogueSceneData> _scenes = new Dictionary<string, DialogueSceneData>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcData> _npcs = new Dictionary<string, NpcData>(StringComparer.Ordinal);
        private readonly List<DialogueLineData> _order = new List<DialogueLineData>();

        private DialogueBook()
        {
        }

        public static DialogueBook Build(DialogueLibraryData data)
        {
            DialogueBook book = new DialogueBook();
            foreach (NpcData npc in data == null ? new NpcData[0] : data.Npcs ?? new NpcData[0])
            {
                if (npc != null && !string.IsNullOrEmpty(npc.NpcId) && !book._npcs.ContainsKey(npc.NpcId))
                {
                    book._npcs.Add(npc.NpcId, npc);
                }
            }

            foreach (DialogueLineData line in data == null ? new DialogueLineData[0] : data.Lines ?? new DialogueLineData[0])
            {
                if (line != null && !string.IsNullOrEmpty(line.LineId) && !book._lines.ContainsKey(line.LineId))
                {
                    book._lines.Add(line.LineId, line);
                    book._order.Add(line);
                }
            }

            foreach (DialogueSceneData scene in data == null ? new DialogueSceneData[0] : data.Scenes ?? new DialogueSceneData[0])
            {
                if (scene != null && !string.IsNullOrEmpty(scene.SceneId) && !book._scenes.ContainsKey(scene.SceneId))
                {
                    book._scenes.Add(scene.SceneId, scene);
                }
            }

            return book;
        }

        public NpcData Npc(string npcId)
        {
            return !string.IsNullOrEmpty(npcId) && _npcs.TryGetValue(npcId, out NpcData npc) ? npc : null;
        }

        public DialogueSceneData Scene(string sceneId)
        {
            return !string.IsNullOrEmpty(sceneId) && _scenes.TryGetValue(sceneId, out DialogueSceneData scene) ? scene : null;
        }

        /// <summary>A scene's lines, in order (unknown ids skipped); empty for an unknown scene.</summary>
        public List<DialogueLineData> SceneLines(string sceneId)
        {
            List<DialogueLineData> lines = new List<DialogueLineData>();
            foreach (string id in Scene(sceneId)?.LineIds ?? new string[0])
            {
                if (id != null && _lines.TryGetValue(id, out DialogueLineData line))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        /// <summary>
        /// The Grove design's resolution: <paramref name="npcId"/>'s highest-priority line whose
        /// conditions are all in <paramref name="facts"/> (ties: the one with more conditions, then
        /// file order); null when none holds.
        /// </summary>
        public DialogueLineData Resolve(string npcId, ICollection<string> facts)
        {
            DialogueLineData best = null;
            foreach (DialogueLineData line in _order)
            {
                if (line.NpcId != npcId)
                {
                    continue;
                }

                bool holds = true;
                foreach (string condition in line.Conditions ?? new string[0])
                {
                    holds &= facts != null && facts.Contains(condition);
                }

                int conditions = (line.Conditions ?? new string[0]).Length;
                if (holds && (best == null || line.Priority > best.Priority || (line.Priority == best.Priority && conditions > (best.Conditions ?? new string[0]).Length)))
                {
                    best = line;
                }
            }

            return best;
        }
    }

    /// <summary>Checks <see cref="DialogueLibraryData"/>: ids, references, text length and tone.</summary>
    public static class DialogueValidator
    {
        public const int MaxLineLength = 240;

        public static List<string> Validate(DialogueLibraryData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("Dialogue library is null (the JSON did not parse).");
                return errors;
            }

            if (data.SchemaVersion != DialogueLibraryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this code reads version " + DialogueLibraryData.CurrentSchemaVersion + ".");
            }

            HashSet<string> npcs = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcData npc in data.Npcs ?? new NpcData[0])
            {
                if (npc == null || !BeastRosterValidator.IsSnakeCaseId(npc.NpcId) || !npcs.Add(npc.NpcId) || string.IsNullOrWhiteSpace(npc.DisplayName))
                {
                    errors.Add("Npc '" + npc?.NpcId + "': needs a unique snake_case NpcId and a DisplayName.");
                }
            }

            HashSet<string> lines = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueLineData line in data.Lines ?? new DialogueLineData[0])
            {
                string where = "Line '" + line?.LineId + "'";
                if (line == null || !BeastRosterValidator.IsSnakeCaseId(line.LineId) || !lines.Add(line.LineId))
                {
                    errors.Add(where + ": needs a unique snake_case LineId.");
                    continue;
                }

                if (!npcs.Contains(line.NpcId))
                {
                    errors.Add(where + ": unknown NpcId '" + line.NpcId + "'.");
                }

                if (string.IsNullOrWhiteSpace(line.Text) || line.Text.Length > MaxLineLength)
                {
                    errors.Add(where + ": Text must be 1-" + MaxLineLength + " characters.");
                }
                else if (!ToneCheck.IsClean(line.Text))
                {
                    errors.Add(where + ": no one dies in this world (content bible).");
                }
            }

            HashSet<string> scenes = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueSceneData scene in data.Scenes ?? new DialogueSceneData[0])
            {
                string where = "Scene '" + scene?.SceneId + "'";
                if (scene == null || !BeastRosterValidator.IsSnakeCaseId(scene.SceneId) || !scenes.Add(scene.SceneId))
                {
                    errors.Add(where + ": needs a unique snake_case SceneId.");
                    continue;
                }

                if (scene.LineIds == null || scene.LineIds.Length == 0)
                {
                    errors.Add(where + ": has no lines.");
                }

                foreach (string id in scene.LineIds ?? new string[0])
                {
                    if (!lines.Contains(id))
                    {
                        errors.Add(where + ": unknown line '" + id + "'.");
                    }
                }
            }

            return errors;
        }

        /// <summary>Every scene a tutorial region's map names exists (<c>FixedNodeData.SceneId</c>).</summary>
        public static List<string> ValidateScenes(DialogueLibraryData data, Campaign.RegionLibraryData regions)
        {
            List<string> errors = new List<string>();
            HashSet<string> scenes = new HashSet<string>(StringComparer.Ordinal);
            foreach (DialogueSceneData scene in data == null ? new DialogueSceneData[0] : data.Scenes ?? new DialogueSceneData[0])
            {
                if (scene != null && !string.IsNullOrEmpty(scene.SceneId))
                {
                    scenes.Add(scene.SceneId);
                }
            }

            foreach (Campaign.RegionData region in regions == null ? new Campaign.RegionData[0] : regions.TutorialRegions ?? new Campaign.RegionData[0])
            {
                foreach (Campaign.FixedNodeData node in region == null ? new Campaign.FixedNodeData[0] : region.FixedNodes ?? new Campaign.FixedNodeData[0])
                {
                    if (node != null && !string.IsNullOrEmpty(node.SceneId) && !scenes.Contains(node.SceneId))
                    {
                        errors.Add("Region '" + region.RegionId + "': scene '" + node.SceneId + "' is not in dialogue.json.");
                    }
                }
            }

            return errors;
        }
    }
}
