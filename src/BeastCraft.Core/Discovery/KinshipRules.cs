using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Progression;
using BeastCraft.Save;
using BeastCraft.Tutorial;

namespace BeastCraft.Discovery
{
    /// <summary>What a Kinship call did.</summary>
    public enum KinshipOutcome
    {
        Refused,

        /// <summary>The trial was lost: nothing changes but the retry count; try again any time.</summary>
        Lost,

        /// <summary>The trial was won: a choice of beasts waits (<see cref="DiscoveryProgress.PendingKinshipId"/>).</summary>
        Won,

        /// <summary>A beast was chosen and joined.</summary>
        Joined
    }

    /// <summary>The result of a <see cref="KinshipRules"/> call.</summary>
    public sealed class KinshipResult
    {
        public KinshipOutcome Outcome;

        public string Error;

        public KinshipSiteData Site;

        public PointOfInterest Poi;

        /// <summary>After a win (and on a pending choice): the species offered, one or two, the region's theme first.</summary>
        public List<string> Offer = new List<string>();

        /// <summary>The level the chosen beast joins at.</summary>
        public int JoinLevel;

        /// <summary>After a win: whether the site's optional bond condition was met (flavour only).</summary>
        public bool BondMet;

        /// <summary>The beast that joined.</summary>
        public OwnedBeast Beast;

        public bool Success
        {
            get { return Outcome != KinshipOutcome.Refused; }
        }

        internal static KinshipResult Refused(string error)
        {
            return new KinshipResult { Outcome = KinshipOutcome.Refused, Error = error };
        }
    }

    /// <summary>
    /// Kinship (producer decisions, 2026-09-27; Hearthglen gives the one-per-stance trio): seven sites
    /// across the first six regions (<c>discovery.json</c> <c>KinshipSites</c>; two in the sixth), each a
    /// point of interest with a fixed trial. Winning it lets the player choose one of two beasts they
    /// do not own yet (<see cref="Offer"/>: the site's <see cref="KinshipSiteData.Preferred"/> order —
    /// the region's theme first — then the roster's), which joins at the fielded team's mean level
    /// minus <see cref="JoinLevelsBelow"/> (at least 1), knowing its species' default loadout at skill
    /// level 1. A loss changes nothing but the retry seed. Ten species, three owned after Hearthglen
    /// and seven sites: each claim takes one of the seven unowned, so every beast is owned once every
    /// site is claimed, whatever the trio and whichever of the two is chosen (the last site offers the
    /// last beast alone). A site never offers an owned beast; one with nothing left (a save that owned
    /// beasts before Kinship existed) is a lore and cache stop instead (<see cref="DiscoveryRules.Visit"/>).
    /// Non-throwing; a refused call changes nothing.
    /// </summary>
    public static class KinshipRules
    {
        /// <summary>How many beasts a site offers (fewer when fewer are left unowned).</summary>
        public const int OfferSize = 2;

        /// <summary>How many levels below the fielded team's mean a recruit joins (producer decision: about 3).</summary>
        public const int JoinLevelsBelow = 3;

        /// <summary>
        /// The species <paramref name="site"/> offers a player who owns <paramref name="owned"/>: the
        /// first <see cref="OfferSize"/> not owned in the site's preferred order, then
        /// <paramref name="rosterOrder"/>'s. Pure and deterministic.
        /// </summary>
        public static List<string> Offer(KinshipSiteData site, ICollection<string> owned, IReadOnlyList<string> rosterOrder)
        {
            List<string> offer = new List<string>();
            List<string> order = new List<string>(site == null ? new string[0] : site.Preferred ?? new string[0]);
            foreach (string id in rosterOrder ?? new string[0])
            {
                order.Add(id);
            }

            foreach (string id in order)
            {
                if (offer.Count >= OfferSize)
                {
                    break;
                }

                if (!string.IsNullOrEmpty(id) && !offer.Contains(id) && (owned == null || !owned.Contains(id)) && Contains(rosterOrder, id))
                {
                    offer.Add(id);
                }
            }

            return offer;
        }

        /// <summary><see cref="Offer(KinshipSiteData, ICollection{string}, IReadOnlyList{string})"/> for the species <paramref name="save"/> owns.</summary>
        public static List<string> Offer(PlayerSave save, KinshipSiteData site, IReadOnlyList<CreatureSpeciesSO> roster)
        {
            return Offer(site, OwnedSpecies(save), RosterOrder(roster));
        }

        /// <summary>The species ids of <paramref name="roster"/>, in roster order.</summary>
        public static List<string> RosterOrder(IReadOnlyList<CreatureSpeciesSO> roster)
        {
            List<string> ids = new List<string>();
            foreach (CreatureSpeciesSO species in roster ?? new CreatureSpeciesSO[0])
            {
                if (species != null && !string.IsNullOrEmpty(species.SpeciesId))
                {
                    ids.Add(species.SpeciesId);
                }
            }

            return ids;
        }

