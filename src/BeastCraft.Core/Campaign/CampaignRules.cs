using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;
using BeastCraft.Economy;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Session;

namespace BeastCraft.Campaign
{
    /// <summary>
    /// The region campaign's rules over a <see cref="PlayerSave"/>: starting an expedition, which
    /// nodes can be entered, what a node fields, and what clearing, losing, camping, trading and
    /// retreating do to the save. Fighting is not here — the game fights a node's
    /// <see cref="PlanFor"/> through <c>BattleSession.Run</c> with <see cref="BattleSeed"/>, pays it
    /// out with <c>BattleSession.ApplyRewards(..., beastLevelCap: </c><see cref="BeastCap"/><c>)</c>,
    /// then reports the outcome to <see cref="ResolveBattle"/>.
    /// <para>
    /// <strong>An expedition</strong> is one stage of a region: a node map generated from a seed
    /// (<see cref="NodeMapGenerator"/>) and stored in <see cref="CampaignProgress.ActiveRun"/>. The
    /// player starts below row 0 and each step enters a node linked from the one they stand on.
    /// Battles start at full HP (no attrition between nodes).
    /// </para>
    /// <list type="bullet">
    /// <item><b>Win</b>: the node is cleared and becomes the current node. A Gate also clears the
    /// stage (the region's <see cref="RegionProgress.StagesCleared"/>) and ends the expedition; the
    /// Boss clears the region, grants its seal (<see cref="GrantSeal"/>: the cap rises and every
    /// beast's bank is released), unlocks the regions it opens, and ends the expedition.</item>
    /// <item><b>Loss</b> (lead decision: retry from the node): nothing is cleared, the player stays
    /// where they were and may retry the same node — the same lineup with a new battle seed
    /// (<see cref="BattleSeed"/> of the attempt) — or take another path. <see cref="Retreat"/>
    /// abandons the expedition.</item>
    /// <item><b>Rest ("Camp")</b>: trains one chosen beast by what a standing fielded beast earns for
    /// a clear at the node's level (after the level-gap falloff, under the cap), since battles
    /// already start at full HP. A travelling trader also waits at every camp: the game opens the
    /// shop there with <see cref="ShopContextFor"/> of the camp node (approved; the economy's pacing
    /// relies on it, about one Trader visit per stage).</item>
    /// <item><b>Shop ("Trader")</b>: opens the <see cref="IShopService"/> (the economy's
    /// <c>ShopService</c>: gold, frozen stock) and marks the node visited.</item>
    /// </list>
    /// <para>
    /// Replays are allowed: a stage already cleared can be started again with a new seed (the
    /// level-gap falloff makes old content pay little or no XP; drops still roll). Every method is
    /// non-throwing and returns a <see cref="CampaignResult"/>; a refused call changes nothing.
    /// </para>
    /// </summary>
    public static class CampaignRules
    {
        /// <summary>The beast level cap for <paramref name="save"/>'s seals (<see cref="LevelCaps.BeastCap"/>).</summary>
        public static int BeastCap(PlayerSave save, RegionLibrary library)
        {
            return LevelCaps.BeastCap(save == null || save.Campaign == null ? null : save.Campaign.Seals, library);
        }

        /// <summary>
        /// How far the player has got: the level of the highest cleared map location — a region's
        /// boss (its max level) once beaten, else the pass (Gate) of its last cleared stage, and any
        /// battle location cleared in the expedition in progress — over every unlocked region
        /// <paramref name="library"/> knows. 0 when nothing is cleared (or no save or library). What
        /// the idle rewards are paid at (<c>Idle.IdleRewardCalculator</c>).
        /// </summary>
        public static int ProgressLevel(PlayerSave save, RegionLibrary library)
        {
            if (save == null || save.Campaign == null || library == null)
            {
                return 0;
            }

            int best = 0;
            foreach (RegionProgress progress in save.Campaign.Regions ?? new List<RegionProgress>())
            {
                RegionData region = progress == null ? null : library.GetRegion(progress.RegionId);
                if (region == null)
                {
                    continue;
                }

                if (progress.BossCleared)
                {
                    best = Math.Max(best, region.MaxLevel);
                    continue;
                }

                if (progress.StagesCleared > 0)
                {
                    MapRulesData rules = library.RulesFor(region);
                    int gate = NodeMapGenerator.RowLevel(region, rules, progress.StagesCleared - 1, rules.Layers - 1) + rules.GateLevelOffset;
                    best = Math.Max(best, gate);
                }
            }

            MapRun run = save.Campaign.ActiveRun;
            if (save.Campaign.HasActiveRun && library.GetRegion(run.RegionId) != null)
            {
                foreach (int nodeId in run.Cleared ?? new List<int>())
                {
                    MapNode node = run.Find(nodeId);
                    if (node != null && node.IsBattle)
                    {
                        best = Math.Max(best, node.Level);
                    }
                }
            }

            return Math.Max(0, Math.Min(BeastProgression.MaxLevel, best));
        }

