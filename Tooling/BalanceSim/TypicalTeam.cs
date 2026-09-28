using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeastCraft.Battle;
using BeastCraft.Battle.Scouting;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Discovery;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The owned-roster model and the typical-team measure the shipping difficulty is calibrated on
    /// (<see cref="TypicalCalibration"/>, <c>--mode typical</c>; producer decision: the targets are hit by a
    /// TYPICAL team, a sensible but not optimal pick from the beasts the player owns at that point).
    /// <para>
    /// <strong>Owned beasts</strong> (<see cref="BandAt"/>): the Kinship roster flow the campaign pacing model
    /// walks. The population is every one-per-stance trio of the roster (the Hearthglen trio: 30), each owning,
    /// at encounter level <c>L</c>, its trio plus one beast of every Kinship site whose trial level (its map row
    /// <see cref="TrialRow"/> plus its offset) is at most <c>L</c>. A site offers two beasts
    /// (<see cref="KinshipRules.Offer(KinshipSiteData, ICollection{string}, IReadOnlyList{string})"/>); trio
    /// <c>k</c> takes offer <c>(k + site) mod 2</c>, so half the population takes each choice at every site (the
    /// recruit choices mixed, deterministically). The trio fights at the node's level (the pacing model's
    /// fielded median sits there), each recruit <see cref="LagAt"/> levels below it (the campaign model's bench
    /// gap by region).
    /// </para>
    /// <para>
    /// <strong>Reasonable teams</strong> (<see cref="Reasonable"/>): three owned beasts covering at least two
    /// stances with at least one Vanguard. A team with no Vanguard has nothing to screen its Ranged and
    /// Skirmishers (the game's suggester never proposes one); a single-stance team gives up the stance
    /// system. Everything else is a pick a sensible player might bring.
    /// </para>
    /// <para>
    /// <strong>Typical team</strong>: per owned roster, the median-performing reasonable team (each team's
    /// clear rate over every composition of the shape, at its members' own levels; the median of the roster's
    /// teams, linear between the two middle ones for an even count). The typical rate is the mean over the
    /// population of each roster's median. Half the sensible picks from a roster do better, half worse, so the
    /// target is what a player who brings an ordinary team meets, with no scouting and no counter-pick. The
    /// weak pick is the roster's lower quartile (<see cref="WeakQuantile"/>) of the same teams; the scouted pick
    /// is the game's own <see cref="TeamSuggester"/> over what the roster owns, per composition (a lagging recruit
    /// weighed as the game weighs it).
    /// </para>
    /// </summary>
    public sealed class TypicalTeamModel
    {
        /// <summary>The weak-but-reasonable pick: this quantile of a roster's reasonable teams.</summary>
        public const double WeakQuantile = 0.25;

        /// <summary>The typical pick: the median of a roster's reasonable teams.</summary>
        public const double TypicalQuantile = 0.5;

        /// <summary>The map row a Kinship site's trial level is read at (as <see cref="NewPlayerReport.TrialRow"/>).</summary>
        public const int TrialRow = NewPlayerReport.TrialRow;

        /// <summary>Levels below the trio a recruit fights at, by mainline region order (r01, r02, ...; the last value after): the campaign model's bench gap at the boss.</summary>
        public static readonly int[] RecruitLag = NewPlayerReport.RecruitLag;

        private readonly IReadOnlyList<CreatureSpeciesSO> _species;
        private readonly List<int[]> _teams;
        private readonly Dictionary<string, int> _teamIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly RegionLibrary _regions;
        private readonly List<KinshipSiteData> _sites = new List<KinshipSiteData>();
        private readonly List<int> _siteLevels = new List<int>();
        private readonly List<string> _order = new List<string>();

        /// <param name="teams">The simulator's teams (<see cref="PveSimulator.Teams"/>, every simulator shares them).</param>
        public TypicalTeamModel(IReadOnlyList<CreatureSpeciesSO> species, List<int[]> teams, DiscoveryLibrary discovery, RegionLibrary regions)
        {
            _species = species;
            _teams = teams;
            _regions = regions;
            for (int t = 0; t < teams.Count; t++)
            {
                _teamIndex[Key(teams[t])] = t;
            }

            foreach (CreatureSpeciesSO s in species)
            {
                _order.Add(s.SpeciesId);
            }

            foreach (KinshipSiteData site in discovery.Sites)
            {
                RegionData region = regions.GetRegion(site.RegionId);
                if (region == null)
                {
                    continue;
                }

                _sites.Add(site);
                _siteLevels.Add(NodeMapGenerator.RowLevel(region, regions.RulesFor(region), site.Stage, TrialRow) + site.LevelOffset);
            }

            Trios = NewPlayerReport.Trios(teams, species);
        }

        /// <summary>The one-per-stance trios (team indices), the population's starting rosters.</summary>
        public int[] Trios { get; }

        /// <summary>The simulator's teams.</summary>
        public List<int[]> Teams
        {
            get { return _teams; }
        }

        /// <summary>Three beasts covering at least two stances, at least one of them a Vanguard.</summary>
        public static bool Reasonable(int[] team, IReadOnlyList<CreatureSpeciesSO> species)
        {
            HashSet<CombatStance> stances = new HashSet<CombatStance>();
            int vanguards = 0;
            foreach (int s in team)
            {
                stances.Add(species[s].Stance);
                vanguards += species[s].Stance == CombatStance.Vanguard ? 1 : 0;
            }

            return stances.Count >= 2 && vanguards >= 1;
        }

        /// <summary>The mainline region whose level band holds <paramref name="level"/> (the last one above it; null with none).</summary>
        public RegionData RegionAt(int level)
        {
            RegionData found = null;
            foreach (RegionData region in _regions.MainlineRegions())
            {
                if (found == null || level >= region.MinLevel)
                {
                    found = region;
                }

                if (level <= region.MaxLevel)
                {
                    break;
                }
            }

            return found;
        }

        /// <summary>Levels a recruit fights below the trio at <paramref name="level"/>: <see cref="RecruitLag"/> of its region.</summary>
        public int LagAt(int level)
        {
            List<RegionData> mainline = _regions.MainlineRegions();
            RegionData region = RegionAt(level);
            int index = Math.Max(0, mainline.IndexOf(region));
            return RecruitLag[Math.Min(index, RecruitLag.Length - 1)];
        }

        /// <summary>The Kinship sites reached by level <paramref name="level"/> (trial level at most it), in campaign order.</summary>
        public int SitesReached(int level)
        {
            int count = 0;
            for (int j = 0; j < _sites.Count; j++)
            {
                if (_siteLevels[j] <= level)
                {
                    count = j + 1;
                }
            }

            return count;
        }

        /// <summary>
        /// The population at encounter level <paramref name="level"/>: every trio's roster with its reasonable
        /// teams. The trio fights at <paramref name="teamLevel"/> (0 = <paramref name="level"/>), the recruits
        /// <see cref="LagAt"/> below it (never below 1). <paramref name="sites"/> overrides how many Kinship
        /// sites have been claimed (-1 = <see cref="SitesReached"/>).
        /// </summary>
        public Band BandAt(int level, int teamLevel = 0, int sites = -1)
        {
            int trioLevel = teamLevel > 0 ? teamLevel : level;
            int lag = LagAt(level);
            int claimed = sites < 0 ? SitesReached(level) : Math.Min(sites, _sites.Count);
            Band band = new Band
            {
                Level = level,
                TeamLevel = trioLevel,
                RecruitLevel = Math.Max(1, trioLevel - lag),
                Lag = lag,
                Sites = claimed,
                Region = RegionAt(level)
            };

            Dictionary<long, int> entryOf = new Dictionary<long, int>();
            for (int k = 0; k < Trios.Length; k++)
            {
                int[] trio = _teams[Trios[k]];
                HashSet<string> owned = new HashSet<string>(StringComparer.Ordinal);
                foreach (int s in trio)
                {
                    owned.Add(_species[s].SpeciesId);
                }

                for (int j = 0; j < claimed; j++)
                {
                    List<string> offer = KinshipRules.Offer(_sites[j], owned, _order);
                    if (offer.Count > 0)
                    {
                        owned.Add(offer[(k + j) % offer.Count]);
                    }
                }

                Roster roster = new Roster { Trio = trio };
                for (int s = 0; s < _species.Count; s++)
                {
                    if (owned.Contains(_species[s].SpeciesId))
                    {
                        roster.Owned.Add(s);
                    }
                }

                HashSet<int> trioSet = new HashSet<int>(trio);
                foreach (int[] combo in TeamSuggester.Combinations(roster.Owned.Count, 3))
                {
                    int[] team = { roster.Owned[combo[0]], roster.Owned[combo[1]], roster.Owned[combo[2]] };
                    if (!Reasonable(team, _species))
                    {
                        continue;
                    }

                    roster.Entries.Add(EntryFor(band, band.Entries, entryOf, team, trioSet));
                }

                band.Rosters.Add(roster);
            }

            return band;
        }

        /// <summary>The index in <paramref name="entries"/> of <paramref name="team"/> (species indices, any order) with the trio's members at the trio's level.</summary>
        private int EntryFor(Band band, List<Entry> entries, Dictionary<long, int> entryOf, int[] team, HashSet<int> trio)
        {
            int[] sorted = (int[])team.Clone();
            Array.Sort(sorted);
            int t = _teamIndex[Key(sorted)];
            int mask = 0;
            int[] levels = new int[sorted.Length];
            for (int m = 0; m < sorted.Length; m++)
            {
                bool recruit = !trio.Contains(sorted[m]);
                mask |= recruit ? 1 << m : 0;
                levels[m] = recruit ? band.RecruitLevel : band.TeamLevel;
            }

            long key = ((long)t << 8) | (long)mask;
            if (!entryOf.TryGetValue(key, out int index))
            {
                index = entries.Count;
                entries.Add(new Entry { Team = t, Mask = mask, MemberLevels = levels });
                entryOf.Add(key, index);
            }

            return index;
        }

        /// <summary>
        /// Every entry of <paramref name="band"/> against every composition of <paramref name="shape"/>,
        /// <paramref name="samples"/> times, at <paramref name="multiplier"/> (elemental, the enemies at the band's
        /// level), then per roster its typical (median), weak (<see cref="WeakQuantile"/>) and best team.
        /// </summary>
        public Measure Evaluate(PveSimulator pve, Band band, EncounterShape shape, double multiplier, int samples)
        {
            int compositions = shape.Compositions.Count;
            int per = compositions * samples;
            bool[][] ties = Ties(pve, band, shape);
            int[] cleared = new int[band.Entries.Count * per];
            Parallel.For(0, cleared.Length, i =>
            {
                Entry entry = band.Entries[i / per];
                int c = (i % per) / samples;
                PveBattle battle = pve.RunBattle(KitMode.Elemental, band.TeamLevel, band.Level, shape.Compositions[c], multiplier, entry.Team, i % samples, false,
                                                 ties[c][entry.Team], true, entry.MemberLevels, 0, out _);
                cleared[i] = battle.Cleared ? 1 : 0;
            });

            double[] rates = new double[band.Entries.Count];
            for (int e = 0; e < rates.Length; e++)
            {
                int sum = 0;
                for (int j = 0; j < per; j++)
                {
                    sum += cleared[(e * per) + j];
                }

                rates[e] = (100.0 * sum) / per;
            }

            Measure measure = new Measure { Battles = cleared.Length, RosterTypical = new double[band.Rosters.Count] };
            measure.TypicalMin = double.MaxValue;
            measure.TypicalMax = double.MinValue;
            for (int r = 0; r < band.Rosters.Count; r++)
            {
                List<double> mine = new List<double>();
                foreach (int e in band.Rosters[r].Entries)
                {
                    mine.Add(rates[e]);
                }

                mine.Sort();
                double typical = Quantile(mine, TypicalQuantile);
                measure.RosterTypical[r] = typical;
                measure.Typical += typical / band.Rosters.Count;
                measure.Weak += Quantile(mine, WeakQuantile) / band.Rosters.Count;
                measure.Best += mine[mine.Count - 1] / band.Rosters.Count;
                measure.TypicalMin = Math.Min(measure.TypicalMin, typical);
                measure.TypicalMax = Math.Max(measure.TypicalMax, typical);
            }

            return measure;
        }

        /// <summary>
        /// The scouted pick: per roster and composition, the game's <see cref="TeamSuggester"/> over the roster's
        /// beasts at their levels (the trio at the band's team level, recruits below it; the suggester weighs the
        /// lag), <paramref name="samples"/> battles each at <paramref name="multiplier"/>. The mean over rosters,
        /// with the lowest and highest roster in <paramref name="min"/> / <paramref name="max"/>.
        /// </summary>
        public double EvaluateScouted(PveSimulator pve, SimOptions options, Band band, EncounterShape shape, double multiplier, int samples, out double min, out double max)
        {
            int compositions = shape.Compositions.Count;
            Dictionary<long, int> entryOf = new Dictionary<long, int>();
            List<Entry> entries = new List<Entry>();
            int[,] picks = new int[band.Rosters.Count, compositions];
            for (int c = 0; c < compositions; c++)
            {
                EncounterPreview preview = ScoutedPicker.Preview(shape.Compositions[c], options.ScoutedDetail);
                bool afflicts = ScoutedPicker.CanAfflict(shape.Compositions[c]);
                for (int r = 0; r < band.Rosters.Count; r++)
                {
                    Roster roster = band.Rosters[r];
                    HashSet<int> trio = new HashSet<int>(roster.Trio);
                    List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>();
                    foreach (int s in roster.Owned)
                    {
                        owned.Add(new TeamSuggestionCandidate(_species[s], trio.Contains(s) ? band.TeamLevel : band.RecruitLevel));
                    }

                    TeamSuggestion suggestion = TeamSuggester.Suggest(new TeamSuggestionRequest
                    {
                        Preview = preview,
                        Owned = owned,
                        TeamSize = 3,
                        MinVanguards = options.ScoutedVanguardMin,
                        Bonds = options.BondsActive ? options.Library.TeamBonds : null,
                        EncounterCanAfflict = afflicts
                    });

                    int[] team = new int[suggestion.Members.Count];
                    for (int m = 0; m < team.Length; m++)
                    {
                        team[m] = roster.Owned[suggestion.Members[m]];
                    }

                    picks[r, c] = EntryFor(band, entries, entryOf, team, trio);
                }
            }

            // Each (entry, composition) once, however many rosters pick it.
            int per = samples;
            Dictionary<long, int> slotOf = new Dictionary<long, int>();
            List<long> slots = new List<long>();
            for (int r = 0; r < band.Rosters.Count; r++)
            {
                for (int c = 0; c < compositions; c++)
                {
                    long key = ((long)picks[r, c] * compositions) + c;
                    if (!slotOf.ContainsKey(key))
                    {
                        slotOf.Add(key, slots.Count);
                        slots.Add(key);
                    }
                }
            }

            bool[][] ties = Ties(pve, band, shape);
            int[] cleared = new int[slots.Count * per];
            Parallel.For(0, cleared.Length, i =>
            {
                long key = slots[i / per];
                Entry entry = entries[(int)(key / compositions)];
                int c = (int)(key % compositions);
                PveBattle battle = pve.RunBattle(KitMode.Elemental, band.TeamLevel, band.Level, shape.Compositions[c], multiplier, entry.Team, i % per, false,
                                                 ties[c][entry.Team], true, entry.MemberLevels, 0, out _);
                cleared[i] = battle.Cleared ? 1 : 0;
            });

            double mean = 0.0;
            min = double.MaxValue;
            max = double.MinValue;
            for (int r = 0; r < band.Rosters.Count; r++)
            {
                int sum = 0;
                for (int c = 0; c < compositions; c++)
                {
                    int slot = slotOf[((long)picks[r, c] * compositions) + c];
                    for (int s = 0; s < per; s++)
                    {
                        sum += cleared[(slot * per) + s];
                    }
                }

                double rate = (100.0 * sum) / (compositions * per);
                mean += rate / band.Rosters.Count;
                min = Math.Min(min, rate);
                max = Math.Max(max, rate);
            }

            return mean;
        }

        private static bool[][] Ties(PveSimulator pve, Band band, EncounterShape shape)
        {
            bool[][] ties = new bool[shape.Compositions.Count][];
            for (int c = 0; c < ties.Length; c++)
            {
                ties[c] = pve.PlayersWinTies(KitMode.Elemental, band.TeamLevel, shape.Compositions[c].Id);
            }

            return ties;
        }

        /// <summary>The <paramref name="q"/> quantile of <paramref name="sorted"/> (ascending), linear between neighbours.</summary>
        public static double Quantile(List<double> sorted, double q)
        {
            if (sorted.Count == 0)
            {
                return 0.0;
            }

            double position = q * (sorted.Count - 1);
            int low = (int)Math.Floor(position);
            int high = Math.Min(sorted.Count - 1, low + 1);
            double t = position - low;
            return sorted[low] + (t * (sorted[high] - sorted[low]));
        }

        private static string Key(int[] team)
        {
            return string.Join(",", team);
        }

        /// <summary>One owned roster of the population.</summary>
        public sealed class Roster
        {
            /// <summary>The trio's species (ascending): the beasts at the team's level.</summary>
            public int[] Trio;

            /// <summary>Every owned species index, ascending.</summary>
            public List<int> Owned = new List<int>();

            /// <summary>The roster's reasonable teams, as indices into <see cref="Band.Entries"/>.</summary>
            public List<int> Entries = new List<int>();
        }

        /// <summary>A team at its members' levels (the trio's at the team level, a recruit's below it).</summary>
        public sealed class Entry
        {
            public int Team;

            /// <summary>Bit m set = member m (of <see cref="PveSimulator.Teams"/>[<see cref="Team"/>]) is a recruit.</summary>
            public int Mask;

            public int[] MemberLevels;
        }

        /// <summary>The population at one encounter level.</summary>
        public sealed class Band
        {
            /// <summary>The enemies' level (the node's).</summary>
            public int Level;

            /// <summary>The trio's level.</summary>
            public int TeamLevel;

            public int RecruitLevel;
            public int Lag;

            /// <summary>Kinship sites claimed (recruits owned).</summary>
            public int Sites;

            public RegionData Region;
            public List<Roster> Rosters = new List<Roster>();

            /// <summary>Every distinct (team, levels) any roster fields.</summary>
            public List<Entry> Entries = new List<Entry>();

            /// <summary>The fewest and most reasonable teams a roster has.</summary>
            public void TeamCounts(out int min, out int max)
            {
                min = int.MaxValue;
                max = 0;
                foreach (Roster roster in Rosters)
                {
                    min = Math.Min(min, roster.Entries.Count);
                    max = Math.Max(max, roster.Entries.Count);
                }
            }
        }

        /// <summary>One evaluation: means over the population's rosters.</summary>
        public sealed class Measure
        {
            /// <summary>Mean over rosters of the median reasonable team's clear rate: the calibrated number.</summary>
            public double Typical;

            /// <summary>Mean over rosters of the lower-quartile team's rate.</summary>
            public double Weak;

            /// <summary>Mean over rosters of the best reasonable team's rate (noise-inflated: the luckiest of the roster's teams).</summary>
            public double Best;

            public double TypicalMin;
            public double TypicalMax;
            public double[] RosterTypical;
            public int Battles;
        }
    }
    /// <summary>
    /// Simulators by (skill level, battlefield region, gear): each fields every beast and avatar skill at its
    /// skill level (its own <see cref="SkillLibraryKits"/>), stands on the region's battle layouts (none for a
    /// region without them, as the game) and wears the gear profile at the battle's level.
    /// </summary>
    public sealed class SimulatorCache
    {
        private readonly SimOptions _options;
        private readonly IReadOnlyList<CreatureSpeciesSO> _species;
        private readonly Dictionary<string, PveSimulator> _simulators = new Dictionary<string, PveSimulator>(StringComparer.Ordinal);
        private readonly Dictionary<int, SkillLibraryKits> _kits = new Dictionary<int, SkillLibraryKits>();

        public SimulatorCache(SimOptions options, IReadOnlyList<CreatureSpeciesSO> species)
        {
            _options = options;
            _species = species;
        }

        /// <summary>Why the last <see cref="Get"/> returned null (the skill library did not load).</summary>
        public List<string> Errors { get; } = new List<string>();

        public PveSimulator Get(int skillLevel, string battlefieldRegion, GearProfile gear)
        {
            string key = skillLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (battlefieldRegion ?? string.Empty) + "|" + (int)gear;
            if (_simulators.TryGetValue(key, out PveSimulator pve))
            {
                return pve;
            }

            if (!_kits.TryGetValue(skillLevel, out SkillLibraryKits kits))
            {
                kits = SkillLibraryKits.Load(SkillLibraryKits.ResolvePath(_options.SkillLibraryPath), RosterLoader.ResolvePath(_options.RosterPath), skillLevel, Errors);
                if (kits == null)
                {
                    return null;
                }

                _kits.Add(skillLevel, kits);
            }

            SimOptions copy = _options.ForSeed(_options.Seed);
            copy.SkillLevel = skillLevel;
            copy.Library = kits;
            copy.ObstaclesRegion = battlefieldRegion;
            pve = new PveSimulator(copy, _species);
            GearKits gearKits = _options.GearKits;
            pve.GearFor = gear == GearProfile.None || gearKits == null ? null : (s, l) => gearKits.For(s, l, gear);
            _simulators.Add(key, pve);
            return pve;
        }
    }
}
