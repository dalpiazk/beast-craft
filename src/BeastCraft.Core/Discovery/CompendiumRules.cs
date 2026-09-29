using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Save;

namespace BeastCraft.Discovery
{
    /// <summary>How a compendium beast entry stands.</summary>
    public enum CompendiumBeastState
    {
        /// <summary>Never owned, and not currently offered.</summary>
        Unknown = 0,

        /// <summary>Currently offered by a pending Kinship choice (a live preview, not stored).</summary>
        Offered = 1,

        Owned = 2
    }

    /// <summary>One beast's compendium entry: whether it is owned (and how it joined).</summary>
    public sealed class CompendiumBeastEntry
    {
        public string SpeciesId;

        public CompendiumBeastState State;

        /// <summary>Whether it joined through a Kinship site rather than a Hearthglen pick (or an older save's starting six).</summary>
        public bool JoinedThroughKinship;

        /// <summary>The Kinship site claimed for it, or "" when it did not join through Kinship.</summary>
        public string KinshipSiteId;
    }

    /// <summary>One lore entry's compendium state: the content and whether it has been found.</summary>
    public sealed class CompendiumLoreEntry
    {
        public string LoreId;

        public string RegionId;

        public string Title;

        public string Text;

        public bool Found;
    }

    /// <summary>The compendium's completion counts (beasts, lore, Kinship sites) and its own percent.</summary>
    public sealed class CompendiumCompletion
    {
        public int BeastsOwned;

        public int BeastsTotal;

        public int LoreFound;

        public int LoreTotal;

        public int KinshipClaimed;

        public int KinshipTotal;

        /// <summary>0-100, rounded down, over every count above together; 100 only when everything is found.</summary>
        public int Percent;
    }

    /// <summary>
    /// The Collector persona's compendium (docs/design/kinship-discovery.md, "Seams for PR B"): a pure
    /// derived view over save state, never its own storage. Per-beast entries (owned, or currently
    /// offered by a pending Kinship choice; whether it joined through Kinship, and which site), the
    /// lore entries found (<see cref="DiscoveryProgress.LoreIds"/>, <c>LoreEntryData</c>'s title and
    /// text), and completion counts over both plus the Kinship sites claimed.
    /// <para>
    /// <strong>Enemies encountered are not part of the compendium.</strong> The save tracks no
    /// per-enemy "seen" state today (only points of interest, lore and Kinship sites), and adding one
    /// would be new tracking this deliverable has no cheap, save-derivable seam for; left for a later
    /// pass if the producer wants it.
    /// </para>
    /// </summary>
    public static class CompendiumRules
    {
        /// <summary>Every roster species' compendium entry, in roster order.</summary>
        public static List<CompendiumBeastEntry> BeastEntries(PlayerSave save, DiscoveryContent content)
        {
            List<CompendiumBeastEntry> entries = new List<CompendiumBeastEntry>();
            if (save == null || content == null || content.Roster == null)
            {
                return entries;
            }

            save.EnsureInitialized();
            HashSet<string> owned = KinshipRules.OwnedSpecies(save);
            List<string> offered = new List<string>();
            if (save.Discovery.HasPendingKinship)
            {
                KinshipResult pending = KinshipRules.Pending(save, content);
                if (pending.Success)
                {
                    offered = pending.Offer;
                }
            }

            foreach (CreatureSpeciesSO species in content.Roster)
            {
                if (species == null || string.IsNullOrEmpty(species.SpeciesId))
                {
                    continue;
                }

                KinshipJoinRecord join = save.Discovery.FindKinshipJoin(species.SpeciesId);
                entries.Add(new CompendiumBeastEntry
                {
                    SpeciesId = species.SpeciesId,
                    State = owned.Contains(species.SpeciesId) ? CompendiumBeastState.Owned
                            : Contains(offered, species.SpeciesId) ? CompendiumBeastState.Offered
                            : CompendiumBeastState.Unknown,
                    JoinedThroughKinship = join != null,
                    KinshipSiteId = join == null ? string.Empty : join.SiteId
                });
            }

            return entries;
        }