        /// <summary>
        /// The stage an expedition into <paramref name="regionId"/> starts at by default: the first
        /// uncleared stage, or the last stage once all are cleared (the boss again, for a replay).
        /// −1 when the region is unknown or not unlocked.
        /// </summary>
        public static int NextStage(PlayerSave save, RegionLibrary library, string regionId)
        {
            RegionData region = library == null ? null : library.GetRegion(regionId);
            RegionProgress progress = save == null || save.Campaign == null ? null : save.Campaign.FindRegion(regionId);
            if (region == null || progress == null)
            {
                return -1;
            }

            int stages = Math.Max(1, region.Stages);
            return Math.Min(Math.Max(0, progress.StagesCleared), stages - 1);
        }

        /// <summary>Starts a <see cref="RunDifficulty.Normal"/> expedition into <paramref name="regionId"/> at its <see cref="NextStage"/>.</summary>
        public static CampaignResult StartRun(PlayerSave save, RegionLibrary library, string regionId, int seed)
        {
            return StartRun(save, library, regionId, NextStage(save, library, regionId), seed, RunDifficulty.Normal);
        }

        /// <summary>Starts an expedition on <paramref name="difficulty"/> into <paramref name="regionId"/> at its <see cref="NextStage"/>.</summary>
        public static CampaignResult StartRun(PlayerSave save, RegionLibrary library, string regionId, int seed, RunDifficulty difficulty)
        {
            return StartRun(save, library, regionId, NextStage(save, library, regionId), seed, difficulty);
        }

        /// <summary>A <see cref="RunDifficulty.Normal"/> expedition into stage <paramref name="stage"/> of <paramref name="regionId"/>.</summary>
        public static CampaignResult StartRun(PlayerSave save, RegionLibrary library, string regionId, int stage, int seed)
        {
            return StartRun(save, library, regionId, stage, seed, RunDifficulty.Normal);
        }

        /// <summary>
        /// Starts an expedition into stage <paramref name="stage"/> of <paramref name="regionId"/> on
        /// <paramref name="difficulty"/> (the player's choice when starting; stored on the run as
        /// <see cref="MapRun.Difficulty"/>), its map generated from <paramref name="seed"/> with the
        /// region and rules of that difficulty (<see cref="RegionLibrary.RegionFor"/>,
        /// <see cref="RegionLibrary.RulesFor(RegionData, RunDifficulty)"/>: on Hard, the region's
        /// <see cref="RegionData.HardMode"/> shapes and boss). Refused when an expedition is already in
        /// progress (retreat first), the region is unknown or locked, the stage is past the first
        /// uncleared one, or the difficulty is not allowed there (Hard outside a post-game region, or
        /// an undefined value).
        /// </summary>
        public static CampaignResult StartRun(PlayerSave save, RegionLibrary library, string regionId, int stage, int seed, RunDifficulty difficulty)
        {
            if (save == null || library == null)
            {
                return CampaignResult.Refused("No save or no region library.");
            }

            save.EnsureInitialized();
            RegionData region = library.GetRegion(regionId);
            if (region == null)
            {
                return CampaignResult.Refused("Unknown region '" + regionId + "'.");
            }

            if (!save.Campaign.IsUnlocked(regionId))
            {
                return CampaignResult.Refused("Region '" + regionId + "' is not unlocked.");
            }

            if (save.Campaign.HasActiveRun)
            {
                return CampaignResult.Refused("An expedition into '" + save.Campaign.ActiveRun.RegionId + "' is in progress; retreat or finish it first.");
            }

            int next = NextStage(save, library, regionId);
            if (stage < 0 || stage > next)
            {
                return CampaignResult.Refused("Stage " + stage + " of '" + regionId + "' is not reachable yet (next is " + next + ").");
            }

            if (!Enum.IsDefined(typeof(RunDifficulty), difficulty) || !RegionLibrary.Allows(region, difficulty))
            {
                return CampaignResult.Refused("Difficulty " + difficulty + " is not available in '" + regionId + "' (Hard is for post-game regions only).");
            }

            MapRun run = save.Campaign.ActiveRun;
            run.Clear();
            run.RegionId = regionId;
            run.Stage = stage;
            run.Seed = seed;
            run.Difficulty = difficulty;
            run.Nodes = NodeMapGenerator.Generate(library.RegionFor(region, difficulty), library.RulesFor(region, difficulty), stage, seed);
            save.Campaign.CurrentRegionId = regionId;
            return CampaignResult.Done(CampaignOutcome.Started, null);
        }

