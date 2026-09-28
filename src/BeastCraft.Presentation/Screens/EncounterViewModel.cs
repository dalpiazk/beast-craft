using System;
using System.Collections.Generic;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Session;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One group of the enemy line-up as the preview lists it.</summary>
    public sealed class EnemyGroupView
    {
        public string Name;
        public string EnemyId;
        public Element Element;
        public CombatStance? Stance;
        public int Count;
        public int Level;

        /// <summary>The sprite to draw it with in this region (its per-region art), or null.</summary>
        public string ArtKey;
    }

    /// <summary>One owned beast in the party picker.</summary>
    public sealed class PartyMemberView
    {
        public string BeastId;
        public string SpeciesId;
        public string Name;
        public int Level;
        public Element Element;
        public CombatStance Stance;
        public string ArtKey;

        /// <summary>Its place in the party (0-based deployment order), or −1 when not picked.</summary>
        public int PartyIndex = -1;

        public bool Selected
        {
            get { return PartyIndex >= 0; }
        }
    }

    /// <summary>One held consumable in the picker.</summary>
    public sealed class ConsumableView
    {
        public string ConsumableId;
        public string Name;
        public string Description;
        public int Quantity;
        public bool Selected;
    }

    /// <summary>The dismissible team-suggestion banner (after repeated losses at the location).</summary>
    public sealed class SuggestionView
    {
        public int Losses;
        public List<string> BeastIds = new List<string>();
        public List<string> Names = new List<string>();
    }

    /// <summary>Whether the picked team can start, and why not.</summary>
    public sealed class TeamValidation
    {
        public bool Ok;
        public string Message;
    }

    /// <summary>
    /// The encounter screen: the full preview of the fight — free, always (producer decision) —
    /// and the team setup. The enemies (type, element, stance, level, how many), the arena and the
    /// battlefield it will be fought on; the party picker over the owned beasts (up to
    /// <see cref="GameSession.PartySize"/>, in deployment order, with stances and elements); one
    /// consumable at most; after repeated losses there, the team suggestion as a dismissible
    /// banner (respecting the settings toggle, <see cref="CampaignRules.SuggestionFor"/>); and
    /// <see cref="Start"/>.
    /// </summary>
    public sealed class EncounterViewModel
    {
        private readonly GameSession _session;
        private readonly List<string> _team = new List<string>();
        private string _consumable;

        public EncounterViewModel(GameSession session, int nodeId) : this(session, NodeBattle.For(session, nodeId, out string error), error, nodeId)
        {
        }

        /// <summary>
        /// The preview of the Kinship trial at point of interest <paramref name="poiId"/>
        /// (<see cref="NodeBattle.ForKinship"/>): the same page, titled by the site, with the site's
        /// words and its optional bond condition as a banner, no team suggestion.
        /// </summary>
        public static EncounterViewModel ForKinship(GameSession session, string poiId)
        {
            return new EncounterViewModel(session, NodeBattle.ForKinship(session, poiId, out string error), error, -1);
        }

        private EncounterViewModel(GameSession session, NodeBattle battle, string error, int nodeId)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Battle = battle;
            Error = error;
            NodeId = nodeId;
            if (Battle == null)
            {
                return;
            }

            GameContent content = session.Content;
            MapNode node = Battle.Node;
            Title = session.LocationName(node);
            KindLabel = MapViewModel.KindLabel(node.Type);
            if (Battle.IsKinshipTrial)
            {
                Discovery.KinshipSiteData site = Discovery.KinshipRules.SiteOf(content.Discovery, Battle.Trial);
                Title = site?.Name ?? "Kinship trial";
                KindLabel = "Kinship trial";
                Banner = site?.Intro;
                BondText = site?.BondText;
            }
            Level = Battle.Plan.Level;
            Arena = Battle.Plan.Arena.ToString();
            Attempt = Battle.Attempt;
            if (Battle.Layout != null)
            {
                BackdropArtKey = Battle.Layout.ArtKey;
                LayoutName = LayoutNameOf(Battle.Layout.ArtKey);
                Obstacles = Battle.Layout.Cells?.Length ?? 0;
            }

            EncounterPreview preview = Battle.Plan.Preview(ScoutingDetail.Full);
            DominantElement = preview.DominantElement;
            foreach (EncounterPreviewGroup group in preview.Groups)
            {
                EncounterLineupEnemy match = null;
                foreach (EncounterLineupEnemy enemy in Battle.Plan.Enemies)
                {
                    if (enemy.DisplayName == group.DisplayName && enemy.Element == group.Element)
                    {
                        match = enemy;
                        break;
                    }
                }

                Enemies.Add(new EnemyGroupView
                {
                    Name = group.DisplayName,
                    EnemyId = match?.EnemyId,
                    Element = group.Element,
                    Stance = group.Stance,
                    Count = group.Count,
                    Level = Battle.Plan.Level,
                    ArtKey = match == null ? null : DemoBattle.ArtKeyOf(content, match.EnemyId, Battle.ArtRegionId)
                });
            }

            foreach (OwnedBeast beast in session.Save.Beasts)
            {
                CreatureSpeciesSO species = content.Battle.GetSpecies(beast.Progress.SpeciesId);
                if (species == null)
                {
                    continue;
                }

                Owned.Add(new PartyMemberView
                {
                    BeastId = beast.BeastId,
                    SpeciesId = species.SpeciesId,
                    Name = species.DisplayName ?? species.SpeciesId,
                    Level = beast.Progress.Level,
                    Element = species.Elements != null && species.Elements.Length > 0 ? species.Elements[0] : Element.None,
                    Stance = species.Stance,
                    ArtKey = species.ArtKey
                });
            }

            List<string> start = session.LastTeam.FindAll(id => Owned.Exists(m => m.BeastId == id));
            if (start.Count == 0)
            {
                foreach (PartyMemberView member in Owned)
                {
                    start.Add(member.BeastId);
                }
            }

            for (int i = 0; i < start.Count && _team.Count < PartySize; i++)
            {
                _team.Add(start[i]);
            }

            foreach (ConsumableStack stack in session.Save.Consumables)
            {
                ConsumableSO consumable = stack == null || stack.Quantity <= 0 ? null : content.Battle.GetConsumable(stack.ConsumableId);
                if (consumable != null)
                {
                    Consumables.Add(new ConsumableView
                    {
                        ConsumableId = consumable.ConsumableId,
                        Name = consumable.DisplayName ?? consumable.ConsumableId,
                        Description = consumable.Description,
                        Quantity = stack.Quantity
                    });
                }
            }

            CampaignTeamSuggestion suggestion = Battle.IsKinshipTrial ? null
                                                : CampaignRules.SuggestionFor(session.Save, nodeId, session.Settings, content.Encounters, content.Battle, content.Campaign, PartySize);
            if (suggestion != null && !session.DismissedSuggestions.Contains(SuggestionKey()))
            {
                Suggestion = new SuggestionView { Losses = suggestion.Losses };
                foreach (string beastId in suggestion.BeastIds)
                {
                    Suggestion.BeastIds.Add(beastId);
                    Suggestion.Names.Add(session.BeastName(session.Save.FindBeast(beastId)));
                }
            }

            Refresh();
        }

        public int PartySize
        {
            get { return GameSession.PartySize; }
        }

        /// <summary>Set when the location cannot be fought now (then nothing else is).</summary>
        public string Error { get; }

        public NodeBattle Battle { get; }

        public int NodeId { get; }

        public string Title { get; }

        public string KindLabel { get; }

        /// <summary>A Kinship trial's words (the site's intro), shown as a banner; null for a map location.</summary>
        public string Banner { get; }

        /// <summary>A Kinship trial's optional bond condition in words (flavour), or null.</summary>
        public string BondText { get; }

        public int Level { get; }

        public string Arena { get; }

        /// <summary>Losses here so far.</summary>
        public int Attempt { get; }

        /// <summary>The battlefield's painted backdrop (its art key) and its name, or null on the open board.</summary>
        public string BackdropArtKey { get; }

        public string LayoutName { get; }

        public int Obstacles { get; }

        public Element? DominantElement { get; }

        public List<EnemyGroupView> Enemies { get; } = new List<EnemyGroupView>();

        public List<PartyMemberView> Owned { get; } = new List<PartyMemberView>();

        public List<ConsumableView> Consumables { get; } = new List<ConsumableView>();

        /// <summary>The picked team in deployment order.</summary>
        public IReadOnlyList<string> Team
        {
            get { return _team; }
        }

        public string SelectedConsumable
        {
            get { return _consumable; }
        }

        /// <summary>The suggestion banner, or null (none offered, the setting is off, or it was dismissed).</summary>
        public SuggestionView Suggestion { get; private set; }

        /// <summary>
        /// Adds a beast to the party (at the end) or takes it out. Refused (false, with
        /// <paramref name="message"/>) when the party is full or the beast is not owned.
        /// </summary>
        public bool ToggleMember(string beastId, out string message)
        {
            message = null;
            if (!Owned.Exists(m => m.BeastId == beastId))
            {
                message = "That beast is not in your collection.";
                return false;
            }

            if (_team.Remove(beastId))
            {
                Refresh();
                return true;
            }

            if (_team.Count >= PartySize)
            {
                message = "Your party is full (" + PartySize + "). Tap a member to take them out first.";
                return false;
            }

            _team.Add(beastId);
            Refresh();
            return true;
        }

        /// <summary>Picks <paramref name="consumableId"/> (replacing any other: one per battle), or clears it when it is already picked or null.</summary>
        public void ToggleConsumable(string consumableId)
        {
            _consumable = consumableId == null || consumableId == _consumable || !Consumables.Exists(c => c.ConsumableId == consumableId) ? null : consumableId;
            Refresh();
        }

        /// <summary>Takes the suggested team (and hides the banner).</summary>
        public void ApplySuggestion()
        {
            if (Suggestion == null)
            {
                return;
            }

            _team.Clear();
            foreach (string beastId in Suggestion.BeastIds)
            {
                if (_team.Count < PartySize && Owned.Exists(m => m.BeastId == beastId))
                {
                    _team.Add(beastId);
                }
            }

            DismissSuggestion();
        }

        /// <summary>Hides the banner for this location for the rest of the session.</summary>
        public void DismissSuggestion()
        {
            if (Suggestion == null)
            {
                return;
            }

            _session.DismissedSuggestions.Add(SuggestionKey());
            Suggestion = null;
            Refresh();
        }

        public TeamValidation Validate()
        {
            if (Battle == null)
            {
                return new TeamValidation { Ok = false, Message = Error };
            }

            if (_team.Count == 0)
            {
                return new TeamValidation { Ok = false, Message = "Pick at least one beast." };
            }

            if (_team.Count > PartySize)
            {
                return new TeamValidation { Ok = false, Message = "At most " + PartySize + " beasts can fight." };
            }

            return new TeamValidation { Ok = true };
        }

        public bool CanStart
        {
            get { return Validate().Ok; }
        }

        /// <summary>Begins the battle (autosaving the node entry); null with <paramref name="error"/> when refused.</summary>
        public NodeBattle Start(out string error)
        {
            TeamValidation validation = Validate();
            if (!validation.Ok)
            {
                error = validation.Message;
                return null;
            }

            return Battle.Begin(_team, _consumable, out error) == null ? null : Battle;
        }

        /// <summary>A layout's display name from its backdrop art key (<c>backdrop/r01/sun0/medium</c> → "Sun 1": numbered from 1).</summary>
        public static string LayoutNameOf(string artKey)
        {
            string[] parts = (artKey ?? string.Empty).Split('/');
            string id = parts.Length >= 3 ? parts[2] : artKey ?? string.Empty;
            string letters = id.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            string digits = id.Substring(letters.Length);
            letters = letters.Replace('_', ' ').Trim();
            if (letters.Length > 0)
            {
                letters = char.ToUpperInvariant(letters[0]) + letters.Substring(1);
            }

            return digits.Length == 0 ? letters : (letters + " " + (int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) + 1)).Trim();
        }

        private string SuggestionKey()
        {
            MapRun run = _session.Save.Campaign.ActiveRun;
            return run.RegionId + "/" + run.Stage + "/" + run.Seed + "/" + NodeId;
        }

        private void Refresh()
        {
            foreach (PartyMemberView member in Owned)
            {
                member.PartyIndex = _team.IndexOf(member.BeastId);
            }

            foreach (ConsumableView consumable in Consumables)
            {
                consumable.Selected = consumable.ConsumableId == _consumable;
            }
        }
    }
}
