using System;
using System.Collections.Generic;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// A beast picker the trial-pick popup shows (<c>TrialPickModal</c>): Hearthglen's stance-cycle pick
    /// (<see cref="StarterPickViewModel"/>) or a Kinship site's choice of two (<see cref="KinshipPickViewModel"/>).
    /// </summary>
    public interface IBeastPicker
    {
        string Title { get; }

        string Subtitle { get; }

        /// <summary>The stance this pick must be (the explainer line), or null.</summary>
        CombatStance? RequiredStance { get; }

        List<PickOptionView> Options { get; }

        /// <summary>Makes the pick; false with <paramref name="error"/> when refused.</summary>
        bool Choose(string speciesId, out string error);

        /// <summary>The toast once <paramref name="option"/> has joined.</summary>
        string JoinedMessage(PickOptionView option);
    }

    /// <summary>
    /// A won Kinship trial's choice (<see cref="KinshipRules.Pending"/>): the one or two not-yet-owned
    /// beasts the site offers (the region's theme first), each with its stance, element, art and blurb,
    /// and the level they join at; <see cref="Choose"/> makes it through the session. Shown by the
    /// Hearthglen trial's popup, so both picks look alike.
    /// </summary>
    public sealed class KinshipPickViewModel : IBeastPicker
    {
        private readonly GameSession _session;

        public KinshipPickViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            KinshipResult pending = KinshipRules.Pending(session.Save, session.Content.Discovery);
            Site = pending.Site;
            JoinLevel = pending.JoinLevel;
            foreach (string id in pending.Offer)
            {
                CreatureSpeciesSO species = session.Content.Battle.GetSpecies(id);
                if (species != null)
                {
                    Options.Add(StarterPickViewModel.OptionOf(species));
                }
            }
        }

        public KinshipSiteData Site { get; }

        public int JoinLevel { get; }

        public List<PickOptionView> Options { get; } = new List<PickOptionView>();

        public CombatStance? RequiredStance
        {
            get { return null; }
        }

        public string Title
        {
            get { return Options.Count > 1 ? "Two beasts answer" : "A beast answers"; }
        }

        public string Subtitle
        {
            get
            {
                return (Options.Count > 1 ? "At the " + (Site?.Name ?? "kinship stone") + ", choose who joins you." : "At the " + (Site?.Name ?? "kinship stone") + ", the last beast joins you.") +
                       " They join at level " + JoinLevel + ", a little behind your team; camp and the bench help them catch up.";
            }
        }

        public bool Choose(string speciesId, out string error)
        {
            KinshipResult result = _session.ChooseKinship(speciesId);
            error = result.Error;
            return result.Success;
        }

        public string JoinedMessage(PickOptionView option)
        {
            return (option?.Name ?? "A beast") + " joins your team at level " + JoinLevel + "!";
        }
    }

    /// <summary>
    /// A point of interest's popup (tap it on the map): its name and kind, what it says (a shrine's
    /// words, a lore stone's entry, a cache's contents, a Vista's promise, a Kinship site's story), and
    /// the one action (<see cref="Visit"/>) while it can be visited; a found point shows what it held.
    /// </summary>
    public sealed class PoiViewModel
    {
        private readonly GameSession _session;

        public PoiViewModel(GameSession session, string poiId)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Poi = PointOfInterest.Find(DiscoveryRules.PointsOnMap(session.Save, session.Content.Discovery), poiId);
            if (Poi == null)
            {
                Title = "Nothing here";
                return;
            }

            State = DiscoveryRules.StateOf(session.Save, session.Content.Discovery, Poi);
            Title = NameOf(session, Poi);
            KindLabel = MapViewModel.PoiKindLabel(Poi.Kind);
            DiscoveryLibrary library = session.Content.Discovery.Library;
            switch (Poi.Kind)
            {
                case PoiKind.Shrine:
                    Body = library.Shrine(Poi.RefId)?.Text;
                    Action = "Rest a while";
                    Found = "You rested here. Something of this place will grow in your Grove.";
                    break;
                case PoiKind.LoreStone:
                    Body = State == PoiState.Found ? library.Lore(Poi.RefId)?.Text : "Old carvings cover the stone, worn soft by weather.";
                    Action = "Read the stone";
                    break;
                case PoiKind.Cache:
                    Body = State == PoiState.Found ? "Empty now: you took what it held." : "Something is tucked away here: " + CacheText(session, library.Cache(Poi.RefId)) + ".";
                    Action = "Open it";
                    break;
                case PoiKind.Vista:
                    Body = State == PoiState.Found ? "You have looked out from here: the land around is mapped." : "A high place with a long view. From the top you could map the land around.";
                    Action = "Look out";
                    break;
                default:
                    KinshipSiteData site = library.Site(Poi.RefId);
                    bool claimed = site != null && session.Save.Discovery.HasClaimed(site.SiteId);
                    Body = claimed ? library.Lore(site.LoreId)?.Text : site?.Intro;
                    Action = claimed ? null : "Visit the stone";
                    Found = claimed ? "Your bond was made here." : null;
                    break;
            }

            if (State == PoiState.Found && Poi.Kind != PoiKind.LoreStone && Poi.Kind != PoiKind.KinshipSite)
            {
                Body = Found ?? Body;
            }
        }

        public PointOfInterest Poi { get; }

        public PoiState State { get; }

        public string Title { get; }

        public string KindLabel { get; }

        public string Body { get; }

        /// <summary>The action's label while it can be visited; null once found.</summary>
        public string Action { get; }

        private string Found { get; }

        public bool CanVisit
        {
            get { return Poi != null && State == PoiState.Revealed && Action != null; }
        }

        /// <summary>A point's display name.</summary>
        public static string NameOf(GameSession session, PointOfInterest poi)
        {
            DiscoveryLibrary library = session.Content.Discovery.Library;
            switch (poi.Kind)
            {
                case PoiKind.Shrine:
                    return library.Shrine(poi.RefId)?.Name ?? "Shrine";
                case PoiKind.LoreStone:
                    return library.Lore(poi.RefId)?.Title ?? "Lore stone";
                case PoiKind.Cache:
                    return library.Cache(poi.RefId)?.Name ?? "Cache";
                case PoiKind.KinshipSite:
                    return library.Site(poi.RefId)?.Name ?? "Kinship stone";
                default:
                    return "Vista";
            }
        }

        /// <summary>A cache's contents in words ("45 gold and 1 Essence Shard").</summary>
        public static string CacheText(GameSession session, CacheData cache)
        {
            List<string> parts = new List<string>();
            if (cache == null)
            {
                return "nothing";
            }

            if (cache.Gold > 0)
            {
                parts.Add(cache.Gold + " gold");
            }

            foreach (CacheMaterialData material in cache.Materials ?? new CacheMaterialData[0])
            {
                string name = Array.Find(session.Content.SkillLibrary.Materials ?? new Skills.SkillMaterialData[0], m => m.MaterialId == material.MaterialId)?.DisplayName ??
                              material.MaterialId;
                parts.Add(material.Quantity + " " + name + (material.Quantity == 1 ? string.Empty : "s"));
            }

            if (!string.IsNullOrEmpty(cache.Look))
            {
                parts.Add("a new look, the " + session.LookName(cache.Look));
            }

            return parts.Count == 0 ? "nothing" : string.Join(" and ", parts);
        }

        /// <summary>Visits the point (<see cref="GameSession.VisitPoi"/>); returns the toast to show (why, when refused).</summary>
        public string Visit(out DiscoveryResult result)
        {
            result = _session.VisitPoi(Poi?.PoiId);
            if (!result.Success)
            {
                return result.Error;
            }

            switch (Poi.Kind)
            {
                case PoiKind.Shrine:
                    return Title + " found. It will remember you when your Grove grows.";
                case PoiKind.LoreStone:
                    return "Lore found: " + Title + ".";
                case PoiKind.Cache:
                    return "You found " + CacheText(_session, _session.Content.Discovery.Library.Cache(result.CacheId)) + ".";
                case PoiKind.Vista:
                    return "From up here you can see the land around: the map is clearer.";
                default:
                    return "Every beast here already walks with you. The stone gives you an offering instead: " +
                           CacheText(_session, _session.Content.Discovery.Library.Cache(result.CacheId)) + ".";
            }
        }
    }

    /// <summary>One stage of the region progress panel.</summary>
    public sealed class StageProgressView
    {
        public int Stage;
        public string Label;

        /// <summary>Map rows walked of the stage's rows.</summary>
        public int RowsWalked;

        public int Rows;
        public int PoisFound;
        public int PoisTotal;
        public bool IsCurrent;
        public bool Cleared;

        /// <summary>Whether the stage can be revisited (reached, not the one on the map now).</summary>
        public bool CanRevisit;
    }

    /// <summary>
    /// The region progress panel (the map header's "Explored" chip): the region's exploration, stage by
    /// stage — rows walked, points of interest found — its 100% reward, and a way to revisit a stage already
    /// reached to explore what the fog still hides (<see cref="GameSession.ReplayStage"/>).
    /// </summary>
    public sealed class RegionProgressViewModel
    {
        private readonly GameSession _session;

        public RegionProgressViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            MapRun run = session.Save.Campaign.ActiveRun;
            RegionData region = session.Content.Campaign.GetRegion(run.RegionId);
            RegionProgress progress = session.Save.Campaign.FindRegion(run.RegionId);
            Completion = DiscoveryRules.Completion(session.Save, session.Content.Discovery, run.RegionId);
            Name = region?.DisplayName ?? run.RegionId;
            RegionDiscoveryData data = session.Content.Discovery.Library.Region(run.RegionId);
            if (Completion == null || region == null || progress == null)
            {
                return;
            }

            Reward = (data == null || string.IsNullOrEmpty(data.CompletionLook) ? string.Empty : "the " + session.LookName(data.CompletionLook)) +
                     (data != null && data.CompletionGold > 0 ? " and " + data.CompletionGold + " gold" : string.Empty);
            int layers = session.Content.Campaign.RulesFor(region).Layers;
            int next = CampaignRules.NextStage(session.Save, session.Content.Campaign, run.RegionId);
            List<PointOfInterest> points = DiscoveryRules.PointsOf(session.Save, session.Content.Discovery, run.RegionId);
            for (int stage = 0; stage < region.Stages; stage++)
            {
                bool cleared = progress.BossCleared || stage < progress.StagesCleared;
                StageFog fog = progress.FindFog(stage);
                List<PointOfInterest> mine = points.FindAll(p => p.Stage == stage);
                Stages.Add(new StageProgressView
                {
                    Stage = stage,
                    Label = "Stage " + (stage + 1),
                    Rows = layers,
                    RowsWalked = cleared ? layers : Math.Min(layers, (fog == null ? -1 : fog.DeepestLayer) + 1),
                    PoisTotal = mine.Count,
                    PoisFound = mine.FindAll(p => progress.HasFound(p.PoiId)).Count,
                    IsCurrent = stage == run.Stage,
                    Cleared = cleared,
                    CanRevisit = stage != run.Stage && stage <= next
                });
            }
        }

        public string Name { get; }

        public RegionCompletion Completion { get; }

        /// <summary>The 100% reward in words ("the Mosstrail Cape and 60 gold").</summary>
        public string Reward { get; } = string.Empty;

        public List<StageProgressView> Stages { get; } = new List<StageProgressView>();

        /// <summary>Starts stage <paramref name="stage"/> again on a new map (the expedition in progress is left). False when refused.</summary>
        public bool Revisit(int stage)
        {
            CampaignResult result = _session.ReplayStage(stage);
            return result != null && result.Success;
        }
    }
}