        /// <summary>
        /// Whether <paramref name="nodeId"/> can be entered now: an expedition is in progress, the
        /// node is on its map and not yet cleared, and it is on row 0 (before the first step) or
        /// linked from the current node.
        /// </summary>
        public static bool CanEnter(MapRun run, int nodeId)
        {
            MapNode node = run == null || string.IsNullOrEmpty(run.RegionId) ? null : run.Find(nodeId);
            if (node == null || run.IsCleared(nodeId))
            {
                return false;
            }

            if (run.CurrentNodeId < 0)
            {
                return node.Layer == 0;
            }

            MapNode current = run.Find(run.CurrentNodeId);
            return current != null && current.Next != null && Array.IndexOf(current.Next, nodeId) >= 0;
        }

        /// <summary>The nodes that can be entered now (<see cref="CanEnter"/>), in id order.</summary>
        public static List<MapNode> Choices(MapRun run)
        {
            List<MapNode> choices = new List<MapNode>();
            if (run == null || run.Nodes == null)
            {
                return choices;
            }

            foreach (MapNode node in run.Nodes)
            {
                if (node != null && CanEnter(run, node.NodeId))
                {
                    choices.Add(node);
                }
            }

            return choices;
        }

        /// <summary>A den's (Elite's) gold multiplier (<see cref="RewardModifiersFor"/>).</summary>
        public const double EliteGoldMultiplier = 1.5;

        /// <summary>The flat gold a pass (Gate) adds to its clear.</summary>
        public const int GateBonusGold = 5;

        /// <summary>The flat gold a lair (Boss) adds to its clear.</summary>
        public const int BossBonusGold = 10;

        /// <summary>
        /// The economy reward modifiers a clear of <paramref name="node"/> pays with, for
        /// <c>BattleSession.ApplyRewards(..., modifiers)</c>: a den (Elite) x<see cref="EliteGoldMultiplier"/>
        /// gold, a pass (Gate) +<see cref="GateBonusGold"/>, a lair (Boss) +<see cref="BossBonusGold"/>;
        /// anything else <see cref="RewardModifiers.None"/>'s values. A fresh object each call (the
        /// caller may attach the gear and cosmetic libraries).
        /// </summary>
        public static RewardModifiers RewardModifiersFor(MapNode node)
        {
            RewardModifiers modifiers = new RewardModifiers();
            if (node == null)
            {
                return modifiers;
            }

            if (node.Type == MapNodeType.Elite)
            {
                modifiers.GoldMultiplier = EliteGoldMultiplier;
            }
            else if (node.Type == MapNodeType.Gate)
            {
                modifiers.BonusGold = GateBonusGold;
            }
            else if (node.Type == MapNodeType.Boss)
            {
                modifiers.BonusGold = BossBonusGold;
            }

            return modifiers;
        }