        /// <summary>The species <paramref name="save"/> owns.</summary>
        public static HashSet<string> OwnedSpecies(PlayerSave save)
        {
            HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
            foreach (OwnedBeast beast in save == null || save.Beasts == null ? new List<OwnedBeast>() : save.Beasts)
            {
                if (beast != null && beast.Progress != null && !string.IsNullOrEmpty(beast.Progress.SpeciesId))
                {
                    owned.Add(beast.Progress.SpeciesId);
                }
            }

            return owned;
        }

        /// <summary>
        /// The level a recruit joins at: the mean level of <paramref name="team"/> (the beasts that
        /// fought the trial; every owned beast when none is named), rounded down, minus
        /// <see cref="JoinLevelsBelow"/>, at least 1.
        /// </summary>
        public static int JoinLevel(PlayerSave save, IReadOnlyList<string> team)
        {
            long sum = 0;
            int count = 0;
            foreach (OwnedBeast beast in save == null || save.Beasts == null ? new List<OwnedBeast>() : save.Beasts)
            {
                if (beast != null && beast.Progress != null && (team == null || team.Count == 0 || Contains(team, beast.BeastId)))
                {
                    sum += beast.Progress.Level;
                    count++;
                }
            }

            return Math.Max(1, (count == 0 ? 1 : (int)(sum / count)) - JoinLevelsBelow);
        }

        /// <summary>The site a Kinship point of interest is.</summary>
        public static KinshipSiteData SiteOf(DiscoveryContent content, PointOfInterest poi)
        {
            return content == null || poi == null || poi.Kind != PoiKind.KinshipSite ? null : content.Library.Site(poi.RefId);
        }

        /// <summary>The trial <paramref name="poi"/> fields: its site's template at the point's level (the template's own difficulty; never eased).</summary>
        public static EncounterPlan PlanFor(DiscoveryContent content, PointOfInterest poi)
        {
            KinshipSiteData site = SiteOf(content, poi);
            return site == null ? null : EncounterPlan.FromTemplate(content.Encounters, content.Enemies, site.TemplateId, poi.Level);
        }

        /// <summary>The battle seed of a trial attempt: <c>DeriveSeed(poi.Seed, losses so far)</c>.</summary>
        public static int BattleSeed(PointOfInterest poi, int losses)
        {
            return LootRoller.DeriveSeed(poi == null ? 0 : poi.Seed, Math.Max(0, losses));
        }

        /// <summary>
        /// Whether the trial at <paramref name="poiId"/> can be fought now: the point is on the map of the
        /// expedition in progress, seen, its site not claimed, a beast left to offer, and no other
        /// choice pending. Null with <paramref name="error"/> otherwise.
        /// </summary>
        public static PointOfInterest Challengeable(PlayerSave save, DiscoveryContent content, string poiId, out KinshipSiteData site, out string error)
        {
            site = null;
            PointOfInterest poi = DiscoveryRules.Visitable(save, content, poiId, out error);
            if (poi == null)
            {
                return null;
            }

            site = SiteOf(content, poi);
            if (site == null)
            {
                error = "There is no trial here.";
                return null;
            }

            if (save.Discovery.HasPendingKinship)
            {
                error = "A beast is waiting to join you: choose it first.";
                return null;
            }

            if (save.Discovery.HasClaimed(site.SiteId))
            {
                error = "You have already bonded here.";
                return null;
            }

            if (Offer(save, site, content.Roster).Count == 0)
            {
                error = "No beast is left to answer here.";
                return null;
            }

            return poi;
        }

        /// <summary>
        /// Records the trial fought at <paramref name="poiId"/> by <paramref name="team"/>: a loss counts
        /// toward the retry seed and changes nothing else; a win sets the pending choice (the site, and the
        /// join level from the team, <see cref="JoinLevel"/>) and returns the offer and whether the bond
        /// condition was met (<paramref name="knockedOut"/>: any fielded beast knocked out).
        /// </summary>
        public static KinshipResult ResolveTrial(PlayerSave save, DiscoveryContent content, string poiId, BattleOutcome outcome, IReadOnlyList<string> team,
                                                 bool knockedOut)
        {
            PointOfInterest poi = Challengeable(save, content, poiId, out KinshipSiteData site, out string error);
            if (poi == null)
            {
                return KinshipResult.Refused(error);
            }

            if (outcome != BattleOutcome.PlayerVictory)
            {
                save.Discovery.KinshipLosses++;
                return new KinshipResult { Outcome = KinshipOutcome.Lost, Site = site, Poi = poi };
            }

            int join = Math.Min(JoinLevel(save, team), Math.Max(1, CampaignRules.BeastCap(save, content.Regions)));
            save.Discovery.PendingKinshipId = site.SiteId;
            save.Discovery.PendingKinshipLevel = join;
            return new KinshipResult
            {
                Outcome = KinshipOutcome.Won,
                Site = site,
                Poi = poi,
                Offer = Offer(save, site, content.Roster),
                JoinLevel = join,
                BondMet = BondMet(site, TeamSpecies(save, content, team), knockedOut)
            };
        }

