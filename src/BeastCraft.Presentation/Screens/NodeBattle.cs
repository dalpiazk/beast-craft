using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Session;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// One campaign battle at a map location, from the pick to the pay-out, through the campaign's
    /// own rules: the node's encounter (<see cref="CampaignRules.PlanFor"/>) fought in the node's
    /// region (its backdrops and obstacles) with the attempt's seed
    /// (<see cref="CampaignRules.BattleSeed"/> of the losses there so far), begun through
    /// <see cref="BattleSession.Begin"/> (autosaved: the node was entered and its consumable
    /// spent), then <see cref="Complete"/>: <see cref="BattleSession.ApplyRewards"/> under the seals'
    /// cap with the node's reward modifiers and the economy's gear and looks,
    /// <see cref="CampaignRules.ResolveBattle"/> (a win clears the node; a loss counts toward the
    /// retry rules), an autosave, and the consolidated <see cref="ResultsViewModel"/>.
    /// </summary>
    public sealed class NodeBattle
    {
        private readonly GameSession _session;
        private BattleSessionRun _run;
        private bool _completed;

        private NodeBattle(GameSession session, MapNode node, EncounterPlan plan, string regionId, int attempt)
        {
            _session = session;
            Node = node;
            Plan = plan;
            RegionId = regionId;
            Attempt = attempt;
            Seed = CampaignRules.BattleSeed(node, attempt);
            Layout = BattleLayouts.Pick(session.Content.Battle.Layouts, regionId, plan.Arena, Seed);
        }

        public MapNode Node { get; }

        public EncounterPlan Plan { get; }

        /// <summary>The region the battle is fought in (its backdrops and obstacles).</summary>
        public string RegionId { get; }

        /// <summary>Losses at this location so far: this fight's attempt number (0 = the first).</summary>
        public int Attempt { get; }

        /// <summary>The battle's seed (<see cref="CampaignRules.BattleSeed"/>).</summary>
        public int Seed { get; }

        /// <summary>The battlefield the battle will stand on (the same seeded pick the session makes), or null for the open board.</summary>
        public BattleLayoutEntryData Layout { get; }

        /// <summary>The setup begun, once <see cref="Begin"/> succeeded.</summary>
        public BattleSetup Setup { get; private set; }

        /// <summary>Battle unit id to species or enemy id (for the sprites), once begun.</summary>
        public Dictionary<string, string> SpeciesByUnit { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public BattleSessionRun Run
        {
            get { return _run; }
        }

        /// <summary>The battle at <paramref name="nodeId"/> of the expedition in progress; null with <paramref name="error"/> when it cannot be fought now.</summary>
        public static NodeBattle For(GameSession session, int nodeId, out string error)
        {
            error = null;
            MapRun run = session?.Save?.Campaign?.ActiveRun;
            MapNode node = run?.Find(nodeId);
            if (node == null || !session.Save.Campaign.HasActiveRun)
            {
                error = "No such location on the map.";
                return null;
            }

            if (!node.IsBattle)
            {
                error = "Nothing to fight at a " + MapViewModel.KindLabel(node.Type) + ".";
                return null;
            }

            if (!CampaignRules.CanEnter(run, nodeId))
            {
                error = "That location cannot be reached from here.";
                return null;
            }

            EncounterPlan plan = CampaignRules.PlanFor(node, session.Content.Encounters, session.Content.Enemies);
            if (plan == null)
            {
                error = "The encounter could not be built.";
                return null;
            }

            return new NodeBattle(session, node, plan, run.RegionId, CampaignRules.LossesAt(run, nodeId));
        }

        /// <summary>
        /// Begins the battle with <paramref name="team"/> (beast ids, deployment order) and
        /// <paramref name="consumableId"/> (null for none), and autosaves. Null with
        /// <paramref name="error"/> when the session refuses the setup (nothing is spent then).
        /// </summary>
        public BattleSessionRun Begin(IReadOnlyList<string> team, string consumableId, out string error)
        {
            error = null;
            if (_run != null)
            {
                return _run;
            }

            BattleSetup setup = new BattleSetup
            {
                Save = _session.Save,
                Content = _session.Content.Battle,
                Seed = Seed,
                // The Beastbinder fights beside the team, off the board, as the difficulty was calibrated.
                IncludeAvatar = true,
                AvatarProfile = CampaignAvatar.Profile(_session.Content),
                Encounter = Plan.ToSetup(RegionId),
                TeamBeastIds = new List<string>(team ?? new string[0])
            };
            if (!string.IsNullOrEmpty(consumableId))
            {
                setup.Consumables.Add(consumableId);
            }

            BattleSessionRun run = BattleSession.Begin(setup);
            if (run.Battle == null)
            {
                error = run.Result.Error;
                return null;
            }

            Setup = setup;
            _run = run;
            SpeciesByUnit.Clear();
            foreach (string beastId in setup.TeamBeastIds)
            {
                OwnedBeast beast = _session.Save.FindBeast(beastId);
                SpeciesByUnit[BattleSession.BeastUnitIdPrefix + beastId] = beast?.Progress?.SpeciesId;
            }

            for (int i = 0; i < setup.Encounter.Enemies.Count; i++)
            {
                EnemySpec spec = setup.Encounter.Enemies[i];
                string unitId = string.IsNullOrEmpty(spec.UnitId) ? "enemy" + (i + 1).ToString(CultureInfo.InvariantCulture) : spec.UnitId;
                SpeciesByUnit[unitId] = spec.SpeciesId;
            }

            _session.LastTeam.Clear();
            _session.LastTeam.AddRange(setup.TeamBeastIds);
            _session.Autosave(AutosaveReason.NodeEntry);
            return run;
        }

        /// <summary>Whether <see cref="Complete"/> has run (the battle is paid out).</summary>
        public bool IsCompleted
        {
            get { return _completed; }
        }

        /// <summary>
        /// Claims the battle screen's one hand-back to the results: true the first time (after the
        /// battle was begun), false ever after — so Back, Continue and Enter racing, or a re-entrant
        /// call from the results callback, hand it back once.
        /// </summary>
        public bool TryHandBack()
        {
            if (_run == null || _handedBack)
            {
                return false;
            }

            _handedBack = true;
            return true;
        }

        private bool _handedBack;

        /// <summary>
        /// Pays the finished battle out and records it on the map (see the class remarks), autosaves,
        /// and returns the results. The battle is played out first if it is not over. Once only.
        /// </summary>
        public ResultsViewModel Complete()
        {
            if (_run == null)
            {
                throw new InvalidOperationException("The battle was never begun.");
            }

            if (_completed)
            {
                throw new InvalidOperationException("The battle was already completed.");
            }

            _completed = true;
            BattleSessionResult result = _run.Finish();
            PlayerSave save = _session.Save;
            GameContent content = _session.Content;

            Dictionary<string, (int Level, int Xp)> before = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            foreach (OwnedBeast beast in save.Beasts)
            {
                before[beast.BeastId] = (beast.Progress.Level, beast.Progress.Xp);
            }

            int cap = CampaignRules.BeastCap(save, content.Campaign);
            RewardModifiers modifiers = CampaignRules.RewardModifiersFor(Node).With(content.Economy);
            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, content.Battle, content.Drops, cap, modifiers);
            CampaignResult campaign = CampaignRules.ResolveBattle(save, content.Campaign, Node.NodeId, result.Outcome, content.Economy);
            _session.Autosave(AutosaveReason.Results);
            return ResultsViewModel.Build(_session, this, result, summary, campaign, before);
        }
    }
}
