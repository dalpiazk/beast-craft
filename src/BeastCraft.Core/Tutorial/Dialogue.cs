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

        /// <summary>
        /// NPC requests for a Grove item (<c>Grove.GroveItemInventory</c>): grown, crafted or found.
        /// Added for the Grove design's D2 (<c>docs/design/grove.md</c>, "NPCs"). Resolved and fulfilled
        /// through <see cref="Npc.NpcRules"/>.
        /// </summary>
        public RequestData[] Requests = new RequestData[0];

        /// <summary>Optional per-NPC chapter chains (D2's "side stories"), never required for the main campaign.</summary>
        public SideStoryData[] SideStories = new SideStoryData[0];

        /// <summary>
        /// The dialogue layer's own small lore codex (a request or side-story chapter reward),
        /// deliberately separate from <c>Grove.GroveLibraryData.Lore</c> and <c>Discovery.DiscoveryLibraryData.Lore</c>
        /// — see <c>docs/design/grove.md</c>, "Why the Grove has its own lore list" (the same reasoning
        /// applies to the dialogue layer's).
        /// </summary>
        public NpcLoreEntryData[] Lore = new NpcLoreEntryData[0];
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
        /// Facts that must all hold for <see cref="DialogueBook.Resolve"/> (the Grove's resolution).
        /// Scene lines usually carry none: the scene plays them in order. A fact is <c>"kind:params"</c>
        /// (e.g. <c>scene:hg_shrine</c>); the Grove's own kinds (<see cref="Npc.NpcRules.BuildFacts"/> builds
        /// them: <c>affinity_tier_species</c>, <c>affinity_tier_any</c>, <c>variety_discovered</c>,
        /// <c>variety_count</c>, <c>decor_placed_count</c>, <c>habitat_unlocked</c>, <c>expedition_story</c>,
        /// <c>region_cleared</c>, <c>grove_lore_found</c>, <c>npc_lore_found</c>, <c>item_held</c>,
        /// <c>side_story_chapter</c>, <c>side_story_complete</c>) are deliberately kept in one place and
        /// one growable list (<see cref="NpcConditionKinds.All"/>) so a later PR (D3) can add new kinds —
        /// e.g. <c>colour_form_owned</c>, <c>location_soothed</c> — without touching
        /// <see cref="DialogueBook.Resolve"/> or this shape at all.
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

    /// <summary>
    /// One NPC request for a held Grove item (<see cref="Grove.GroveItemInventory"/>): available once
    /// <see cref="Conditions"/> hold, fulfilled once by <see cref="Npc.NpcRules.FulfillRequest"/> (which
    /// consumes <see cref="Count"/> of <see cref="ItemId"/> and grants <see cref="RewardKind"/>/
    /// <see cref="RewardId"/>), never again. No combat power ever, no gold that buys power — a title is
    /// earned only through an achievement, never a request reward.
    /// </summary>
    [Serializable]
    public class RequestData
    {
        public string RequestId;

        public string NpcId;

        /// <summary>See <see cref="DialogueLineData.Conditions"/>.</summary>
        public string[] Conditions = new string[0];

        /// <summary>A <see cref="Grove.GroveItemInventory"/> id: a grown variety, a crafted dye, or an expedition trinket.</summary>
        public string ItemId;

        /// <summary>How many of <see cref="ItemId"/> the request asks for. At least 1.</summary>
        public int Count = 1;

        /// <summary><c>"lore"</c> (a <see cref="NpcLoreEntryData.LoreId"/>), <c>"decor"</c> (a <c>Grove.DecorData.DecorId</c>), <c>"look"</c> (a <c>"grove"</c> cosmetic key; not authored in v1) or <c>"none"</c>.</summary>
        public string RewardKind = "none";

        public string RewardId = string.Empty;

        /// <summary>The NPC's ask (DRAFT text, the content bible's tone).</summary>
        public string Text;
    }

    /// <summary>
    /// An optional per-NPC chapter chain (docs/design/grove.md, "NPCs" — "side stories"): never
    /// required for the main campaign. Chapters form one linked chain via
    /// <see cref="SideStoryChapterData.NextChapterId"/>, from the one chapter no other chapter names to
    /// the one whose <see cref="SideStoryChapterData.NextChapterId"/> is "" (validated acyclic by
    /// <see cref="DialogueValidator"/>). Progress is tracked per save (<see cref="Npc.SideStoryState"/>)
    /// and surfaced in the compendium (<see cref="Discovery.CompendiumRules"/>).
    /// </summary>
    [Serializable]
    public class SideStoryData
    {
        public string StoryId;

        public string NpcId;

        /// <summary>The arc's own name, shown in a compendium listing (DRAFT).</summary>
        public string DisplayName;

        public SideStoryChapterData[] Chapters = new SideStoryChapterData[0];
    }

    /// <summary>
    /// One chapter of a <see cref="SideStoryData"/>: available once its own <see cref="Conditions"/>
    /// hold and every earlier chapter in the chain is complete; completing it (
    /// <see cref="Npc.NpcRules.FulfillChapter"/>) optionally consumes a held Grove item
    /// (<see cref="RequestItemId"/>/<see cref="RequestCount"/>, "" = no item needed, just the
    /// condition), grants <see cref="RewardKind"/>/<see cref="RewardId"/> and unlocks
    /// <see cref="LoreId"/> ("" = none) — "a request → reward + lore → next chapter", per the design.
    /// </summary>
    [Serializable]
    public class SideStoryChapterData
    {
        public string ChapterId;

        /// <summary>See <see cref="DialogueLineData.Conditions"/>.</summary>
        public string[] Conditions = new string[0];

        /// <summary>The NPC's line for this chapter (DRAFT text, the content bible's tone).</summary>
        public string Text;

        /// <summary>A <see cref="Grove.GroveItemInventory"/> id this chapter asks for, or "" (no item needed).</summary>
        public string RequestItemId = string.Empty;

        /// <summary>How many of <see cref="RequestItemId"/> are needed. Unused (0) when <see cref="RequestItemId"/> is "".</summary>
        public int RequestCount;

        /// <summary><c>"decor"</c>, <c>"look"</c> (not authored in v1) or <c>"none"</c>. Never <c>"lore"</c> — see <see cref="LoreId"/> for a chapter's lore.</summary>
        public string RewardKind = "none";

        public string RewardId = string.Empty;

        /// <summary>A <see cref="NpcLoreEntryData.LoreId"/> this chapter unlocks, or "" (none).</summary>
        public string LoreId = string.Empty;

        /// <summary>The next chapter's id, or "" (the arc's last chapter).</summary>
        public string NextChapterId = string.Empty;
    }

    /// <summary>The dialogue layer's own lore entry (DRAFT text, the content bible's tone): a request's or a side-story chapter's flavour reward.</summary>
    [Serializable]
    public class NpcLoreEntryData
    {
        public string LoreId;

        public string Title;

        public string Text;
    }

    /// <summary>
    /// The condition-fact vocabulary <see cref="DialogueLineData.Conditions"/>, <see cref="RequestData.Conditions"/>
    /// and <see cref="SideStoryChapterData.Conditions"/> may use, each a <c>"kind:params"</c> string
    /// (<see cref="Npc.NpcRules.BuildFacts"/> builds the live set; <see cref="DialogueValidator"/> checks every
    /// authored condition's kind is in this list). Deliberately one small, growable array: add a new
    /// kind here and start emitting it from <see cref="Npc.NpcRules.BuildFacts"/> — nothing else changes.
    /// </summary>
    public static class NpcConditionKinds
    {
        /// <summary>A tutorial scene is playing (existing, pre-Grove; <c>scene:hg_shrine</c>).</summary>
        public const string Scene = "scene";

        /// <summary>An owned beast of a species has reached at least this affinity tier (<c>affinity_tier_species:phoenix:3</c>).</summary>
        public const string AffinityTierSpecies = "affinity_tier_species";

        /// <summary>Any owned beast has reached at least this affinity tier (<c>affinity_tier_any:2</c>).</summary>
        public const string AffinityTierAny = "affinity_tier_any";

        /// <summary>A Wildgarden variety has been discovered (<c>variety_discovered:sunpetal_pure</c>).</summary>
        public const string VarietyDiscovered = "variety_discovered";

        /// <summary>At least this many herbarium varieties have been discovered (<c>variety_count:10</c>).</summary>
        public const string VarietyCount = "variety_count";

        /// <summary>At least this many decor pieces are placed, any habitat (<c>decor_placed_count:3</c>).</summary>
        public const string DecorPlacedCount = "decor_placed_count";

        /// <summary>A Grove habitat is unlocked (<c>habitat_unlocked:mossy_glade</c>).</summary>
        public const string HabitatUnlocked = "habitat_unlocked";

        /// <summary>A Board expedition story has been unlocked (<c>expedition_story:story_whispering_hollow</c>).</summary>
        public const string ExpeditionStory = "expedition_story";

        /// <summary>A region's boss has been cleared (<c>region_cleared:r01</c>).</summary>
        public const string RegionCleared = "region_cleared";

        /// <summary>A Grove lore entry has been found (<c>grove_lore_found:affinity_lore_phoenix</c>).</summary>
        public const string GroveLoreFound = "grove_lore_found";

        /// <summary>A dialogue-layer lore entry has been found (<c>npc_lore_found:trader_lore_roads</c>).</summary>
        public const string NpcLoreFound = "npc_lore_found";

        /// <summary>At least this many of a Grove item are held (<c>item_held:dye_sunwash:2</c>).</summary>
        public const string ItemHeld = "item_held";

        /// <summary>A side story's chapter has been completed (<c>side_story_chapter:trader_roads:ch2</c>).</summary>
        public const string SideStoryChapter = "side_story_chapter";

        /// <summary>A side story has been completed in full (<c>side_story_complete:trader_roads</c>).</summary>
        public const string SideStoryComplete = "side_story_complete";

        /// <summary>
        /// At least this many ordinary battle locations have ever been soothed with a Grove item
        /// instead of fought (<c>location_soothed:1</c>; <c>Campaign.CampaignProgress.LocationsSoothed</c>,
        /// cascading like <see cref="DecorPlacedCount"/>). Added for the Grove design's D3.
        /// </summary>
        public const string LocationSoothed = "location_soothed";

        /// <summary>
        /// A beast colour form is owned account-wide (<c>colour_form_owned:phoenix_ember_bloom</c>;
        /// <c>Grove.ColourFormData.ColourFormId</c>, checked against <c>PlayerSave.Cosmetics</c> — no
        /// new save shape, see <c>Grove.GroveLibraryData.ColourForms</c>). Added for the Grove
        /// design's D3.
        /// </summary>
        public const string ColourFormOwned = "colour_form_owned";

        /// <summary>Every known kind, for <see cref="DialogueValidator"/>'s allow-list check.</summary>
        public static readonly string[] All =
        {
            Scene, AffinityTierSpecies, AffinityTierAny, VarietyDiscovered, VarietyCount, DecorPlacedCount, HabitatUnlocked, ExpeditionStory, RegionCleared,
            GroveLoreFound, NpcLoreFound, ItemHeld, SideStoryChapter, SideStoryComplete, LocationSoothed, ColourFormOwned
        };

        /// <summary>Whether <paramref name="condition"/>'s <c>"kind:"</c> prefix is a known kind.</summary>
        public static bool IsKnown(string condition)
        {
            if (string.IsNullOrEmpty(condition))
            {
                return false;
            }

            int at = condition.IndexOf(':');
            string kind = at < 0 ? condition : condition.Substring(0, at);
            return Array.IndexOf(All, kind) >= 0;
        }
    }

    /// <summary>The built dialogue library: scenes in order, and condition resolution for the Grove's NPC lines.</summary>
    public sealed class DialogueBook
    {
        private readonly Dictionary<string, DialogueLineData> _lines = new Dictionary<string, DialogueLineData>(StringComparer.Ordinal);
        private readonly Dictionary<string, DialogueSceneData> _scenes = new Dictionary<string, DialogueSceneData>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcData> _npcs = new Dictionary<string, NpcData>(StringComparer.Ordinal);
        private readonly Dictionary<string, RequestData> _requests = new Dictionary<string, RequestData>(StringComparer.Ordinal);
        private readonly Dictionary<string, SideStoryData> _sideStories = new Dictionary<string, SideStoryData>(StringComparer.Ordinal);
        private readonly Dictionary<string, NpcLoreEntryData> _lore = new Dictionary<string, NpcLoreEntryData>(StringComparer.Ordinal);
        private readonly List<DialogueLineData> _order = new List<DialogueLineData>();
        private readonly List<RequestData> _requestOrder = new List<RequestData>();
        private readonly List<SideStoryData> _sideStoryOrder = new List<SideStoryData>();
        private readonly List<NpcLoreEntryData> _loreOrder = new List<NpcLoreEntryData>();

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

            foreach (RequestData request in data == null ? new RequestData[0] : data.Requests ?? new RequestData[0])
            {
                if (request != null && !string.IsNullOrEmpty(request.RequestId) && !book._requests.ContainsKey(request.RequestId))
                {
                    book._requests.Add(request.RequestId, request);
                    book._requestOrder.Add(request);
                }
            }

            foreach (SideStoryData story in data == null ? new SideStoryData[0] : data.SideStories ?? new SideStoryData[0])
            {
                if (story != null && !string.IsNullOrEmpty(story.StoryId) && !book._sideStories.ContainsKey(story.StoryId))
                {
                    book._sideStories.Add(story.StoryId, story);
                    book._sideStoryOrder.Add(story);
                }
            }

            foreach (NpcLoreEntryData lore in data == null ? new NpcLoreEntryData[0] : data.Lore ?? new NpcLoreEntryData[0])
            {
                if (lore != null && !string.IsNullOrEmpty(lore.LoreId) && !book._lore.ContainsKey(lore.LoreId))
                {
                    book._lore.Add(lore.LoreId, lore);
                    book._loreOrder.Add(lore);
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

        public RequestData Request(string requestId)
        {
            return !string.IsNullOrEmpty(requestId) && _requests.TryGetValue(requestId, out RequestData request) ? request : null;
        }

        /// <summary><paramref name="npcId"/>'s requests, in file order (empty for an unknown or requestless NPC).</summary>
        public List<RequestData> RequestsFor(string npcId)
        {
            List<RequestData> found = new List<RequestData>();
            foreach (RequestData request in _requestOrder)
            {
                if (request.NpcId == npcId)
                {
                    found.Add(request);
                }
            }

            return found;
        }

        public SideStoryData SideStory(string storyId)
        {
            return !string.IsNullOrEmpty(storyId) && _sideStories.TryGetValue(storyId, out SideStoryData story) ? story : null;
        }

        /// <summary><paramref name="npcId"/>'s side stories, in file order (empty for an unknown NPC or one with none).</summary>
        public List<SideStoryData> SideStoriesFor(string npcId)
        {
            List<SideStoryData> found = new List<SideStoryData>();
            foreach (SideStoryData story in _sideStoryOrder)
            {
                if (story.NpcId == npcId)
                {
                    found.Add(story);
                }
            }

            return found;
        }

        public NpcLoreEntryData Lore(string loreId)
        {
            return !string.IsNullOrEmpty(loreId) && _lore.TryGetValue(loreId, out NpcLoreEntryData lore) ? lore : null;
        }

        /// <summary>Every dialogue-layer lore entry, in file order (<see cref="Discovery.CompendiumRules.NpcLoreEntries"/> reads this).</summary>
        public IReadOnlyList<NpcLoreEntryData> AllLore
        {
            get { return _loreOrder; }
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

                CheckConditionKinds(errors, where, line.Conditions);
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

        /// <summary>
        /// Validates <see cref="DialogueLibraryData.Requests"/> and <see cref="DialogueLibraryData.SideStories"/>
        /// (the Grove design's D2): unique snake_case ids; every <c>NpcId</c> known; every request's and
        /// chapter's item id a real <see cref="Grove.GroveItemInventory"/> id (a Wildgarden variety, a
        /// crafted dye or an expedition trinket — <paramref name="garden"/>/<paramref name="expedition"/>);
        /// every reward id resolving for its kind (a <see cref="NpcLoreEntryData.LoreId"/> for
        /// <c>"lore"</c>, a <c>Grove.DecorData.DecorId</c> for <c>"decor"</c>, a <c>"grove"</c> look for
        /// <c>"look"</c> when <paramref name="cosmetics"/> is given); every chapter's <c>LoreId</c> known;
        /// every condition's kind known (<see cref="NpcConditionKinds"/>); every side story's chapter
        /// chain acyclic, with exactly one entry chapter and every chapter reachable from it; text
        /// length and tone.
        /// </summary>
        public static List<string> ValidateRequestsAndSideStories(DialogueLibraryData data, Grove.GroveLibraryData grove, Garden.GardenLibraryData garden,
                                                                    Expeditions.ExpeditionLibraryData expedition, Economy.CosmeticLibraryData cosmetics)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                return errors;
            }

            HashSet<string> npcs = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcData npc in data.Npcs ?? new NpcData[0])
            {
                if (npc != null && !string.IsNullOrEmpty(npc.NpcId))
                {
                    npcs.Add(npc.NpcId);
                }
            }

            HashSet<string> groveItemIds = GroveItemIds(garden, expedition);
            HashSet<string> decorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (Grove.DecorData decor in grove == null ? new Grove.DecorData[0] : grove.Decor ?? new Grove.DecorData[0])
            {
                if (decor != null && !string.IsNullOrEmpty(decor.DecorId))
                {
                    decorIds.Add(decor.DecorId);
                }
            }

            HashSet<string> loreIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcLoreEntryData lore in data.Lore ?? new NpcLoreEntryData[0])
            {
                string where = "Lore '" + lore?.LoreId + "'";
                if (lore == null || !BeastRosterValidator.IsSnakeCaseId(lore.LoreId) || !loreIds.Add(lore.LoreId))
                {
                    errors.Add(where + ": needs a unique snake_case LoreId.");
                    continue;
                }

                CheckShortText(errors, where + ": Title", lore.Title, 40);
                CheckLongText(errors, where + ": Text", lore.Text);
            }

            HashSet<string> requestIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (RequestData request in data.Requests ?? new RequestData[0])
            {
                string where = "Request '" + request?.RequestId + "'";
                if (request == null || !BeastRosterValidator.IsSnakeCaseId(request.RequestId) || !requestIds.Add(request.RequestId))
                {
                    errors.Add(where + ": needs a unique snake_case RequestId.");
                    continue;
                }

                if (!npcs.Contains(request.NpcId))
                {
                    errors.Add(where + ": unknown NpcId '" + request.NpcId + "'.");
                }

                CheckConditionKinds(errors, where, request.Conditions);
                CheckLongText(errors, where + ": Text", request.Text);

                if (string.IsNullOrEmpty(request.ItemId) || !groveItemIds.Contains(request.ItemId))
                {
                    errors.Add(where + ": ItemId '" + request.ItemId + "' is not a Grove item (a Wildgarden variety, a crafted dye or an expedition trinket).");
                }

                if (request.Count < 1)
                {
                    errors.Add(where + ": Count must be at least 1.");
                }

                CheckReward(errors, where, request.RewardKind, request.RewardId, "look_request_" + request.RequestId, loreIds, decorIds, cosmetics, true);
            }

            foreach (SideStoryData story in data.SideStories ?? new SideStoryData[0])
            {
                ValidateSideStory(errors, story, npcs, groveItemIds, decorIds, loreIds, cosmetics);
            }

            return errors;
        }

        private static void ValidateSideStory(List<string> errors, SideStoryData story, HashSet<string> npcs, HashSet<string> groveItemIds,
                                               HashSet<string> decorIds, HashSet<string> loreIds, Economy.CosmeticLibraryData cosmetics)
        {
            string storyWhere = "SideStory '" + story?.StoryId + "'";
            if (story == null || !BeastRosterValidator.IsSnakeCaseId(story.StoryId))
            {
                errors.Add(storyWhere + ": needs a unique snake_case StoryId.");
                return;
            }

            if (!npcs.Contains(story.NpcId))
            {
                errors.Add(storyWhere + ": unknown NpcId '" + story.NpcId + "'.");
            }

            CheckShortText(errors, storyWhere + ": DisplayName", story.DisplayName, 40);

            SideStoryChapterData[] chapters = story.Chapters ?? new SideStoryChapterData[0];
            if (chapters.Length == 0)
            {
                errors.Add(storyWhere + ": needs at least one chapter.");
                return;
            }

            Dictionary<string, SideStoryChapterData> byId = new Dictionary<string, SideStoryChapterData>(StringComparer.Ordinal);
            HashSet<string> nextTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (SideStoryChapterData chapter in chapters)
            {
                string where = storyWhere + " chapter '" + chapter?.ChapterId + "'";
                if (chapter == null || !BeastRosterValidator.IsSnakeCaseId(chapter.ChapterId) || byId.ContainsKey(chapter.ChapterId))
                {
                    errors.Add(where + ": needs a unique snake_case ChapterId within its story.");
                    continue;
                }

                byId[chapter.ChapterId] = chapter;
                CheckConditionKinds(errors, where, chapter.Conditions);
                CheckLongText(errors, where + ": Text", chapter.Text);

                if (!string.IsNullOrEmpty(chapter.RequestItemId))
                {
                    if (!groveItemIds.Contains(chapter.RequestItemId))
                    {
                        errors.Add(where + ": RequestItemId '" + chapter.RequestItemId + "' is not a Grove item.");
                    }

                    if (chapter.RequestCount < 1)
                    {
                        errors.Add(where + ": RequestCount must be at least 1 when RequestItemId is set.");
                    }
                }

                if (chapter.RewardKind == "lore")
                {
                    errors.Add(where + ": RewardKind may not be 'lore' — use LoreId for a chapter's lore reward.");
                }
                else
                {
                    CheckReward(errors, where, chapter.RewardKind, chapter.RewardId, "look_" + story.StoryId + "_" + chapter.ChapterId, null, decorIds, cosmetics, false);
                }

                if (!string.IsNullOrEmpty(chapter.LoreId) && !loreIds.Contains(chapter.LoreId))
                {
                    errors.Add(where + ": LoreId '" + chapter.LoreId + "' is not in dialogue.json's Lore list.");
                }
            }

            foreach (SideStoryChapterData chapter in chapters)
            {
                if (chapter == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(chapter.NextChapterId))
                {
                    if (!byId.ContainsKey(chapter.NextChapterId))
                    {
                        errors.Add(storyWhere + " chapter '" + chapter.ChapterId + "': NextChapterId '" + chapter.NextChapterId + "' is not a chapter of this story.");
                    }
                    else if (!nextTargets.Add(chapter.NextChapterId))
                    {
                        errors.Add(storyWhere + ": two chapters both name '" + chapter.NextChapterId + "' as NextChapterId.");
                    }
                }
            }

            List<SideStoryChapterData> entries = new List<SideStoryChapterData>();
            foreach (SideStoryChapterData chapter in byId.Values)
            {
                if (!nextTargets.Contains(chapter.ChapterId))
                {
                    entries.Add(chapter);
                }
            }

            if (entries.Count != 1)
            {
                errors.Add(storyWhere + ": needs exactly one entry chapter (one no other chapter names as NextChapterId); found " + entries.Count + ".");
                return;
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            SideStoryChapterData cursor = entries[0];
            while (cursor != null && visited.Add(cursor.ChapterId))
            {
                cursor = string.IsNullOrEmpty(cursor.NextChapterId) ? null : (byId.TryGetValue(cursor.NextChapterId, out SideStoryChapterData next) ? next : null);
            }

            if (cursor != null)
            {
                errors.Add(storyWhere + ": its chapter chain cycles back to '" + cursor.ChapterId + "'.");
            }
            else if (visited.Count != byId.Count)
            {
                errors.Add(storyWhere + ": " + (byId.Count - visited.Count) + " chapter(s) are unreachable from the entry chapter.");
            }
        }

        private static void CheckReward(List<string> errors, string where, string rewardKind, string rewardId, string lookUnlockId, HashSet<string> loreIds,
                                        HashSet<string> decorIds, Economy.CosmeticLibraryData cosmetics, bool loreAllowed)
        {
            switch (rewardKind)
            {
                case "lore" when loreAllowed:
                    if (loreIds == null || string.IsNullOrEmpty(rewardId) || !loreIds.Contains(rewardId))
                    {
                        errors.Add(where + ": RewardId '" + rewardId + "' is not in dialogue.json's Lore list.");
                    }

                    break;
                case "decor":
                    if (string.IsNullOrEmpty(rewardId) || !decorIds.Contains(rewardId))
                    {
                        errors.Add(where + ": RewardId '" + rewardId + "' is not a known Grove decor id.");
                    }

                    break;
                case "look":
                    CheckLook(errors, where + ": look reward", rewardId, lookUnlockId, cosmetics);
                    break;
                case "none":
                    if (!string.IsNullOrEmpty(rewardId))
                    {
                        errors.Add(where + ": RewardKind none must have an empty RewardId.");
                    }

                    break;
                default:
                    errors.Add(where + ": RewardKind must be " + (loreAllowed ? "lore, decor, look or none." : "decor, look or none."));
                    break;
            }
        }

        private static void CheckLook(List<string> errors, string at, string key, string unlockId, Economy.CosmeticLibraryData cosmetics)
        {
            if (string.IsNullOrEmpty(key))
            {
                errors.Add(at + " is missing.");
                return;
            }

            if (cosmetics == null)
            {
                return;
            }

            string[] parts = key.Split('/');
            Economy.CosmeticCategoryData category = parts.Length != 2
                                                          ? null
                                                          : Array.Find(cosmetics.Categories ?? new Economy.CosmeticCategoryData[0], c => c != null && c.CategoryId == parts[0]);
            Economy.CosmeticOptionData option = category == null
                                                     ? null
                                                     : Array.Find(category.Options ?? new Economy.CosmeticOptionData[0], o => o != null && o.OptionId == parts[1]);
            if (option == null || option.Source != Economy.CosmeticLibrary.SourceGrove || option.UnlockId != unlockId)
            {
                errors.Add(at + " '" + key + "' must be a grove look whose UnlockId is '" + unlockId + "'.");
            }
        }

        /// <summary>Every <see cref="Grove.GroveItemInventory"/> id the content can grant: Wildgarden varieties, crafted dyes and expedition trinkets.</summary>
        private static HashSet<string> GroveItemIds(Garden.GardenLibraryData garden, Expeditions.ExpeditionLibraryData expedition)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Garden.VarietyData variety in garden == null ? new Garden.VarietyData[0] : garden.Varieties ?? new Garden.VarietyData[0])
            {
                if (variety != null && !string.IsNullOrEmpty(variety.VarietyId))
                {
                    ids.Add(variety.VarietyId);
                }
            }

            foreach (Garden.RecipeData recipe in garden == null ? new Garden.RecipeData[0] : garden.Recipes ?? new Garden.RecipeData[0])
            {
                if (recipe != null && recipe.Output == "dye" && !string.IsNullOrEmpty(recipe.OutputId))
                {
                    ids.Add(recipe.OutputId);
                }
            }

            foreach (Expeditions.ExpeditionOutcomeTableData table in expedition == null
                                                                          ? new Expeditions.ExpeditionOutcomeTableData[0]
                                                                          : expedition.OutcomeTables ?? new Expeditions.ExpeditionOutcomeTableData[0])
            {
                foreach (Expeditions.ExpeditionOutcomeEntryData entry in table == null
                                                                              ? new Expeditions.ExpeditionOutcomeEntryData[0]
                                                                              : table.Entries ?? new Expeditions.ExpeditionOutcomeEntryData[0])
                {
                    if (entry != null && entry.Kind == "trinket" && !string.IsNullOrEmpty(entry.Id))
                    {
                        ids.Add(entry.Id);
                    }
                }
            }

            return ids;
        }

        private static void CheckConditionKinds(List<string> errors, string where, string[] conditions)
        {
            foreach (string condition in conditions ?? new string[0])
            {
                if (!NpcConditionKinds.IsKnown(condition))
                {
                    errors.Add(where + ": condition '" + condition + "' has an unknown kind (see NpcConditionKinds).");
                }
            }
        }

        private static void CheckShortText(List<string> errors, string at, string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength || text.IndexOf('\'') >= 0)
            {
                errors.Add(at + " must be 1-" + maxLength + " characters with no apostrophe.");
            }
        }

        private static void CheckLongText(List<string> errors, string at, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLineLength)
            {
                errors.Add(at + " must be 1-" + MaxLineLength + " characters.");
            }
            else if (!ToneCheck.IsClean(text))
            {
                errors.Add(at + ": no one dies in this world (content bible).");
            }
        }
    }
}