        /// <summary>The pending choice (after a won trial): its site, offer and join level; refused when none is pending.</summary>
        public static KinshipResult Pending(PlayerSave save, DiscoveryContent content)
        {
            KinshipSiteData site = save == null || content == null || !save.Discovery.HasPendingKinship ? null : content.Library.Site(save.Discovery.PendingKinshipId);
            if (site == null)
            {
                return KinshipResult.Refused("No beast is waiting to join.");
            }

            return new KinshipResult
            {
                Outcome = KinshipOutcome.Won,
                Site = site,
                Poi = PointOfSite(save, content, site),
                Offer = Offer(save, site, content.Roster),
                JoinLevel = Math.Max(1, save.Discovery.PendingKinshipLevel)
            };
        }

        /// <summary>
        /// Makes the pending choice: <paramref name="speciesId"/> (one of the offer) joins at the pending
        /// level knowing its species' default loadout at skill level 1; the site is claimed, its lore
        /// recorded and its point of interest found.
        /// </summary>
        public static KinshipResult Choose(PlayerSave save, DiscoveryContent content, string speciesId)
        {
            KinshipResult pending = Pending(save, content);
            if (!pending.Success)
            {
                return pending;
            }

            if (!pending.Offer.Contains(speciesId ?? string.Empty))
            {
                return KinshipResult.Refused("That beast is not offered here.");
            }

            OwnedBeast beast = StarterPicks.AddBeast(save, content.Skills, speciesId, pending.JoinLevel);
            DiscoveryProgress.AddOnce(save.Discovery.ClaimedKinshipIds, pending.Site.SiteId);
            DiscoveryProgress.AddOnce(save.Discovery.LoreIds, pending.Site.LoreId);
            if (pending.Poi != null)
            {
                save.Campaign.FindRegion(pending.Poi.RegionId)?.MarkFound(pending.Poi.PoiId);
            }

            save.Discovery.PendingKinshipId = string.Empty;
            save.Discovery.PendingKinshipLevel = 0;
            pending.Outcome = KinshipOutcome.Joined;
            pending.Beast = beast;
            return pending;
        }

        /// <summary>
        /// Whether a trial won by <paramref name="team"/> met <paramref name="site"/>'s optional bond
        /// condition (flavour only): <c>stance:{Stance}</c>, <c>element:{Element}</c>, <c>no_knockout</c>;
        /// no condition is always met.
        /// </summary>
        public static bool BondMet(KinshipSiteData site, IReadOnlyList<CreatureSpeciesSO> team, bool knockedOut)
        {
            string condition = site == null ? string.Empty : site.BondCondition ?? string.Empty;
            if (condition.Length == 0)
            {
                return true;
            }

            if (condition == "no_knockout")
            {
                return !knockedOut;
            }

            string[] parts = condition.Split(':');
            foreach (CreatureSpeciesSO species in team ?? new CreatureSpeciesSO[0])
            {
                if (species == null || parts.Length != 2)
                {
                    continue;
                }

                if (parts[0] == "stance" && species.Stance.ToString() == parts[1])
                {
                    return true;
                }

                if (parts[0] == "element" && Array.Exists(species.Elements ?? new Element[0], e => e.ToString() == parts[1]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The point of interest a site is, in the save's layout of its region (null before the region's first expedition).</summary>
        public static PointOfInterest PointOfSite(PlayerSave save, DiscoveryContent content, KinshipSiteData site)
        {
            if (site == null)
            {
                return null;
            }

            foreach (PointOfInterest point in DiscoveryRules.PointsOf(save, content, site.RegionId))
            {
                if (point.Kind == PoiKind.KinshipSite && point.RefId == site.SiteId)
                {
                    return point;
                }
            }

            return null;
        }

        private static List<CreatureSpeciesSO> TeamSpecies(PlayerSave save, DiscoveryContent content, IReadOnlyList<string> team)
        {
            List<CreatureSpeciesSO> species = new List<CreatureSpeciesSO>();
            foreach (string beastId in team ?? new string[0])
            {
                OwnedBeast beast = save.FindBeast(beastId);
                foreach (CreatureSpeciesSO candidate in content.Roster ?? new CreatureSpeciesSO[0])
                {
                    if (beast != null && candidate != null && candidate.SpeciesId == beast.Progress.SpeciesId)
                    {
                        species.Add(candidate);
                    }
                }
            }

            return species;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            foreach (string other in ids ?? new string[0])
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