        /// <summary>The battle seed of attempt <paramref name="attempt"/> (0 = the first) at <paramref name="node"/>: <c>LootRoller.DeriveSeed(EncounterSeed, attempt)</c>.</summary>
        public static int BattleSeed(MapNode node, int attempt)
        {
            return LootRoller.DeriveSeed(node == null ? 0 : node.EncounterSeed, attempt < 0 ? 0 : attempt);
        }

        /// <summary>
        /// The encounter a battle node fields: its template at its level (Boss, authored Gate), or a
        /// generated encounter of its shape at its level drawn with its <see cref="MapNode.EncounterSeed"/>
        /// (so a retry fields the same lineup). Null for Rest and Shop nodes or unknown content.
        /// </summary>
        public static EncounterPlan PlanFor(MapNode node, EncounterLibrary encounters, EnemyCatalog enemies)
        {
            if (node == null || !node.IsBattle)
            {
                return null;
            }

            return string.IsNullOrEmpty(node.TemplateId)
                ? EncounterPlan.Generate(encounters, enemies, node.ShapeId, node.Level, node.EncounterSeed)
                : EncounterPlan.FromTemplate(encounters, enemies, node.TemplateId, node.Level);
        }

        /// <summary>
        /// Records the outcome of the battle fought at <paramref name="nodeId"/> (rewards are paid by
        /// <c>BattleSession.ApplyRewards</c>, not here). See the class remarks for a win and a loss.
        /// </summary>
        public static CampaignResult ResolveBattle(PlayerSave save, RegionLibrary library, int nodeId, BattleOutcome outcome)
        {
            return ResolveBattle(save, library, nodeId, outcome, null);
        }

        /// <summary>The <c>LootRoller.DeriveSeed(node.EncounterSeed, …)</c> stream a pass's or lair's first-clear reward is drawn on.</summary>
        public const int NodeRewardStream = 5;

        /// <summary>
        /// <see cref="ResolveBattle(PlayerSave, RegionLibrary, int, BattleOutcome)"/> with the
        /// economy's first-clear rewards (<paramref name="economy"/> null = none): the first clear of
        /// a stage's pass (Gate) grants a guaranteed common of its band (user decision), the
        /// first clear of a region's lair (Boss) a guaranteed epic from the boss pool (a rare below the epic bands), each
        /// drawn on <c>DeriveSeed(node.EncounterSeed, </c><see cref="NodeRewardStream"/><c>)</c>
        /// (<see cref="CampaignResult.GearGranted"/>); a lair's first clear also unlocks its
        /// boss-exclusive looks and then any milestone looks reached (<see cref="CampaignResult.CosmeticsUnlocked"/>).
        /// A replay grants nothing new.
        /// </summary>
        public static CampaignResult ResolveBattle(PlayerSave save, RegionLibrary library, int nodeId, BattleOutcome outcome, EconomyContent economy)
        {
            CampaignResult refused = CheckNode(save, library, nodeId, out MapRun run, out MapNode node, out RegionData region);
            if (refused != null)
            {
                return refused;
            }

            if (!node.IsBattle)
            {
                return CampaignResult.Refused("Node " + nodeId + " is a " + node.Type + " node, not a battle.");
            }

            if (outcome != BattleOutcome.PlayerVictory)
            {
                run.Attempts++;
                if (run.NodeAttemptsNodeId != nodeId)
                {
                    // A loss at another location starts that location's count afresh.
                    run.NodeAttemptsNodeId = nodeId;
                    run.NodeAttempts = 0;
                }

                run.NodeAttempts++;
                return CampaignResult.Done(CampaignOutcome.Lost, node);
            }

            Clear(run, node);
            RegionProgress progress = save.Campaign.FindRegion(run.RegionId);
            if (node.Type == MapNodeType.Gate)
            {
                bool firstGate = progress.StagesCleared <= run.Stage;
                progress.StagesCleared = Math.Max(progress.StagesCleared, run.Stage + 1);
                run.Clear();
                CampaignResult gate = CampaignResult.Done(CampaignOutcome.StageCleared, node);
                if (firstGate)
                {
                    GrantNodeGear(save, economy, node, 0, gate);
                }

                return gate;
            }

            if (node.Type == MapNodeType.Boss)
            {
                bool firstBoss = !progress.BossCleared;
                progress.StagesCleared = Math.Max(progress.StagesCleared, Math.Max(1, region.Stages) - 1);
                progress.BossCleared = true;
                CampaignResult result = CampaignResult.Done(CampaignOutcome.RegionCleared, node);
                if (firstBoss)
                {
                    GrantNodeGear(save, economy, node, 2, result);
                    if (economy != null && economy.Cosmetics != null)
                    {
                        result.CosmeticsUnlocked.AddRange(CosmeticRules.UnlockBossLooks(save, economy.Cosmetics, region.RegionId));
                    }
                }

                if (run.Difficulty == RunDifficulty.Hard && economy != null && economy.Cosmetics != null)
                {
                    // Every Hard clear (the first included) unlocks the Hard-only looks not yet owned;
                    // the loot is Normal's (the gear above is the first clear's either way).
                    result.CosmeticsUnlocked.AddRange(CosmeticRules.UnlockHardBossLooks(save, economy.Cosmetics, region.RegionId));
                }

                if (!string.IsNullOrEmpty(region.BossRewardSealId))
                {
                    CampaignResult seal = GrantSeal(save, library, region.BossRewardSealId);
                    result.SealGranted = seal.SealGranted;
                    result.LevelsReleased = seal.LevelsReleased;
                    result.BeastLevelCap = seal.BeastLevelCap;
                }

                foreach (RegionData unlocked in library.UnlockedBy(region.RegionId))
                {
                    if (save.Campaign.Unlock(unlocked.RegionId))
                    {
                        result.UnlockedRegionIds.Add(unlocked.RegionId);
                    }
                }

                run.Clear();
                if (economy != null && economy.Cosmetics != null)
                {
                    result.CosmeticsUnlocked.AddRange(CosmeticRules.UnlockMilestones(save, economy.Cosmetics));
                }

                return result;
            }

            return CampaignResult.Done(CampaignOutcome.Cleared, node);
        }

