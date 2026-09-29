using System;
using System.Collections.Generic;

namespace BeastCraft.Npc
{
    /// <summary>
    /// The NPC dialogue layer's account-wide state of one save (<c>PlayerSave.Npc</c>, schema 10 — the
    /// Grove design's D2 deliverable, folded into schema 10 in place because it had not shipped yet;
    /// see <c>docs/design/grove.md</c>, "D2"): every dialogue line seen (<see cref="Dialogue"/>), every
    /// NPC request fulfilled, once each (<see cref="RequestsFulfilled"/>), the NPC/side-story lore
    /// found (<see cref="LoreIds"/> — the dialogue layer's own small codex, separate from the Grove's
    /// and the discovery layer's, the same "each domain owns its own small lore list" shape as
    /// <c>Grove.GroveProgress.LoreIds</c>) and every side story's chapter progress
    /// (<see cref="SideStories"/>). Plain serializable data with public fields; the rules are
    /// <see cref="NpcRules"/>'.
    /// </summary>
    [Serializable]
    public class NpcProgress
    {
        public DialogueState Dialogue = new DialogueState();

        /// <summary>Every <c>Tutorial.RequestData.RequestId</c> fulfilled, each once, in order.</summary>
        public List<string> RequestsFulfilled = new List<string>();

        /// <summary>The dialogue layer's own lore entries found (a request or side-story chapter reward), each once, in order.</summary>
        public List<string> LoreIds = new List<string>();

        public List<SideStoryState> SideStories = new List<SideStoryState>();

        /// <summary>Whether request <paramref name="requestId"/> has already been fulfilled.</summary>
        public bool HasFulfilled(string requestId)
        {
            return !string.IsNullOrEmpty(requestId) && RequestsFulfilled != null && RequestsFulfilled.Contains(requestId);
        }

        /// <summary>Side story <paramref name="storyId"/>'s chapter progress, or null (never started).</summary>
        public SideStoryState FindSideStory(string storyId)
        {
            if (string.IsNullOrEmpty(storyId) || SideStories == null)
            {
                return null;
            }

            foreach (SideStoryState state in SideStories)
            {
                if (state != null && state.StoryId == storyId)
                {
                    return state;
                }
            }

            return null;
        }

        /// <summary>Side story <paramref name="storyId"/>'s chapter progress, creating an empty one (no chapter completed) if it has none yet.</summary>
        public SideStoryState SideStoryOf(string storyId)
        {
            SideStoryState state = FindSideStory(storyId);
            if (state != null)
            {
                return state;
            }

            state = new SideStoryState { StoryId = storyId };
            SideStories.Add(state);
            return state;
        }

        /// <summary>Replaces null lists and sub-objects with empty ones and drops empty and repeated ids. Returns how many things were repaired.</summary>
        public int EnsureInitialized()
        {
            int repaired = 0;
            if (Dialogue == null)
            {
                Dialogue = new DialogueState();
                repaired++;
            }

            repaired += Repair(ref Dialogue.LinesSeen);
            repaired += Repair(ref RequestsFulfilled);
            repaired += Repair(ref LoreIds);

            if (SideStories == null)
            {
                SideStories = new List<SideStoryState>();
                repaired++;
            }

            repaired += SideStories.RemoveAll(state => state == null || string.IsNullOrEmpty(state.StoryId));
            HashSet<string> storyIds = new HashSet<string>(StringComparer.Ordinal);
            repaired += SideStories.RemoveAll(state => !storyIds.Add(state.StoryId));
            foreach (SideStoryState state in SideStories)
            {
                repaired += Repair(ref state.ChaptersCompleted);
            }

            return repaired;
        }

        private static int Repair(ref List<string> list)
        {
            int repaired = 0;
            if (list == null)
            {
                list = new List<string>();
                repaired++;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            repaired += list.RemoveAll(id => string.IsNullOrEmpty(id) || !seen.Add(id));
            return repaired;
        }
    }

    /// <summary>Every dialogue line the player has ever been shown (<see cref="Tutorial.DialogueBook.Resolve"/>), for a later "seen before" or journal screen.</summary>
    [Serializable]
    public class DialogueState
    {
        public List<string> LinesSeen = new List<string>();
    }

    /// <summary>One side story's chapter progress (<see cref="NpcProgress.SideStories"/>).</summary>
    [Serializable]
    public class SideStoryState
    {
        public string StoryId;

        /// <summary>Every <c>Tutorial.SideStoryChapterData.ChapterId</c> completed, in the order completed.</summary>
        public List<string> ChaptersCompleted = new List<string>();
    }
}
