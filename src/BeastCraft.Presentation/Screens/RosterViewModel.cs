using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>How the roster grid is ordered.</summary>
    public enum RosterSort
    {
        /// <summary>The order the beasts joined.</summary>
        Joined = 0,
        Level = 1,
        Name = 2,
        Element = 3,
        Stance = 4
    }

    /// <summary>One card of the roster grid: an owned beast, or the silhouette of a species not yet found.</summary>
    public sealed class RosterEntry
    {
        /// <summary>The owned beast's id; null for a silhouette.</summary>
        public string BeastId;

        public string SpeciesId;

        /// <summary>The species' name, or "???" for a silhouette.</summary>
        public string Name;

        public int Level;
        public Element Element;
        public CombatStance Stance;
        public string ArtKey;

        /// <summary>In the last party picked (the one Next battle fields).</summary>
        public bool InParty;

        /// <summary>A species the player has not found yet (drawn as a silhouette).</summary>
        public bool Silhouette;

        /// <summary>The silhouette's hint (where it will come from).</summary>
        public string Hint;

        /// <summary>The joined order (for <see cref="RosterSort.Joined"/>).</summary>
        public int Order;
    }

    /// <summary>
    /// The Roster tab: every owned beast as an illustrated card (level, stance, element), sortable,
    /// then a silhouette for each species not yet found ("found through Kinship"), which will feed
    /// the compendium. Tapping an owned beast opens its detail screen.
    /// </summary>
    public sealed class RosterViewModel
    {
        public const string SilhouetteHint = "Found through Kinship";

        public static readonly string[] SortNames = { "Joined", "Level", "Name", "Element", "Stance" };

        private readonly GameSession _session;

        public RosterViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public RosterSort Sort { get; private set; } = RosterSort.Joined;

        /// <summary>The owned beasts, in <see cref="Sort"/> order.</summary>
        public List<RosterEntry> Owned { get; } = new List<RosterEntry>();

        /// <summary>The species not yet found, in roster order.</summary>
        public List<RosterEntry> Silhouettes { get; } = new List<RosterEntry>();

        public string SortName
        {
            get { return SortNames[(int)Sort]; }
        }

        /// <summary>Orders the grid by <paramref name="sort"/>.</summary>
        public void SortBy(RosterSort sort)
        {
            if (Enum.IsDefined(typeof(RosterSort), sort))
            {
                Sort = sort;
                Apply();
            }
        }

        /// <summary>The next sort in <see cref="SortNames"/> order (the sort button cycles).</summary>
        public void NextSort()
        {
            SortBy((RosterSort)(((int)Sort + 1) % SortNames.Length));
        }

        /// <summary>Re-reads the save (after a detail screen changed something).</summary>
        public void Refresh()
        {
            Owned.Clear();
            Silhouettes.Clear();
            PlayerSave save = _session.Save;
            HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
            List<string> party = _session.Party();
            for (int i = 0; save != null && i < save.Beasts.Count; i++)
            {
                OwnedBeast beast = save.Beasts[i];
                CreatureSpeciesSO species = beast?.Progress == null ? null : _session.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                if (species == null)
                {
                    continue;
                }

                found.Add(species.SpeciesId);
                Owned.Add(new RosterEntry
                {
                    BeastId = beast.BeastId,
                    SpeciesId = species.SpeciesId,
                    Name = species.DisplayName ?? species.SpeciesId,
                    Level = beast.Progress.Level,
                    Element = PrimaryElement(species),
                    Stance = species.Stance,
                    ArtKey = species.ArtKey,
                    InParty = party.Contains(beast.BeastId),
                    Order = i
                });
            }

            foreach (CreatureSpeciesSO species in _session.Content.Species)
            {
                if (species == null || found.Contains(species.SpeciesId))
                {
                    continue;
                }

                Silhouettes.Add(new RosterEntry
                {
                    SpeciesId = species.SpeciesId,
                    Name = "???",
                    ArtKey = species.ArtKey,
                    Element = PrimaryElement(species),
                    Stance = species.Stance,
                    Silhouette = true,
                    Hint = SilhouetteHint,
                    Order = Silhouettes.Count
                });
            }

            Apply();
        }

        private void Apply()
        {
            Comparison<RosterEntry> compare;
            switch (Sort)
            {
                case RosterSort.Level:
                    compare = (a, b) => b.Level != a.Level ? b.Level.CompareTo(a.Level) : a.Order.CompareTo(b.Order);
                    break;
                case RosterSort.Name:
                    compare = (a, b) =>
                    {
                        int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                        return byName != 0 ? byName : a.Order.CompareTo(b.Order);
                    };
                    break;
                case RosterSort.Element:
                    compare = (a, b) => a.Element != b.Element ? a.Element.CompareTo(b.Element) : a.Order.CompareTo(b.Order);
                    break;
                case RosterSort.Stance:
                    compare = (a, b) => a.Stance != b.Stance ? a.Stance.CompareTo(b.Stance) : b.Level != a.Level ? b.Level.CompareTo(a.Level) : a.Order.CompareTo(b.Order);
                    break;
                default:
                    compare = (a, b) => a.Order.CompareTo(b.Order);
                    break;
            }

            Owned.Sort(compare);
        }

        /// <summary>A species' first element (<see cref="Element.None"/> when it has none).</summary>
        public static Element PrimaryElement(CreatureSpeciesSO species)
        {
            return species?.Elements != null && species.Elements.Length > 0 ? species.Elements[0] : Element.None;
        }
    }
}
