using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Localization;
using BeastCraft.Save;
using BeastCraft.Tutorial;

namespace BeastCraft.Npc
{
    /// <summary>
    /// The NPC dialogue layer's rules (docs/design/grove.md, "NPCs" — D2): deterministic,
    /// non-throwing, refuse-and-no-op on bad input (the <c>Grove.GroveRules</c>/<c>CosmeticRules</c>
    /// idiom). No combat power anywhere: nothing here touches stats, damage or campaign difficulty; a
    /// title is earned only through an achievement, never a request or side-story reward.
    /// <para>
    /// <see cref="BuildFacts"/> is the one place account state becomes the flat fact set
    /// <see cref="Tutorial.DialogueBook.Resolve"/>, <see cref="IsRequestAvailable"/> and
    /// <see cref="IsChapterAvailable"/> all read against — see <see cref="Tutorial.NpcConditionKinds"/>
    /// for the vocabulary and how to grow it.
    /// </para>
    /// </summary>
    public static class NpcRules
    {
        // ---- Facts ----

        /// <summary>
        /// The live condition-fact set for <paramref name="save"/>: every <see cref="Tutorial.NpcConditionKinds"/>
        /// fact currently true, account-wide (not scoped to one beast or habitat — a species- or
        /// count-scoped fact is emitted for every tier/count actually reached, e.g. a beast at tier 3
        /// also emits its tier-1 and tier-2 facts, so a line asking for "at least tier 2" matches).
        /// Pure: reads <paramref name="save"/> and the content libraries only, never writes.
        /// </summary>
        public static HashSet<string> BuildFacts(PlayerSave save, GroveLibrary grove, GardenLibrary garden, ExpeditionLibrary expeditions)
        {
            HashSet<string> facts = new HashSet<string>(StringComparer.Ordinal);
            if (save == null)
            {
                return facts;
            }

            save.EnsureInitialized();

            int maxTierAny = 0;
            Dictionary<string, int> maxTierBySpecies = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (BeastAffinityState state in save.Grove.Affinity)
            {
                if (state == null || state.Tier <= 0)
                {
                    continue;
                }

                maxTierAny = Math.Max(maxTierAny, state.Tier);
                OwnedBeast beast = save.FindBeast(state.BeastId);
                if (beast?.Progress == null || string.IsNullOrEmpty(beast.Progress.SpeciesId))
                {
                    continue;
                }

                string speciesId = beast.Progress.SpeciesId;
                if (!maxTierBySpecies.TryGetValue(speciesId, out int best) || state.Tier > best)
                {
                    maxTierBySpecies[speciesId] = state.Tier;
                }
            }

            for (int tier = 1; tier <= maxTierAny; tier++)
            {
                facts.Add(NpcConditionKinds.AffinityTierAny + ":" + Int(tier));
            }

            foreach (KeyValuePair<string, int> species in maxTierBySpecies)
            {
                for (int tier = 1; tier <= species.Value; tier++)
                {
                    facts.Add(NpcConditionKinds.AffinityTierSpecies + ":" + species.Key + ":" + Int(tier));
                }
            }

            foreach (string habitatId in save.Grove.HabitatsUnlocked)
            {
                facts.Add(NpcConditionKinds.HabitatUnlocked + ":" + habitatId);
            }

            foreach (string loreId in save.Grove.LoreIds)
            {
                facts.Add(NpcConditionKinds.GroveLoreFound + ":" + loreId);
            }

            int placedDecor = save.Grove.PlacedDecor == null ? 0 : save.Grove.PlacedDecor.Count;
            for (int n = 1; n <= placedDecor; n++)
            {
                facts.Add(NpcConditionKinds.DecorPlacedCount + ":" + Int(n));
            }

            foreach (GroveItemStack stack in save.Grove.Items.Items)
            {
                if (stack == null || string.IsNullOrEmpty(stack.ItemId))
                {
                    continue;
                }

                for (int n = 1; n <= stack.Quantity; n++)
                {
                    facts.Add(NpcConditionKinds.ItemHeld + ":" + stack.ItemId + ":" + Int(n));
                }
            }

            foreach (string varietyId in save.Garden.VarietiesDiscovered)
            {
                facts.Add(NpcConditionKinds.VarietyDiscovered + ":" + varietyId);
            }

            int varieties = save.Garden.VarietiesDiscovered == null ? 0 : save.Garden.VarietiesDiscovered.Count;
            for (int n = 1; n <= varieties; n++)
            {
                facts.Add(NpcConditionKinds.VarietyCount + ":" + Int(n));
            }

            foreach (string storyId in save.Expeditions.StoriesUnlocked)
            {
                facts.Add(NpcConditionKinds.ExpeditionStory + ":" + storyId);
            }

            foreach (Campaign.RegionProgress region in save.Campaign.Regions)
            {
                if (region != null && region.BossCleared && !string.IsNullOrEmpty(region.RegionId))
                {
                    facts.Add(NpcConditionKinds.RegionCleared + ":" + region.RegionId);
                }
            }

            foreach (string loreId in save.Npc.LoreIds)
            {
                facts.Add(NpcConditionKinds.NpcLoreFound + ":" + loreId);
            }

            foreach (SideStoryState state in save.Npc.SideStories)
            {
                if (state == null || string.IsNullOrEmpty(state.StoryId) || state.ChaptersCompleted == null)
                {
                    continue;
                }

                foreach (string chapterId in state.ChaptersCompleted)
                {
                    facts.Add(NpcConditionKinds.SideStoryChapter + ":" + state.StoryId + ":" + chapterId);
                }
            }

            for (int n = 1; n <= save.Campaign.LocationsSoothed; n++)
            {
                facts.Add(NpcConditionKinds.LocationSoothed + ":" + Int(n));
            }

            if (grove != null)
            {
                foreach (Grove.ColourFormData form in grove.Data.ColourForms ?? new Grove.ColourFormData[0])
                {
                    if (form == null || string.IsNullOrEmpty(form.ColourFormId))
                    {
                        continue;
                    }

                    string key = CosmeticCollection.Key(form.CosmeticCategoryId, form.CosmeticOptionId);
                    if (save.Cosmetics != null && save.Cosmetics.Has(key))
                    {
                        facts.Add(NpcConditionKinds.ColourFormOwned + ":" + form.ColourFormId);
                    }
                }
            }

            return facts;
        }

