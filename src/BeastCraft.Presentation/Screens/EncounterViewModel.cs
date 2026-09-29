using System;
using System.Collections.Generic;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Garden;
using BeastCraft.Grove;
using BeastCraft.Localization;
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

    /// <summary>One soothing item the player holds for this region (docs/design/grove.md, "Peaceful clears").</summary>
    public sealed class SoothingOptionView
    {
        public string ItemId;
        public string DisplayName;
        public int Held;
    }

    /// <summary>What <see cref="EncounterViewModel.Soothe"/> did.</summary>
    public sealed class SootheOutcome
    {
        public bool Success { get; private set; }

        public string Message { get; private set; }

        internal static SootheOutcome Succeeded(string message)
        {
            return new SootheOutcome { Success = true, Message = message };
        }

        internal static SootheOutcome Refused(string message)
        {
            return new SootheOutcome { Success = false, Message = message };
        }
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

        /// <summary>The text table (<c>ui.encounter.*</c>).</summary>
        private StringTable Text
        {
            get { return _session.Content.Text; }
        }

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
            KindLabel = MapViewModel.KindLabel(node.Type, content.Text);
            if (Battle.IsKinshipTrial)
            {
                Discovery.KinshipSiteData site = Discovery.KinshipRules.SiteOf(content.Discovery, Battle.Trial);
                Title = site?.Name ?? content.Text.Get("ui.results.kinship_trial");
                KindLabel = content.Text.Get("ui.results.kinship_trial");
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

        /// <summary>
        /// "The wilds ease a little (-x%)" while adaptive assist is active at this location
        /// (<see cref="Encounters.EncounterPlan.AssistScale"/> below 1, producer decision "assist +
        /// guidance"; a consecutive-loss discount, <see cref="Campaign.RegionLibrary.AssistScaleFor"/>);
        /// null when it is not (a fresh location, post-game Hard, or Hearthglen, none of which are
        /// ever assisted — Hearthglen has its own catch-up instead).
        /// </summary>
        public string AssistNote
        {
            get
            {
                if (Battle == null || !(Battle.Plan.AssistScale < 1.0))
                {
                    return null;
                }

                int percent = (int)Math.Round((1.0 - Battle.Plan.AssistScale) * 100.0);
                return Text.Format("ui.encounter.assist", percent);
            }
        }

        public int Level { get; }

        public string Arena { get; }

        /// <summary>Losses here so far.</summary>
        public int Attempt { get; }

        /// <summary>The battlefield's painted backdrop (its art key) and its name, or null on the open board.</summary>
        public string BackdropArtKey { get; }

        public string LayoutName { get; }

        public int Obstacles { get; }

        public Element? DominantElement { get; }

        /// <summary>
        /// Whether this location can be soothed instead of fought (docs/design/grove.md, "Peaceful
        /// clears" — D3/D4): an ordinary <see cref="MapNodeType.Battle"/> location whose region has a
        /// soothing item set authored (<see cref="Grove.GroveLibrary.Soothing"/>) — never an Elite den,
        /// a Gate, a Boss, a Kinship trial or a Hearthglen fight (a Kinship trial has no
        /// <see cref="Node"/> at all; Hearthglen's region authors no soothing set, so it is excluded the
        /// same way <see cref="Campaign.CampaignRules.Soothe"/> itself refuses it).
        /// </summary>
        public bool CanSoothe
        {
            get
            {
                return Battle != null && !Battle.IsKinshipTrial && Battle.Node.Type == MapNodeType.Battle &&
                       _session.Content.GroveLibrary.Soothing(Battle.RegionId) != null;
            }
        }

        /// <summary>The region's soothing items the player holds (empty when <see cref="CanSoothe"/> is false, or none are held).</summary>
        public List<SoothingOptionView> SoothingOptions
        {
            get
            {
                List<SoothingOptionView> options = new List<SoothingOptionView>();
                if (!CanSoothe)
                {
                    return options;
                }

                GardenLibrary garden = _session.Content.GardenLibrary;
                SoothingRegionData set = _session.Content.GroveLibrary.Soothing(Battle.RegionId);
                foreach (string itemId in set.ItemIds ?? new string[0])
                {
                    int held = _session.Save.Grove.Items.GetCount(itemId);
                    if (held > 0)
                    {
                        options.Add(new SoothingOptionView { ItemId = itemId, DisplayName = garden.Variety(itemId)?.DisplayName ?? itemId, Held = held });
                    }
                }

                return options;
            }
        }

        /// <summary>
        /// Soothes this location with <paramref name="itemId"/> instead of fighting it
        /// (<see cref="Campaign.CampaignRules.Soothe"/>, with the current <see cref="Team"/>): full
        /// rewards, the node clears, autosaved. Refused (no state changed) when <see cref="CanSoothe"/>
        /// is false, the item is not held, or the team is empty.
        /// </summary>
        public SootheOutcome Soothe(string itemId)
        {
            if (!CanSoothe)
            {
                return SootheOutcome.Refused(Text.Get("ui.encounter.cannot_soothe"));
            }

            GameContent content = _session.Content;
            CampaignResult result = CampaignRules.Soothe(_session.Save, content.Campaign, content.GroveLibrary, content.Encounters, content.Enemies, NodeId, itemId, Team,
                                                          content.Drops, content.Economy, out Session.BattleRewardSummary rewards, content.Achievements);
            if (!result.Success)
            {
                return SootheOutcome.Refused(result.Error);
            }

            _session.Autosave(AutosaveReason.Results);
            return SootheOutcome.Succeeded(SootheSummary(rewards, result));
        }

        private string SootheSummary(Session.BattleRewardSummary rewards, CampaignResult result)
        {
            List<string> parts = new List<string>();
            if (rewards.GoldGained > 0)
            {
                parts.Add(Text.Format("ui.encounter.reward_gold", rewards.GoldGained));
            }

            if (rewards.BeastLevelsGained > 0)
            {
                parts.Add(Text.Format(rewards.BeastLevelsGained == 1 ? "ui.encounter.reward_beast_level" : "ui.encounter.reward_beast_levels", rewards.BeastLevelsGained));
            }

            if (rewards.AvatarLevelsGained > 0)
            {
                parts.Add(rewards.AvatarLevelsGained == 1 ? Text.Get("ui.encounter.reward_avatar_level") : Text.Format("ui.encounter.reward_avatar_levels", rewards.AvatarLevelsGained));
            }

            if (rewards.Loot.Drops.Count > 0)
            {
                parts.Add(Text.Format(rewards.Loot.Drops.Count == 1 ? "ui.encounter.reward_material" : "ui.encounter.reward_materials", rewards.Loot.Drops.Count));
            }

            if (rewards.GearGained.Count > 0)
            {
                parts.Add(Text.Format("ui.encounter.reward_gear", rewards.GearGained.Count));
            }

            string extra = GameSession.ExtraRewardText(Text, result.TitlesEarned, 0);
            return Text.Format("ui.encounter.soothed", parts.Count > 0 ? Text.Format("ui.encounter.soothed_parts", string.Join(Text.Get("ui.common.list_sep"), parts)) : Text.Get("ui.encounter.soothed_full")) + extra;
        }

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
        /// The ALWAYS-shown matchup warnings for the picked <see cref="Team"/> against this encounter
        /// (<see cref="MatchupWarnings.For"/>, producer decision "assist + guidance"): no Vanguard, no
        /// Skirmisher, an element disadvantage, under-levelled by N. Recomputed from the current pick
        /// every read (cheap: at most <see cref="PartySize"/> beasts), so it always reflects
        /// <see cref="ToggleMember"/>. Empty with no team or on an error (<see cref="Battle"/> null).
        /// </summary>
        public List<string> Warnings
        {
            get
            {
                if (Battle == null)
                {
                    return new List<string>();
                }

                List<CreatureSpeciesSO> team = new List<CreatureSpeciesSO>();
                int levelSum = 0;
                foreach (string beastId in _team)
                {
                    OwnedBeast beast = _session.Save.FindBeast(beastId);
                    CreatureSpeciesSO species = beast == null ? null : _session.Content.Battle.GetSpecies(beast.Progress.SpeciesId);
                    if (species != null)
                    {
                        team.Add(species);
                        levelSum += beast.Progress.Level;
                    }
                }

                int teamLevel = team.Count > 0 ? levelSum / team.Count : 0;
                return MatchupWarnings.For(Battle.Plan.Preview(ScoutingDetail.Full), team, teamLevel, Battle.Plan.Level);
            }
        }

        /// <summary>
        /// Adds a beast to the party (at the end) or takes it out. Refused (false, with
        /// <paramref name="message"/>) when the party is full or the beast is not owned.
        /// </summary>
        public bool ToggleMember(string beastId, out string message)
        {
            message = null;
            if (!Owned.Exists(m => m.BeastId == beastId))
            {
                message = Text.Get("ui.encounter.not_in_collection");
                return false;
            }

            if (_team.Remove(beastId))
            {
                Refresh();
                return true;
            }

            if (_team.Count >= PartySize)
            {
                message = Text.Format("ui.encounter.party_full", PartySize);
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
                return new TeamValidation { Ok = false, Message = Text.Get("ui.encounter.pick_one") };
            }

            if (_team.Count > PartySize)
            {
                return new TeamValidation { Ok = false, Message = Text.Format("ui.encounter.at_most", PartySize) };
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
