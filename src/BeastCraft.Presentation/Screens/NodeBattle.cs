using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;
using BeastCraft.Session;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// One campaign battle at a map location, from the pick to the pay-out, through the campaign's
    /// own rules: the node's encounter (<see cref="CampaignRules.PlanFor(MapRun, MapNode, EncounterLibrary, EnemyCatalog, RegionLibrary)"/>,
    /// with the early regions' easing) fought in the node's
    /// region (its backdrops and obstacles) with the attempt's seed
    /// (<see cref="CampaignRules.BattleSeed"/> of the losses there so far), begun through
    /// <see cref="BattleSession.Begin"/> (autosaved: the node was entered and its consumable
    /// spent), then <see cref="Complete"/>: <see cref="BattleSession.ApplyRewards"/> under the seals'
    /// cap with the node's reward modifiers and the economy's gear and looks,
    /// <see cref="CampaignRules.ResolveBattle"/> (a win clears the node; a loss counts toward the
    /// retry rules), an autosave, and the consolidated <see cref="ResultsViewModel"/>.
    /// <para>
    /// A <strong>Kinship trial</strong> (<see cref="ForKinship"/>) is fought the same way at a point of
    /// interest instead of a location: its site's template at the point's level
    /// (<see cref="KinshipRules.PlanFor"/>, never eased), on the region's battlefields, with the trial's
    /// retry seed (<see cref="KinshipRules.BattleSeed"/>). Its <see cref="Complete"/> pays nothing (a
    /// trial is not a clear: no XP, gold or loot; a consumable taken is spent) and reports the outcome
    /// to <see cref="KinshipRules.ResolveTrial"/>: a win leaves the choice of beasts pending.
    /// </para>
    /// </summary>
    public sealed class NodeBattle
    {
        private readonly GameSession _session;
        private BattleSessionRun _run;
        private bool _completed;

        private NodeBattle(GameSession session, MapNode node, EncounterPlan plan, string regionId, string artRegionId, int attempt, PointOfInterest trial = null)
        {
            Trial = trial;
            _session = session;
            Node = node;
            Plan = plan;
            RegionId = regionId;
            ArtRegionId = artRegionId;
            Attempt = attempt;
            Seed = CampaignRules.BattleSeed(node, attempt);
            Layout = BattleLayouts.Pick(session.Content.Battle.Layouts, regionId, plan.Arena, Seed);
        }

        public MapNode Node { get; }

        /// <summary>The Kinship site's point of interest when this is its trial (<see cref="ForKinship"/>); null for a map location.</summary>
        public PointOfInterest Trial { get; }

        /// <summary>Whether this is a Kinship trial.</summary>
        public bool IsKinshipTrial
        {
            get { return Trial != null; }
        }

        public EncounterPlan Plan { get; }

        /// <summary>The region the battle is fought in (its backdrops and obstacles); null on the open board.</summary>
        public string RegionId { get; }

        /// <summary>The region whose art (enemy looks, backdrops) the battle is drawn with, open board or not.</summary>
        public string ArtRegionId { get; }

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

        /// <summary>The player message <paramref name="key"/> (<c>ui.map.*</c>) from the session's text table; the key itself without a session.</summary>
        private static string Say(GameSession session, string key, params object[] args)
        {
            return session?.Content?.Text == null ? key : session.Content.Text.Format(key, args);
        }

        /// <summary>The battle at <paramref name="nodeId"/> of the expedition in progress; null with <paramref name="error"/> when it cannot be fought now.</summary>
        public static NodeBattle For(GameSession session, int nodeId, out string error)
        {
            error = null;
            MapRun run = session?.Save?.Campaign?.ActiveRun;
            MapNode node = run?.Find(nodeId);
            if (node == null || !session.Save.Campaign.HasActiveRun)
            {
                error = Say(session, "ui.map.no_such_location");
                return null;
            }

            if (!node.IsBattle)
            {
                error = Say(session, "ui.map.nothing_to_fight", MapViewModel.KindLabel(node.Type, session.Content.Text));
                return null;
            }

            if (!CampaignRules.CanEnter(run, nodeId))
            {
                error = Say(session, "ui.map.unreachable");
                return null;
            }

            if (session.PendingPick > 0)
            {
                error = Say(session, "ui.map.pick_first");
                return null;
            }

            // Eased in the early regions for a new player (RegionData.StageEasing of RegionLibraryData.EasingShapeScales).
            EncounterPlan plan = CampaignRules.PlanFor(session.Save, run, node, session.Content.Encounters, session.Content.Enemies, session.Content.Campaign,
                                                       session.Content.Battle.GetSpecies);
            if (plan == null)
            {
                error = Say(session, "ui.map.encounter_not_built");
                return null;
            }

            // Fought on the region's battlefields (Hearthglen borrows Verdant Hollow's: RegionData.BattlefieldRegionId),
            // or on the open board where authored (FixedNodeData.OpenBoard); drawn with that region's art either way.
            RegionLibrary regions = session.Content.Campaign;
            return new NodeBattle(session, node, plan, regions.BattlefieldFor(run.RegionId, nodeId), regions.BattlefieldRegionOf(run.RegionId),
                                  CampaignRules.LossesAt(run, nodeId));
        }

        /// <summary>
        /// The Kinship trial at point of interest <paramref name="poiId"/> of the expedition in progress's
        /// map; null with <paramref name="error"/> when it cannot be fought now
        /// (<see cref="KinshipRules.Challengeable"/>). Its <see cref="Node"/> is a stand-in (type Trial,
        /// id −1, the point's level, the template, the point's seed): never on the map.
        /// </summary>
        public static NodeBattle ForKinship(GameSession session, string poiId, out string error)
        {
            error = null;
            if (session?.Save == null)
            {
                error = Say(session, "ui.map.no_game");
                return null;
            }

            PointOfInterest poi = KinshipRules.Challengeable(session.Save, session.Content.Discovery, poiId, out KinshipSiteData site, out error);
            if (poi == null)
            {
                return null;
            }

            if (session.PendingPick > 0)
            {
                error = Say(session, "ui.map.pick_first");
                return null;
            }

            EncounterPlan plan = KinshipRules.PlanFor(session.Content.Discovery, poi);
            if (plan == null)
            {
                error = Say(session, "ui.map.trial_not_built");
                return null;
            }

            MapNode stand = new MapNode
            {
                NodeId = -1,
                Type = MapNodeType.Trial,
                Kind = LocationKind.KinshipSite,
                Level = poi.Level,
                TemplateId = site.TemplateId,
                EncounterSeed = poi.Seed,
                LabelKey = poi.PoiId
            };
            RegionLibrary regions = session.Content.Campaign;
            string regionId = session.Save.Campaign.ActiveRun.RegionId;
            return new NodeBattle(session, stand, plan, regions.BattlefieldRegionOf(regionId), regions.BattlefieldRegionOf(regionId), session.Save.Discovery.KinshipLosses, poi);
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
            // The pack was charged as the battle began: note it, so a crash before the results hands it back.
            BattleConsumableRefund.Record(_session.Save, run.Result.ConsumablesUsed);
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
            BattleConsumableRefund.Clear(_session.Save);
            PlayerSave save = _session.Save;
            GameContent content = _session.Content;
            if (Trial != null)
            {
                bool knockedOut = false;
                foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
                {
                    foreach (BattleUnit unit in result.Units ?? new List<BattleUnit>())
                    {
                        knockedOut |= unit != null && unit.Id == pair.Value && unit.IsDefeated;
                    }
                }

                KinshipResult trial = KinshipRules.ResolveTrial(save, content.Discovery, Trial.PoiId, result.Outcome, Setup.TeamBeastIds, knockedOut);
                _session.Autosave(AutosaveReason.Results);
                return ResultsViewModel.BuildTrial(_session, this, result, trial);
            }

            Dictionary<string, (int Level, int Xp)> before = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
            foreach (OwnedBeast beast in save.Beasts)
            {
                before[beast.BeastId] = (beast.Progress.Level, beast.Progress.Xp);
            }

            int cap = CampaignRules.BeastCap(save, content.Campaign);
            RewardModifiers modifiers = CampaignRules.RewardModifiersFor(save.Campaign.ActiveRun, Node, content.Campaign).With(content.Economy);
            BattleRewardSummary summary = BattleSession.ApplyRewards(save, result, content.Battle, content.Drops, cap, modifiers);
            CampaignResult campaign = CampaignRules.ResolveBattle(save, content.Campaign, Node.NodeId, result.Outcome, content.Economy, content.Achievements);
            _session.CheckCompletion();
            _session.Autosave(AutosaveReason.Results);
            return ResultsViewModel.Build(_session, this, result, summary, campaign, before);
        }

        /// <summary>
        /// Retreats from this battle (the in-battle pause menu's Retreat, before it is decided):
        /// forfeits it outright through <see cref="CampaignRules.RetreatBattle"/> — producer decision,
        /// 2026-09-30, "same as losing the battle" (docs/design/battle-system.md, "Adaptive assist and
        /// guidance"). Unlike <see cref="Complete"/>, the battle's predetermined result is never played
        /// out or paid out: no rewards at all, not even a real loss's reduced XP (there is nothing to
        /// pay out from). The spent consumable is not refunded, exactly as a real loss keeps it spent
        /// (<see cref="Economy.BattleConsumableRefund"/>'s rule: a battle begun cannot hand items back
        /// by being abandoned). Autosaves. Once only (<see cref="IsCompleted"/>), like <see cref="Complete"/>.
        /// </summary>
        public ResultsViewModel Retreat()
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
            BattleConsumableRefund.Clear(_session.Save);
            CampaignResult campaign = CampaignRules.RetreatBattle(_session.Save, _session.Content.Campaign, Node.NodeId);
            _session.Autosave(AutosaveReason.Results);
            return ResultsViewModel.BuildRetreat(_session, this, campaign);
        }
    }
}
