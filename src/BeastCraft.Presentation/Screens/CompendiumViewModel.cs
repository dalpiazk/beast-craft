using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Discovery;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// One beast entry of the compendium screen: unknown (a silhouette), currently offered by a
    /// pending Kinship choice, or owned — and, when it joined through Kinship, the site it joined at.
    /// </summary>
    public sealed class CompendiumBeastRow
    {
        public string SpeciesId;

        /// <summary>The species' name once known; "???" for an unknown silhouette.</summary>
        public string Name;

        public string ArtKey;
        public Element Element;
        public CombatStance Stance;
        public CompendiumBeastState State;

        /// <summary>"Found through Kinship at &lt;site&gt;" once owned that way; the roster's silhouette hint otherwise.</summary>
        public string Hint;
    }

    /// <summary>One lore entry of the compendium screen: its title and text once found, a locked placeholder otherwise.</summary>
    public sealed class CompendiumLoreRow
    {
        public string LoreId;
        public string RegionId;
        public string Title;
        public string Text;
        public bool Found;
    }

    /// <summary>
    /// The compendium screen (the Collector persona, docs/design/compendium-achievements.md): every
    /// roster species (unknown, offered by a live Kinship choice, or owned — and, through Kinship, at
    /// which site), every lore entry (found: its title and text; not found: a locked placeholder) and
    /// the combined completion percent (<see cref="CompendiumRules.Completion"/>). A pure read over the
    /// save through <see cref="CompendiumRules"/>: nothing here is stored, and refreshing re-reads it.
    /// </summary>
    public sealed class CompendiumViewModel
    {
        /// <summary>An unfound lore entry's title placeholder.</summary>
        public const string LockedTitle = "???";

        /// <summary>An unfound lore entry's text placeholder, as a text key.</summary>
        public const string LockedTextKey = "ui.compendium.locked_lore";

        private readonly GameSession _session;

        public CompendiumViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<CompendiumBeastRow> Beasts { get; } = new List<CompendiumBeastRow>();

        public List<CompendiumLoreRow> Lore { get; } = new List<CompendiumLoreRow>();

        public CompendiumCompletion Completion { get; private set; }

        /// <summary>Re-reads the save (a Kinship choice was made, a lore stone found, a region completed).</summary>
        public void Refresh()
        {
            Beasts.Clear();
            Lore.Clear();
            DiscoveryContent content = _session.Content.Discovery;
            foreach (CompendiumBeastEntry entry in CompendiumRules.BeastEntries(_session.Save, content))
            {
                CreatureSpeciesSO species = _session.Content.Battle.GetSpecies(entry.SpeciesId);
                if (species == null)
                {
                    continue;
                }

                bool known = entry.State != CompendiumBeastState.Unknown;
                Beasts.Add(new CompendiumBeastRow
                {
                    SpeciesId = entry.SpeciesId,
                    Name = known ? species.DisplayName ?? entry.SpeciesId : "???",
                    ArtKey = species.ArtKey,
                    Element = RosterViewModel.PrimaryElement(species),
                    Stance = species.Stance,
                    State = entry.State,
                    Hint = HintFor(_session, entry)
                });
            }

            foreach (CompendiumLoreEntry entry in CompendiumRules.LoreEntries(_session.Save, content))
            {
                Lore.Add(new CompendiumLoreRow
                {
                    LoreId = entry.LoreId,
                    RegionId = entry.RegionId,
                    Title = entry.Found ? entry.Title : LockedTitle,
                    Text = entry.Found ? entry.Text : _session.Content.Text.Get(LockedTextKey),
                    Found = entry.Found
                });
            }

            Completion = CompendiumRules.Completion(_session.Save, content);
        }

        private static string HintFor(GameSession session, CompendiumBeastEntry entry)
        {
            switch (entry.State)
            {
                case CompendiumBeastState.Offered:
                    return session.Content.Text.Get("ui.compendium.offered");
                case CompendiumBeastState.Owned:
                    if (!entry.JoinedThroughKinship)
                    {
                        return string.Empty;
                    }

                    KinshipSiteData site = session.Content.Discovery.Library.Site(entry.KinshipSiteId);
                    return session.Content.Text.Format("ui.compendium.found_through", site?.Name ?? session.Content.Text.Get("ui.compendium.a_kinship_stone"));
                default:
                    return session.Content.Text.Get(RosterViewModel.SilhouetteHintKey);
            }
        }
    }
}