        /// <summary>
        /// Camps at Rest node <paramref name="nodeId"/>, training <paramref name="beastId"/>: it earns
        /// what a standing fielded beast earns for a clear at the node's level
        /// (<c>BeastProgression.AwardBattle</c>: falloff on its level, under the cap). The node is
        /// cleared and becomes current. Refused for an unknown beast.
        /// </summary>
        public static CampaignResult Camp(PlayerSave save, RegionLibrary library, int nodeId, string beastId)
        {
            CampaignResult refused = CheckNode(save, library, nodeId, out MapRun run, out MapNode node, out RegionData _);
            if (refused != null)
            {
                return refused;
            }

            if (node.Type != MapNodeType.Rest)
            {
                return CampaignResult.Refused("Node " + nodeId + " is a " + node.Type + " node, not a camp.");
            }

            OwnedBeast beast = save.FindBeast(beastId);
            if (beast == null)
            {
                return CampaignResult.Refused("No beast '" + beastId + "' to train.");
            }

            CampaignResult result = CampaignResult.Done(CampaignOutcome.Visited, node);
            result.BeastLevelCap = BeastCap(save, library);
            result.XpTrained = BeastProgression.BattleXp(BattleOutcome.PlayerVictory, node.Level, false, beast.Progress.Level);
            result.LevelsGained = BeastProgression.AwardBattle(beast.Progress, BattleOutcome.PlayerVictory, node.Level, false, result.BeastLevelCap);
            Clear(run, node);
            return result;
        }