        /// <summary>Every lore entry in the game, in file order, each with whether it has been found.</summary>
        public static List<CompendiumLoreEntry> LoreEntries(PlayerSave save, DiscoveryContent content)
        {
            List<CompendiumLoreEntry> entries = new List<CompendiumLoreEntry>();
            if (save == null || content == null || content.Library == null)
            {
                return entries;
            }

            foreach (LoreEntryData lore in content.Library.Data.Lore ?? new LoreEntryData[0])
            {
                if (lore == null || string.IsNullOrEmpty(lore.LoreId))
                {
                    continue;
                }

                entries.Add(new CompendiumLoreEntry
                {
                    LoreId = lore.LoreId,
                    RegionId = lore.RegionId,
                    Title = lore.Title,
                    Text = lore.Text,
                    Found = save.Discovery.LoreIds != null && save.Discovery.LoreIds.Contains(lore.LoreId)
                });
            }

            return entries;
        }

        /// <summary>
        /// Every dialogue-layer lore entry (an NPC request's or side-story chapter's reward — see
        /// <c>docs/design/grove.md</c>, "NPCs"), in file order, each with whether it has been found
        /// (<c>PlayerSave.Npc.LoreIds</c>). Kept separate from <see cref="LoreEntries"/> (the discovery
        /// layer's own lore) and deliberately **not** folded into <see cref="Completion"/>/its
        /// <c>Percent</c> — that stays scoped to what it already covers (beasts, discovery lore, Kinship
        /// sites), the same "each domain owns its own small lore list" shape used throughout.
        /// </summary>
        public static List<CompendiumLoreEntry> NpcLoreEntries(PlayerSave save, Tutorial.DialogueBook dialogue)
        {
            List<CompendiumLoreEntry> entries = new List<CompendiumLoreEntry>();
            if (save == null || dialogue == null)
            {
                return entries;
            }

            save.EnsureInitialized();
            foreach (Tutorial.NpcLoreEntryData lore in dialogue.AllLore)
            {
                if (lore == null || string.IsNullOrEmpty(lore.LoreId))
                {
                    continue;
                }

                entries.Add(new CompendiumLoreEntry
                {
                    LoreId = lore.LoreId,
                    RegionId = string.Empty,
                    Title = lore.Title,
                    Text = lore.Text,
                    Found = save.Npc.LoreIds != null && save.Npc.LoreIds.Contains(lore.LoreId)
                });
            }

            return entries;
        }

        /// <summary>The compendium's completion: beasts owned, lore found and Kinship sites claimed, and their combined percent. Null without discovery content.</summary>
        public static CompendiumCompletion Completion(PlayerSave save, DiscoveryContent content)
        {
            if (save == null || content == null)
            {
                return null;
            }

            List<CompendiumBeastEntry> beasts = BeastEntries(save, content);
            List<CompendiumLoreEntry> lore = LoreEntries(save, content);
            CompendiumCompletion completion = new CompendiumCompletion
            {
                BeastsTotal = beasts.Count,
                LoreTotal = lore.Count,
                KinshipTotal = content.Library == null ? 0 : content.Library.Sites.Count,
                KinshipClaimed = save.Discovery.ClaimedKinshipIds == null ? 0 : save.Discovery.ClaimedKinshipIds.Count
            };

            foreach (CompendiumBeastEntry beast in beasts)
            {
                completion.BeastsOwned += beast.State == CompendiumBeastState.Owned ? 1 : 0;
            }

            foreach (CompendiumLoreEntry entry in lore)
            {
                completion.LoreFound += entry.Found ? 1 : 0;
            }

            completion.KinshipClaimed = Math.Min(completion.KinshipClaimed, completion.KinshipTotal);
            int done = completion.BeastsOwned + completion.LoreFound + completion.KinshipClaimed;
            int total = completion.BeastsTotal + completion.LoreTotal + completion.KinshipTotal;
            bool complete = completion.BeastsOwned >= completion.BeastsTotal && completion.LoreFound >= completion.LoreTotal &&
                             completion.KinshipClaimed >= completion.KinshipTotal;
            completion.Percent = total <= 0 ? 0 : complete ? 100 : Math.Min(99, (int)(100L * done / total));
            return completion;
        }

        private static bool Contains(List<string> ids, string id)
        {
            foreach (string other in ids ?? new List<string>())
            {
                if (string.Equals(other, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