        private static string Int(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // ---- Dialogue ----

        /// <summary>
        /// <paramref name="book"/>'s resolution for <paramref name="npcId"/> against <paramref name="facts"/>
        /// (<see cref="DialogueBook.Resolve"/>), recording the chosen line as seen
        /// (<see cref="NpcProgress.Dialogue"/>) when one is found. Returns null when nothing matches.
        /// </summary>
        public static DialogueLineData ResolveAndMark(PlayerSave save, DialogueBook book, string npcId, ICollection<string> facts)
        {
            return ResolveAndMark(save, book, npcId, facts, out bool _);
        }

        /// <summary>
        /// <see cref="ResolveAndMark(PlayerSave, DialogueBook, string, ICollection{string})"/>, also
        /// reporting whether the resolved line was newly added to <see cref="NpcProgress.Dialogue"/>'s
        /// <c>LinesSeen</c> (false when it was already seen, or nothing resolved) — so a caller can
        /// autosave only when this call actually changed the save.
        /// </summary>
        public static DialogueLineData ResolveAndMark(PlayerSave save, DialogueBook book, string npcId, ICollection<string> facts, out bool newlySeen)
        {
            newlySeen = false;
            if (save == null || book == null)
            {
                return null;
            }

            DialogueLineData line = book.Resolve(npcId, facts);
            if (line != null)
            {
                save.EnsureInitialized();
                if (!save.Npc.Dialogue.LinesSeen.Contains(line.LineId))
                {
                    save.Npc.Dialogue.LinesSeen.Add(line.LineId);
                    newlySeen = true;
                }
            }

            return line;
        }

        // ---- Requests ----

        /// <summary>Whether request <paramref name="request"/> has already been fulfilled.</summary>
        public static bool IsRequestFulfilled(PlayerSave save, RequestData request)
        {
            return request != null && save?.Npc != null && save.Npc.HasFulfilled(request.RequestId);
        }

        /// <summary>Whether <paramref name="request"/> is showable: not yet fulfilled and its conditions hold. Does not check whether the item is held.</summary>
        public static bool IsRequestAvailable(PlayerSave save, RequestData request, ICollection<string> facts)
        {
            return request != null && !IsRequestFulfilled(save, request) && ConditionsHold(request.Conditions, facts);
        }

        /// <summary>Whether <paramref name="request"/> could be fulfilled right now: available and enough of its item is held.</summary>
        public static bool CanFulfillRequest(PlayerSave save, RequestData request, ICollection<string> facts)
        {
            return IsRequestAvailable(save, request, facts) && save?.Grove?.Items != null && save.Grove.Items.GetCount(request.ItemId) >= request.Count;
        }

        /// <summary>
        /// Fulfils <paramref name="request"/>: refuses unless <see cref="CanFulfillRequest"/>; otherwise
        /// consumes its item, grants its reward (lore, decor, or a look via <paramref name="cosmetics"/>)
        /// and records it fulfilled, once, forever.
        /// </summary>
        public static NpcActionResult FulfillRequest(PlayerSave save, RequestData request, ICollection<string> facts, CosmeticLibrary cosmetics = null)
        {
            if (save == null || request == null)
            {
                return NpcActionResult.Refused("No save or request.");
            }

            save.EnsureInitialized();
            if (!CanFulfillRequest(save, request, facts))
            {
                return NpcActionResult.Refused(RulesText.Get("ui.rules.npc.request_not_ready"));
            }

            save.Grove.Items.TryConsume(request.ItemId, request.Count);
            NpcActionResult result = ApplyReward(save, request.RewardKind, request.RewardId, string.Empty, cosmetics);
            AddOnce(save.Npc.RequestsFulfilled, request.RequestId);
            return result;
        }

        // ---- Side stories ----

        /// <summary>
        /// The chapter of <paramref name="story"/> the player is currently on: the first chapter, in
        /// chain order (<see cref="SideStoryChapterData.NextChapterId"/>), not yet in
        /// <see cref="SideStoryState.ChaptersCompleted"/>. Null once every chapter is complete or the
        /// story has no chapters.
        /// </summary>
        public static SideStoryChapterData CurrentChapter(PlayerSave save, SideStoryData story)
        {
            if (story?.Chapters == null || story.Chapters.Length == 0)
            {
                return null;
            }

            SideStoryState state = save?.Npc?.FindSideStory(story.StoryId);
            List<string> completed = state?.ChaptersCompleted ?? new List<string>();

            Dictionary<string, SideStoryChapterData> byId = new Dictionary<string, SideStoryChapterData>(StringComparer.Ordinal);
            HashSet<string> nextTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (SideStoryChapterData chapter in story.Chapters)
            {
                if (chapter == null || string.IsNullOrEmpty(chapter.ChapterId))
                {
                    continue;
                }

                byId[chapter.ChapterId] = chapter;
                if (!string.IsNullOrEmpty(chapter.NextChapterId))
                {
                    nextTargets.Add(chapter.NextChapterId);
                }
            }

            SideStoryChapterData cursor = null;
            foreach (SideStoryChapterData chapter in byId.Values)
            {
                if (!nextTargets.Contains(chapter.ChapterId))
                {
                    cursor = chapter;
                    break;
                }
            }

            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
            while (cursor != null && completed.Contains(cursor.ChapterId) && visited.Add(cursor.ChapterId))
            {
                cursor = string.IsNullOrEmpty(cursor.NextChapterId) ? null : (byId.TryGetValue(cursor.NextChapterId, out SideStoryChapterData next) ? next : null);
            }

            return cursor != null && completed.Contains(cursor.ChapterId) ? null : cursor;
        }

        /// <summary>Whether every chapter of <paramref name="story"/> has been completed.</summary>
        public static bool IsSideStoryComplete(PlayerSave save, SideStoryData story)
        {
            return story?.Chapters != null && story.Chapters.Length > 0 && CurrentChapter(save, story) == null;
        }

        /// <summary>Overload for <c>Progression.AchievementRules</c>: looks <paramref name="storyId"/> up in <paramref name="book"/> first.</summary>
        public static bool IsSideStoryComplete(PlayerSave save, DialogueBook book, string storyId)
        {
            SideStoryData story = book?.SideStory(storyId);
            return story != null && IsSideStoryComplete(save, story);
        }

        /// <summary>Whether <paramref name="story"/>'s current chapter is showable: one remains and its conditions hold.</summary>
        public static bool IsChapterAvailable(PlayerSave save, SideStoryData story, ICollection<string> facts)
        {
            SideStoryChapterData chapter = CurrentChapter(save, story);
            return chapter != null && ConditionsHold(chapter.Conditions, facts);
        }

        /// <summary>Whether <paramref name="story"/>'s current chapter could be completed right now: available and, if it asks for an item, enough is held.</summary>
        public static bool CanFulfillChapter(PlayerSave save, SideStoryData story, ICollection<string> facts)
        {
            if (!IsChapterAvailable(save, story, facts))
            {
                return false;
            }

            SideStoryChapterData chapter = CurrentChapter(save, story);
            return string.IsNullOrEmpty(chapter.RequestItemId) || (save?.Grove?.Items != null && save.Grove.Items.GetCount(chapter.RequestItemId) >= chapter.RequestCount);
        }

        /// <summary>
        /// Completes <paramref name="story"/>'s current chapter: refuses unless <see cref="CanFulfillChapter"/>;
        /// otherwise consumes its item (if any), grants its reward and lore, and advances the chain.
        /// </summary>
        public static NpcActionResult FulfillChapter(PlayerSave save, SideStoryData story, ICollection<string> facts, CosmeticLibrary cosmetics = null)
        {
            if (save == null || story == null)
            {
                return NpcActionResult.Refused("No save or side story.");
            }

            save.EnsureInitialized();
            if (!CanFulfillChapter(save, story, facts))
            {
                return NpcActionResult.Refused(RulesText.Get("ui.rules.npc.chapter_not_ready"));
            }

            SideStoryChapterData chapter = CurrentChapter(save, story);
            if (!string.IsNullOrEmpty(chapter.RequestItemId))
            {
                save.Grove.Items.TryConsume(chapter.RequestItemId, chapter.RequestCount);
            }

            NpcActionResult result = ApplyReward(save, chapter.RewardKind, chapter.RewardId, chapter.LoreId, cosmetics);
            AddOnce(save.Npc.SideStoryOf(story.StoryId).ChaptersCompleted, chapter.ChapterId);
            return result;
        }

        // ---- Shared ----

        private static bool ConditionsHold(string[] conditions, ICollection<string> facts)
        {
            foreach (string condition in conditions ?? new string[0])
            {
                if (facts == null || !facts.Contains(condition))
                {
                    return false;
                }
            }

            return true;
        }

        private static NpcActionResult ApplyReward(PlayerSave save, string rewardKind, string rewardId, string loreId, CosmeticLibrary cosmetics)
        {
            switch (rewardKind)
            {
                case "lore":
                    AddOnce(save.Npc.LoreIds, rewardId);
                    break;
                case "decor":
                    AddOnce(save.Grove.UnlockedDecorIds, rewardId);
                    break;
                case "look":
                    if (cosmetics != null)
                    {
                        CosmeticRules.UnlockOrRefund(save, cosmetics, rewardId, out int _);
                    }

                    break;
            }

            if (!string.IsNullOrEmpty(loreId))
            {
                AddOnce(save.Npc.LoreIds, loreId);
            }

            return NpcActionResult.Succeeded(rewardKind, rewardId, loreId);
        }

        private static bool AddOnce(List<string> list, string id)
        {
            if (list == null || string.IsNullOrEmpty(id) || list.Contains(id))
            {
                return false;
            }

            list.Add(id);
            return true;
        }
    }

    /// <summary>What an NPC action (<see cref="NpcRules.FulfillRequest"/>, <see cref="NpcRules.FulfillChapter"/>) granted.</summary>
    public sealed class NpcActionResult
    {
        public bool Success { get; private set; } = true;

        public string Error { get; private set; }

        /// <summary>The mechanical reward's kind ("lore", "decor", "look" or "none"). Empty when refused.</summary>
        public string RewardKind { get; private set; } = string.Empty;

        public string RewardId { get; private set; } = string.Empty;

        /// <summary>A side-story chapter's own lore unlocked alongside the mechanical reward, or "" (a request, or none).</summary>
        public string LoreId { get; private set; } = string.Empty;

        internal static NpcActionResult Succeeded(string rewardKind, string rewardId, string loreId)
        {
            return new NpcActionResult { RewardKind = rewardKind ?? string.Empty, RewardId = rewardId ?? string.Empty, LoreId = loreId ?? string.Empty };
        }

        internal static NpcActionResult Refused(string error)
        {
            return new NpcActionResult { Success = false, Error = error };
        }
    }
}