        /// <summary>
        /// Visits Shop node <paramref name="nodeId"/>: opens <paramref name="shop"/> (the economy's
        /// <c>ShopService</c> rolls and freezes the trading post's stock; null or the stub offers
        /// nothing) and marks the node cleared and current; buying happens through the shop with the
        /// same <see cref="ShopContext"/> (<see cref="ShopContextFor"/>).
        /// <see cref="CampaignResult.ShopOpened"/> says whether a shop was offered.
        /// </summary>
        public static CampaignResult Trade(PlayerSave save, RegionLibrary library, int nodeId, IShopService shop)
        {
            CampaignResult refused = CheckNode(save, library, nodeId, out MapRun run, out MapNode node, out RegionData _);
            if (refused != null)
            {
                return refused;
            }

            if (node.Type != MapNodeType.Shop)
            {
                return CampaignResult.Refused("Node " + nodeId + " is a " + node.Type + " node, not a shop.");
            }

            CampaignResult result = CampaignResult.Done(CampaignOutcome.Visited, node);
            result.ShopOpened = shop != null && shop.Open(save, ShopContextFor(run, node));
            Clear(run, node);
            return result;
        }

        /// <summary>The <see cref="ShopContext"/> of trading post <paramref name="node"/> on <paramref name="run"/> (its region, stage, id, level and seed).</summary>
        public static ShopContext ShopContextFor(MapRun run, MapNode node)
        {
            return new ShopContext(run == null ? string.Empty : run.RegionId, run == null ? 0 : run.Stage, node == null ? -1 : node.NodeId, node == null ? 1 : node.Level,
                                   node == null ? 0 : node.EncounterSeed);
        }

        /// <summary>Abandons the expedition in progress (nothing it cleared is kept; stage progress already earned stays).</summary>
        public static CampaignResult Retreat(PlayerSave save)
        {
            if (save == null || save.Campaign == null || !save.Campaign.HasActiveRun)
            {
                return CampaignResult.Refused("No expedition in progress.");
            }

            save.Campaign.ActiveRun.Clear();
            return CampaignResult.Done(CampaignOutcome.Retreated, null);
        }

        /// <summary>
        /// Grants seal <paramref name="sealId"/> (a region boss's reward, or a story event's — the
        /// hook for future non-boss seals) and releases every beast's level-cap bank at the new cap
        /// (<see cref="LevelCap.Release"/>). Refused for an unknown seal; granting one already owned
        /// changes nothing and succeeds with <see cref="CampaignResult.SealGranted"/> null.
        /// </summary>
        public static CampaignResult GrantSeal(PlayerSave save, RegionLibrary library, string sealId)
        {
            if (save == null || library == null)
            {
                return CampaignResult.Refused("No save or no region library.");
            }

            if (library.GetSeal(sealId) == null)
            {
                return CampaignResult.Refused("Unknown seal '" + sealId + "'.");
            }

            save.EnsureInitialized();
            CampaignResult result = CampaignResult.Done(CampaignOutcome.SealGranted, null);
            if (save.Campaign.AddSeal(sealId))
            {
                result.SealGranted = sealId;
            }

            result.BeastLevelCap = BeastCap(save, library);
            foreach (OwnedBeast beast in save.Beasts)
            {
                result.LevelsReleased += LevelCap.Release(beast.Progress, result.BeastLevelCap);
            }

            return result;
        }

        /// <summary>
        /// How many times the player has lost at <paramref name="nodeId"/> since last clearing a node:
        /// <see cref="MapRun.NodeAttempts"/> when they are being counted at that location
        /// (<see cref="MapRun.NodeAttemptsNodeId"/>), else 0. It is also the attempt number of the next
        /// fight there (<see cref="BattleSeed"/>). 0 for a null run.
        /// </summary>
        public static int LossesAt(MapRun run, int nodeId)
        {
            return run != null && nodeId >= 0 && run.NodeAttemptsNodeId == nodeId ? run.NodeAttempts : 0;
        }

