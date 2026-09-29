using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One team beast's line on the results screen: its XP bar from before to after.</summary>
    public sealed class BeastResultRow
    {
        public string BeastId;
        public string SpeciesId;
        public string Name;
        public int LevelBefore;
        public int LevelAfter;
        public int XpGained;

        /// <summary>The XP bar before the battle, 0-1 of its level (the part shown already filled).</summary>
        public float FractionBefore;

        /// <summary>The XP bar after, 0-1 of the new level.</summary>
        public float FractionAfter;

        public int LevelsGained
        {
            get { return LevelAfter - LevelBefore; }
        }

        /// <summary>XP this battle put in the level-cap bank (held at the binding limit).</summary>
        public int Banked;

        /// <summary>The percent of its XP the level-gap falloff let through (100 unless it out-levelled the fight).</summary>
        public int FalloffPercent = 100;

        public bool KnockedOut;
    }

    /// <summary>One line of loot.</summary>
    public sealed class RewardLine
    {
        public string Kind;
        public string Id;
        public string Name;
        public int Quantity;
    }

    /// <summary>
    /// The one consolidated results screen: victory or defeat, every team beast's XP bar (before
    /// and after, with level-ups), the bench's share, gold, the materials, gear and looks dropped,
    /// the first-clear bonus, XP banked at the binding limit, and what the map made of it (a node
    /// cleared, a stage or region cleared and its seal, or the retry note after a loss).
    /// </summary>
    public sealed class ResultsViewModel
    {
        private ResultsViewModel()
        {
        }

        public BattleOutcome Outcome { get; private set; }

        public bool Victory
        {
            get { return Outcome == BattleOutcome.PlayerVictory; }
        }

        public string Title { get; private set; }

        /// <summary>Where: the location's name.</summary>
        public string Subtitle { get; private set; }

        public int NodeId { get; private set; }

        public List<BeastResultRow> Team { get; } = new List<BeastResultRow>();

        public int BenchXp { get; private set; }

        public int BenchLevelsGained { get; private set; }

        public int Gold { get; private set; }

        public int GoldTotal { get; private set; }

        public List<RewardLine> Drops { get; } = new List<RewardLine>();

        /// <summary>The material roll was this cell's first clear (its guaranteed drop and the gold bonus).</summary>
        public bool FirstClear { get; private set; }

        /// <summary>A pass's or lair's first-clear gear, by name, or null.</summary>
        public string FirstClearGear { get; private set; }

        /// <summary>XP held at the binding limit this battle, over the team and bench.</summary>
        public int XpBanked { get; private set; }

        /// <summary>Levels paid out of the banks when a new seal raised the limit.</summary>
        public int LevelsReleased { get; private set; }

        public string SealGranted { get; private set; }

        public int BindingLimit { get; private set; }

        public List<string> UnlockedRegions { get; } = new List<string>();

        public CampaignOutcome MapOutcome { get; private set; }

        /// <summary>After a loss: losses at this location, and what happens next.</summary>
        public int Losses { get; private set; }

        public string RetryNote { get; private set; }

        /// <summary>Lines for the "what happened on the map" box.</summary>
        public List<string> Notes { get; } = new List<string>();

        public int ConsumablesSpent { get; private set; }

        /// <summary>A Kinship trial's results (no XP or loot: a won trial leaves the choice of beasts pending).</summary>
        public bool IsKinshipTrial { get; private set; }

        /// <summary>A won trial: the beasts offered (their names), and whether the site's bond condition was met.</summary>
        public List<string> KinshipOffer { get; } = new List<string>();

        public bool BondMet { get; private set; }

        /// <summary>XP the Beastbinder (avatar) earned fighting beside the team, and levels gained.</summary>
        public int AvatarXp { get; private set; }

        public int AvatarLevelsGained { get; private set; }

        /// <summary>"&lt;title&gt; Beastbinder" with a title equipped, else plain "Beastbinder" (<see cref="AchievementsViewModel.TitledName"/>).</summary>
        public string AvatarDisplayName { get; private set; } = CampaignAvatar.DisplayName;

        /// <summary>A won trial's pick (2 or 3), now waiting on the map; 0 otherwise.</summary>
        public int PickStep { get; private set; }

        private BattleLogViewModel _log;
        private Func<BattleLogViewModel> _buildLog;

        /// <summary>
        /// The whole battle's log (every hit with its damage breakdown, heals, shields, statuses,
        /// bond reactions, the Beastbinder's arts), filterable by unit; built on first use from the
        /// battle's own records. Empty when the battle is not at hand.
        /// </summary>
        public BattleLogViewModel Log
        {
            get
            {
                if (_log == null)
                {
                    _log = _buildLog?.Invoke() ?? new BattleLogViewModel(null);
                }

                return _log;
            }
        }

        internal static ResultsViewModel Build(GameSession session, NodeBattle battle, BattleSessionResult result, BattleRewardSummary summary, CampaignResult campaign,
                                               Dictionary<string, (int Level, int Xp)> before)
        {
            PlayerSave save = session.Save;
            ResultsViewModel view = new ResultsViewModel
            {
                Outcome = result.Outcome,
                NodeId = battle.Node.NodeId,
                Subtitle = session.LocationName(battle.Node),
                Gold = summary.GoldGained,
                GoldTotal = save.Gold,
                FirstClear = summary.Loot.FirstClear,
                BindingLimit = CampaignRules.BeastCap(save, session.Content.Campaign),
                MapOutcome = campaign.Outcome,
                LevelsReleased = campaign.LevelsReleased,
                ConsumablesSpent = summary.ConsumablesSpent.Count,
                AvatarXp = summary.AvatarXpGained,
                AvatarLevelsGained = summary.AvatarLevelsGained,
                AvatarDisplayName = AchievementsViewModel.TitledName(save, session.Content.Achievements?.Library, CampaignAvatar.DisplayName)
            };
            view.Title = result.Outcome == BattleOutcome.PlayerVictory ? "Victory!" : result.Outcome == BattleOutcome.EnemyVictory ? "Defeat" : "Stalemate";
            BattleRun run = battle.Run?.Battle;
            if (run != null)
            {
                view._buildLog = () => BattleLogViewModel.Build(run, run.Units, BattleLogViewModel.NamesFor(session.Content, battle.SpeciesByUnit, run.Avatar?.Id));
            }

            HashSet<string> knockedOut = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
            {
                BattleUnit unit = FindUnit(result.Units, pair.Value);
                if (unit != null && unit.IsDefeated)
                {
                    knockedOut.Add(pair.Key);
                }
            }

            foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
            {
                OwnedBeast beast = save.FindBeast(pair.Key);
                if (beast == null)
                {
                    continue;
                }

                (int level, int xp) = before.TryGetValue(pair.Key, out (int, int) was) ? was : (beast.Progress.Level, beast.Progress.Xp);
                summary.BeastXpGained.TryGetValue(pair.Key, out int gained);
                summary.XpBanked.TryGetValue(pair.Key, out int banked);
                view.Team.Add(new BeastResultRow
                {
                    BeastId = beast.BeastId,
                    SpeciesId = beast.Progress.SpeciesId,
                    Name = session.BeastName(beast),
                    LevelBefore = level,
                    LevelAfter = beast.Progress.Level,
                    XpGained = gained,
                    FractionBefore = Fraction(level, xp),
                    FractionAfter = Fraction(beast.Progress.Level, beast.Progress.Xp),
                    Banked = banked,
                    FalloffPercent = summary.FalloffPercent.TryGetValue(pair.Key, out int percent) ? percent : 100,
                    KnockedOut = knockedOut.Contains(pair.Key)
                });
            }

            foreach (KeyValuePair<string, int> bench in summary.BenchXpGained)
            {
                view.BenchXp += bench.Value;
            }

            view.BenchLevelsGained = summary.BenchLevelsGained;
            foreach (KeyValuePair<string, int> banked in summary.XpBanked)
            {
                view.XpBanked += banked.Value;
            }

            foreach (MaterialStack stack in summary.Loot.Drops)
            {
                SkillMaterialData material = Array.Find(session.Content.SkillLibrary.Materials ?? new SkillMaterialData[0], m => m != null && m.MaterialId == stack.MaterialId);
                view.Drops.Add(new RewardLine { Kind = "material", Id = stack.MaterialId, Name = material?.DisplayName ?? stack.MaterialId, Quantity = stack.Quantity });
            }

            foreach (string gearId in summary.GearGained)
            {
                view.Drops.Add(new RewardLine { Kind = "gear", Id = gearId, Name = GearName(session, gearId), Quantity = 1 });
            }

            foreach (string look in summary.CosmeticsUnlocked)
            {
                view.Drops.Add(new RewardLine { Kind = "look", Id = look, Name = look, Quantity = 1 });
            }

            if (!string.IsNullOrEmpty(campaign.GearGranted))
            {
                view.FirstClearGear = GearName(session, campaign.GearGranted);
            }

            foreach (string look in campaign.CosmeticsUnlocked)
            {
                view.Drops.Add(new RewardLine { Kind = "look", Id = look, Name = look, Quantity = 1 });
            }

            if (!string.IsNullOrEmpty(campaign.SealGranted))
            {
                view.SealGranted = session.Content.Campaign.GetSeal(campaign.SealGranted)?.DisplayName ?? campaign.SealGranted;
            }

            foreach (string region in campaign.UnlockedRegionIds)
            {
                view.UnlockedRegions.Add(session.Content.Campaign.GetRegion(region)?.DisplayName ?? region);
            }

            view.Losses = campaign.Outcome == CampaignOutcome.Lost ? CampaignRules.LossesAt(save.Campaign.ActiveRun, battle.Node.NodeId) : 0;
            view.PickStep = campaign.PickStep;
            if (campaign.PickStep > 0)
            {
                view.Notes.Add("A beast stirs by the kinship stone. It wants to join you.");
            }
            view.BuildNotes(session);
            string extra = GameSession.ExtraRewardText(campaign.TitlesEarned, 0);
            if (extra.Length > 0)
            {
                view.Notes.Add(extra.Trim());
            }

            return view;
        }

        /// <summary>
        /// A Kinship trial's results (<see cref="NodeBattle.ForKinship"/>): the team's rows without XP (a
        /// trial pays nothing), and what the site made of it — on a win the beasts offered and the bond
        /// condition's flavour line, on a loss the retry note.
        /// </summary>
        internal static ResultsViewModel BuildTrial(GameSession session, NodeBattle battle, BattleSessionResult result, KinshipResult trial)
        {
            PlayerSave save = session.Save;
            ResultsViewModel view = new ResultsViewModel
            {
                Outcome = result.Outcome,
                NodeId = -1,
                Subtitle = trial?.Site?.Name ?? "Kinship trial",
                GoldTotal = save.Gold,
                BindingLimit = CampaignRules.BeastCap(save, session.Content.Campaign),
                MapOutcome = result.Outcome == BattleOutcome.PlayerVictory ? CampaignOutcome.Cleared : CampaignOutcome.Lost,
                ConsumablesSpent = result.ConsumablesUsed?.Count ?? 0,
                IsKinshipTrial = true,
                BondMet = trial != null && trial.BondMet
            };
            view.Title = result.Outcome == BattleOutcome.PlayerVictory ? "Trial won!" : "Not this time";
            BattleRun run = battle.Run?.Battle;
            if (run != null)
            {
                view._buildLog = () => BattleLogViewModel.Build(run, run.Units, BattleLogViewModel.NamesFor(session.Content, battle.SpeciesByUnit, run.Avatar?.Id));
            }

            foreach (KeyValuePair<string, string> pair in result.TeamUnitIds)
            {
                OwnedBeast beast = save.FindBeast(pair.Key);
                BattleUnit unit = FindUnit(result.Units, pair.Value);
                if (beast != null)
                {
                    view.Team.Add(new BeastResultRow
                    {
                        BeastId = beast.BeastId,
                        SpeciesId = beast.Progress.SpeciesId,
                        Name = session.BeastName(beast),
                        LevelBefore = beast.Progress.Level,
                        LevelAfter = beast.Progress.Level,
                        FractionBefore = Fraction(beast.Progress.Level, beast.Progress.Xp),
                        FractionAfter = Fraction(beast.Progress.Level, beast.Progress.Xp),
                        KnockedOut = unit != null && unit.IsDefeated
                    });
                }
            }

            if (trial != null && trial.Outcome == KinshipOutcome.Won)
            {
                foreach (string species in trial.Offer)
                {
                    view.KinshipOffer.Add(session.Content.Battle.GetSpecies(species)?.DisplayName ?? species);
                }

                view.Notes.Add(view.KinshipOffer.Count > 1
                                   ? string.Join(" and ", view.KinshipOffer) + " step out to meet you. Choose who joins your team."
                                   : view.KinshipOffer[0] + " steps out to meet you and joins your team.");
                if (!string.IsNullOrEmpty(trial.Site?.BondCondition))
                {
                    view.Notes.Add(view.BondMet ? "The bond rings true: " + BondFlavour(trial.Site) : "The trial is won, if not quite as the stone hoped. " + BondFlavour(trial.Site));
                }
            }
            else
            {
                view.RetryNote = "The trial waits for you: try again whenever you like (a fresh battle each time). A trial pays no XP.";
                view.Notes.Add(view.RetryNote);
            }

            return view;
        }

        private static string BondFlavour(KinshipSiteData site)
        {
            string text = site?.BondText ?? string.Empty;
            return text.StartsWith("Bond: ", StringComparison.Ordinal) ? char.ToUpperInvariant(text[6]) + text.Substring(7) : text;
        }

        private void BuildNotes(GameSession session)
        {
            switch (MapOutcome)
            {
                case CampaignOutcome.Cleared:
                    Notes.Add(Subtitle + " is cleared. The trail ahead is open.");
                    break;
                case CampaignOutcome.StageCleared:
                    Notes.Add("The pass is yours: stage cleared! The next stage's map awaits.");
                    break;
                case CampaignOutcome.RegionCleared:
                    Notes.Add("The lair is cleared: the region is yours!");
                    break;
                case CampaignOutcome.TutorialCleared:
                    Notes.Add("Hearthglen is behind you. Verdant Hollow is open.");
                    break;
                case CampaignOutcome.Lost:
                    RetryNote = "You can try " + Subtitle + " again (a fresh battle each time) or take another trail.";
                    if (TeamSuggestionPolicy.ShouldSuggest(Losses, session.Settings))
                    {
                        RetryNote += " A suggested team will be offered before the next attempt.";
                    }
                    else if (Losses < TeamSuggestionPolicy.MinLossesBeforeSuggestion && session.Settings.TeamSuggestionsEnabled)
                    {
                        RetryNote += " Losses here: " + Losses + ".";
                    }

                    Notes.Add(RetryNote);
                    break;
            }

            if (SealGranted != null)
            {
                Notes.Add("You claimed the " + SealGranted + ": your binding limit is now " + BindingLimit + ".");
            }

            if (LevelsReleased > 0)
            {
                Notes.Add("Banked XP paid out " + LevelsReleased + (LevelsReleased == 1 ? " level." : " levels."));
            }

            foreach (string region in UnlockedRegions)
            {
                Notes.Add(region + " is now open.");
            }
        }

        private static BattleUnit FindUnit(IReadOnlyList<BattleUnit> units, string unitId)
        {
            foreach (BattleUnit unit in units ?? new List<BattleUnit>())
            {
                if (unit != null && unit.Id == unitId)
                {
                    return unit;
                }
            }

            return null;
        }

        private static float Fraction(int level, int xp)
        {
            if (level >= BeastProgression.MaxLevel)
            {
                return 1f;
            }

            int next = Math.Max(1, BeastProgression.XpToNextLevel(level));
            return Math.Max(0f, Math.Min(1f, xp / (float)next));
        }

        private static string GearName(GameSession session, string gearId)
        {
            GearItem item = session.Content.Economy?.Gear?.Get(gearId);
            return item?.DisplayName ?? gearId;
        }
    }
}