        /// <summary>
        /// The pre-fight team suggestion for location <paramref name="nodeId"/> of the expedition in
        /// progress, or null when none is offered. The one call site of the suggestion rule (user
        /// decision): <see cref="TeamSuggestionPolicy.ShouldSuggest"/> of <see cref="LossesAt"/> and
        /// <paramref name="settings"/> (null = the defaults); when it holds,
        /// <see cref="TeamSuggester.Suggest"/> over every beast the save owns (each as its species from
        /// <paramref name="content"/> at its level; unknown species are skipped) against the node's
        /// encounter (<see cref="PlanFor"/>: its <see cref="EncounterPlan.Preview"/>, the always-free
        /// <c>Full</c> preview, and whether its enemies' kits can afflict the team), with
        /// <paramref name="content"/>'s team bonds. Null also when there is no expedition, the node is
        /// not a battle, or the content cannot build its encounter. Pure; changes nothing.
        /// </summary>
        public static CampaignTeamSuggestion SuggestionFor(PlayerSave save, int nodeId, PlayerSettings settings, EncounterLibrary encounters, BattleContent content,
                                                           int teamSize = 4)
        {
            if (save == null || save.Campaign == null || !save.Campaign.HasActiveRun || content == null || content.Enemies == null)
            {
                return null;
            }

            MapRun run = save.Campaign.ActiveRun;
            int losses = LossesAt(run, nodeId);
            if (!TeamSuggestionPolicy.ShouldSuggest(losses, settings))
            {
                return null;
            }

            EncounterPlan plan = PlanFor(run.Find(nodeId), encounters, content.Enemies);
            if (plan == null)
            {
                return null;
            }

            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>();
            List<string> ownedIds = new List<string>();
            foreach (OwnedBeast beast in save.Beasts)
            {
                CreatureSpeciesSO species = beast == null || beast.Progress == null ? null : content.GetSpecies(beast.Progress.SpeciesId);
                if (species != null)
                {
                    owned.Add(new TeamSuggestionCandidate(species, beast.Progress.Level));
                    ownedIds.Add(beast.BeastId);
                }
            }

            List<SkillSO> enemySkills = new List<SkillSO>();
            foreach (EncounterLineupEnemy enemy in plan.Enemies)
            {
                IReadOnlyList<SkillSO> kit = plan.Catalog.Kit(enemy.EnemyId, enemy.Element);
                if (kit != null)
                {
                    enemySkills.AddRange(kit);
                }
            }

            TeamSuggestion suggestion = TeamSuggester.Suggest(new TeamSuggestionRequest
            {
                Preview = plan.Preview(),
                Owned = owned,
                TeamSize = teamSize,
                Bonds = content.TeamBonds,
                EncounterCanAfflict = TeamSuggester.CanAfflict(enemySkills)
            });

            List<string> beastIds = new List<string>();
            foreach (int member in suggestion.Members)
            {
                beastIds.Add(ownedIds[member]);
            }

            return new CampaignTeamSuggestion(nodeId, losses, suggestion, beastIds);
        }

        private static CampaignResult CheckNode(PlayerSave save, RegionLibrary library, int nodeId, out MapRun run, out MapNode node, out RegionData region)
        {
            run = null;
            node = null;
            region = null;
            if (save == null || library == null)
            {
                return CampaignResult.Refused("No save or no region library.");
            }

            save.EnsureInitialized();
            run = save.Campaign.ActiveRun;
            if (!save.Campaign.HasActiveRun)
            {
                return CampaignResult.Refused("No expedition in progress.");
            }

            region = library.GetRegion(run.RegionId);
            if (region == null || save.Campaign.FindRegion(run.RegionId) == null)
            {
                return CampaignResult.Refused("The expedition's region '" + run.RegionId + "' is unknown or locked.");
            }

            if (!CanEnter(run, nodeId))
            {
                return CampaignResult.Refused("Node " + nodeId + " cannot be entered from where the player stands.");
            }

            node = run.Find(nodeId);
            return null;
        }

        private static void GrantNodeGear(PlayerSave save, EconomyContent economy, MapNode node, int rarity, CampaignResult result)
        {
            if (economy == null || economy.Gear == null)
            {
                return;
            }

            GearItem item = GearDrops.RollGuaranteed(economy.Gear, rarity, node.Level, new Random(LootRoller.DeriveSeed(node.EncounterSeed, NodeRewardStream)));
            if (item != null && GearDrops.Grant(save, item) != null)
            {
                result.GearGranted = item.GearId;
            }
        }

        private static void Clear(MapRun run, MapNode node)
        {
            run.Cleared.Add(node.NodeId);
            run.CurrentNodeId = node.NodeId;
            run.NodeAttempts = 0;
            run.NodeAttemptsNodeId = -1;
        }
    }

    /// <summary>What a <see cref="CampaignRules"/> call did.</summary>
    public enum CampaignOutcome
    {
        /// <summary>Refused: nothing changed (see <see cref="CampaignResult.Error"/>).</summary>
        Refused,

        /// <summary>An expedition started.</summary>
        Started,

        /// <summary>A battle node was won and cleared.</summary>
        Cleared,

        /// <summary>A battle was lost: the player stays and may retry or go elsewhere.</summary>
        Lost,

        /// <summary>A Gate was won: the stage is cleared and the expedition over.</summary>
        StageCleared,

        /// <summary>The Boss was won: the region is cleared, its seal granted, the expedition over.</summary>
        RegionCleared,

        /// <summary>A Rest or Shop node was visited.</summary>
        Visited,

        /// <summary>The expedition was abandoned.</summary>
        Retreated,

        /// <summary>A seal was granted (or was already owned).</summary>
        SealGranted
    }

    /// <summary>The result of a <see cref="CampaignRules"/> call.</summary>
    public sealed class CampaignResult
    {
        private CampaignResult()
        {
        }

        /// <summary>What happened.</summary>
        public CampaignOutcome Outcome { get; private set; }

        /// <summary>Whether anything was done (not <see cref="CampaignOutcome.Refused"/>).</summary>
        public bool Success
        {
            get { return Outcome != CampaignOutcome.Refused; }
        }

        /// <summary>Why the call was refused; null otherwise.</summary>
        public string Error { get; private set; }

        /// <summary>The node acted on, when there was one.</summary>
        public MapNode Node { get; private set; }

        /// <summary>The seal newly granted, or null.</summary>
        public string SealGranted { get; internal set; }

        /// <summary>Beast levels paid out of the banks when the cap rose.</summary>
        public int LevelsReleased { get; internal set; }

        /// <summary>The beast level cap after the call, when it was computed (seal grants, camps); 0 otherwise.</summary>
        public int BeastLevelCap { get; internal set; }

        /// <summary>Regions newly unlocked by a boss clear.</summary>
        public List<string> UnlockedRegionIds { get; } = new List<string>();

        /// <summary>A camp's XP to the trained beast (after the falloff).</summary>
        public int XpTrained { get; internal set; }

        /// <summary>Levels a camp gave the trained beast.</summary>
        public int LevelsGained { get; internal set; }

        /// <summary>Whether a Shop visit actually offered a shop (the stub never does).</summary>
        public bool ShopOpened { get; internal set; }

        /// <summary>The gear id a pass's or lair's first clear granted (now a new instance in the save), or null.</summary>
        public string GearGranted { get; internal set; }

        /// <summary>Cosmetic looks a lair's first clear unlocked (its boss-exclusive looks, then milestones such as bosses beaten).</summary>
        public List<string> CosmeticsUnlocked { get; } = new List<string>();

        internal static CampaignResult Refused(string error)
        {
            return new CampaignResult { Outcome = CampaignOutcome.Refused, Error = error };
        }

        internal static CampaignResult Done(CampaignOutcome outcome, MapNode node)
        {
            return new CampaignResult { Outcome = outcome, Node = node };
        }
    }

    /// <summary>A team suggested before a fight (<see cref="CampaignRules.SuggestionFor"/>).</summary>
    public sealed class CampaignTeamSuggestion
    {
        public CampaignTeamSuggestion(int nodeId, int losses, TeamSuggestion suggestion, IReadOnlyList<string> beastIds)
        {
            NodeId = nodeId;
            Losses = losses;
            Suggestion = suggestion;
            BeastIds = beastIds;
        }

        /// <summary>The map location the suggestion is for.</summary>
        public int NodeId { get; }

        /// <summary>The losses at that location that triggered it (<see cref="CampaignRules.LossesAt"/>).</summary>
        public int Losses { get; }

        /// <summary>The suggester's result (members index the save's beasts with a known species, in save order).</summary>
        public TeamSuggestion Suggestion { get; }

        /// <summary>The suggested beasts' <see cref="OwnedBeast.BeastId"/>s, in save order.</summary>
        public IReadOnlyList<string> BeastIds { get; }
    }
}
