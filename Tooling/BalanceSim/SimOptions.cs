using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>Which element the standard kit is authored in for a run.</summary>
    public enum KitMode
    {
        /// <summary>Every kit skill carries the unit's own (first) element.</summary>
        Elemental = 0,

        /// <summary>Every kit skill is <c>Element.None</c>: isolates stat distribution from the chart.</summary>
        Neutral = 1
    }

    /// <summary>
    /// Every tunable the simulator has, in one place. The constants are the defaults; the CLI
    /// overrides the subset exposed by <see cref="Parse"/>.
    /// </summary>
    public class SimOptions
    {
        // ------------------------------------------------------------------------------------
        // Standard beast kit. The roster has no authored skills yet, so every beast fights with
        // this; every beast has the same kit so the stat lines are what gets measured.
        //
        // Fairness between Attack and SpecialAttack (the baseline's bias: Strike at cooldown 1
        // against Blast at cooldown 2 weighted Attack about twice as heavily):
        //   - Strike (Physical) and Blast (Special) have the same cooldown, 1, so once a beast is
        //     in melee both fire every turn.
        //   - Range is the one asymmetry the brief fixes (Strike 1, Blast 3): Strike needs the
        //     beast to reach a free tile next to its target, so it fires less often — while
        //     closing, and when the target is crowded. Measured over the default PvE run with
        //     equal power 40, under the Runtime's current movement rules (defeated units leave the
        //     grid, partial approach) and the fixtures' current move ranges, Strike fired 0.73x as
        //     often as Blast (0.70 boss, 0.75-0.77 swarm, 0.72-0.76 pack). Strike's power is
        //     therefore 55 (= 40 / 0.731, rounded), so fires x power, and with it the weight of
        //     Attack vs SpecialAttack, is even: the report's kit parity table shows the physical
        //     share of single-target power (49-51% per encounter, 50.1% overall, at the defaults;
        //     54 gave 49.7%). Re-derive StrikePower if the kit, the fixtures or the movement rules
        //     change.
        //   - The AoE ("Burst") is split into a Physical half and a Special half with the same
        //     power, radius and cooldown, fired together, physical first. A single-category Burst
        //     would re-open the bias this kit exists to close. Firing order does not bias the
        //     outcome: a target dies iff the two halves' damage together reaches its HP, whichever
        //     half lands the blow.
        // Burst (2 x power 20, radius 2, cooldown 2) is lower power and longer cooldown than the
        // single-target pair: a periodic spike that only pays off when several enemies are close,
        // i.e. against swarms and packs. Cooldown 2 rather than 3 so it comes up on the beast's
        // second turn — fights at calibrated difficulty last a handful of turns per beast, and at
        // cooldown 3 the swarm was mostly dead before the first Burst (measured under the old
        // round-based turn order; cooldowns count the beast's own turns under ATB as well).
        //
        // AreaBurst semantics (SkillTargetResolver): the disc of radius Range around the caster's
        // own tile at the moment it fires; it never moves the caster. A Burst that catches nobody
        // still fires and re-arms (battle-system.md, decision 7). It is last in the fire order so
        // it goes off from the tile Strike just walked the beast to.
        // ------------------------------------------------------------------------------------
        public const string BlastId = "sim_blast";
        public const DamageCategory BlastCategory = DamageCategory.Special;
        public const float BlastPower = 40f;
        public const int BlastRange = 3;
        public const int BlastCooldown = 1;

        public const string StrikeId = "sim_strike";
        public const DamageCategory StrikeCategory = DamageCategory.Physical;
        public const float StrikePower = 55f;
        public const int StrikeRange = 1;
        public const int StrikeCooldown = BlastCooldown;

        public const string BurstPhysicalId = "sim_burst_physical";
        public const string BurstSpecialId = "sim_burst_special";
        public const float BurstPower = 20f;
        public const int BurstRadius = 2;
        public const int BurstCooldown = 2;

        // ------------------------------------------------------------------------------------
        // 1v1 (PvP) setup.
        // ------------------------------------------------------------------------------------
        public const ArenaSize PvpArena = ArenaSize.Medium;

        /// <summary>Unit ids per side. Initiative ties break on ordinal id, so every pairing is also run side-swapped.</summary>
        public const string PlayerUnitId = "p";
        public const string EnemyUnitId = "e";

        // ------------------------------------------------------------------------------------
        // PvE (team vs encounter) setup.
        // ------------------------------------------------------------------------------------
        public const int DefaultTeamSize = 4;
        public const int MaxTeamSize = 6;
        public const double DefaultTargetClearRate = 50.0;

        /// <summary>Calibration: the multiplier starts at 1 and doubles/halves until it brackets the target, within these bounds.</summary>
        public const double MinMultiplier = 1.0 / 64.0;
        public const double MaxMultiplier = 64.0;

        /// <summary>Calibration: bisection steps once the target is bracketed.</summary>
        public const int CalibrationBisections = 8;

        /// <summary>A calibrated clear rate further than this from the target is reported as a calibration miss.</summary>
        public const double CalibrationTolerance = 10.0;

        /// <summary>How many places count as "top" / "bottom" for the niche flags.</summary>
        public const int NicheBand = 3;

        // ------------------------------------------------------------------------------------
        // Report thresholds (PvP).
        // ------------------------------------------------------------------------------------
        public const double HighWinRate = 60.0;
        public const double LowWinRate = 40.0;
        public const double MaxLevelSwing = 25.0;

        // ------------------------------------------------------------------------------------
        // CLI defaults.
        // ------------------------------------------------------------------------------------
        public static readonly int[] DefaultLevels = { 1, 50, 100 };
        public const int DefaultMatrixLevel = 50;
        public const int DefaultSeed = 12345;
        public const double DefaultMarginalThreshold = 5.0;

        public List<int> Levels = new List<int>(DefaultLevels);
        public List<KitMode> Modes = new List<KitMode> { KitMode.Elemental, KitMode.Neutral };
        public bool RunPve = true;
        public bool RunPvp = true;
        public int TeamSize = DefaultTeamSize;
        public List<string> EncounterFilter;
        public double MarginalThreshold = DefaultMarginalThreshold;
        public double TargetClearRate = DefaultTargetClearRate;

        /// <summary>Null = the elements authored in encounters.json; otherwise every enemy gets this element.</summary>
        public Element? EnemyElementOverride;

        public int MaxTime = BattleTurnExecutor.DefaultMaxTime;
        public int Seed = DefaultSeed;
        public int MatrixLevel = DefaultMatrixLevel;
        public string OutPath;
        public string RosterPath;
        public string EncountersPath;
        public bool SelfCheck;
        public bool ShowHelp;

        public const string Usage =
            "Beast Craft headless balance simulator (local-only tooling).\n" +
            "\n" +
            "Usage: dotnet run --project Tooling/BalanceSim -c Release -- [options]\n" +
            "\n" +
            "  --mode <m>                 pve | pvp | both (default both). pve = team vs encounter (primary);\n" +
            "                             pvp = the 1v1 round-robin (secondary).\n" +
            "  --kit <k>                  elemental | neutral | both (default both).\n" +
            "  --levels <list>            Comma-separated levels (default 1,50,100).\n" +
            "  --encounters <list>        Comma-separated encounter ids from encounters.json (default all).\n" +
            "  --team-size <n>            Beasts per player team, 1-6 (default 4); every combination is fielded.\n" +
            "  --target-clear <pct>       Clear rate the difficulty calibration aims for (default 50).\n" +
            "  --marginal-threshold <x>   Flag a beast whose overall marginal clear rate is outside +/-x points (default 5).\n" +
            "  --enemy-element <e>        authored | None | <Element> (default authored): override every enemy's element.\n" +
            "  --max-time <n>             Battle-time cap before a battle is a stalemate, in turns of a Speed-100 unit (default 2000).\n" +
            "  --seed <n>                 Base seed; each battle derives its own (default 12345).\n" +
            "  --matrix-level <n>         Level the PvP win matrix and stat table are drawn at (default 50, else the highest level).\n" +
            "  --roster <path>            beast-roster.json (default: found by walking up from the working directory).\n" +
            "  --encounters-file <path>   encounters.json (default: Tooling/BalanceSim/encounters.json, found the same way).\n" +
            "  --out <path>               Also write the Markdown report to this file.\n" +
            "  --self-check               Run everything twice and fail unless both reports are identical; also checks the\n" +
            "                             PvE battle loop against BattleTurnExecutor.RunBattle.\n" +
            "  --help                     Show this text.\n";

        /// <summary>Parses the command line. Returns null and fills <paramref name="error"/> on bad input.</summary>
        public static SimOptions Parse(string[] args, out string error)
        {
            SimOptions options = new SimOptions();
            bool matrixLevelGiven = false;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string text;

                switch (arg)
                {
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                    case "--self-check":
                        options.SelfCheck = true;
                        break;
                    case "--levels":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseLevels(text, options.Levels, out error))
                        {
                            return null;
                        }

                        break;
                    case "--mode":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseSimMode(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--kit":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseKit(text, options.Modes, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounters":
                        if (!TryNext(args, ref i, arg, out text, out error))
                        {
                            return null;
                        }

                        options.EncounterFilter = new List<string>();
                        foreach (string part in text.Split(','))
                        {
                            string id = part.Trim();
                            if (id.Length > 0 && !options.EncounterFilter.Contains(id))
                            {
                                options.EncounterFilter.Add(id);
                            }
                        }

                        if (options.EncounterFilter.Count == 0)
                        {
                            error = "--encounters needs at least one encounter id.";
                            return null;
                        }

                        break;
                    case "--team-size":
                        if (!TryNextInt(args, ref i, arg, 1, out options.TeamSize, out error))
                        {
                            return null;
                        }

                        if (options.TeamSize > MaxTeamSize)
                        {
                            error = "--team-size must be between 1 and " + MaxTeamSize + " (the largest battle format).";
                            return null;
                        }

                        break;
                    case "--target-clear":
                        if (!TryNextDouble(args, ref i, arg, out options.TargetClearRate, out error))
                        {
                            return null;
                        }

                        if (options.TargetClearRate <= 0.0 || options.TargetClearRate >= 100.0)
                        {
                            error = "--target-clear expects a percentage strictly between 0 and 100.";
                            return null;
                        }

                        break;
                    case "--marginal-threshold":
                        if (!TryNextDouble(args, ref i, arg, out options.MarginalThreshold, out error))
                        {
                            return null;
                        }

                        if (options.MarginalThreshold < 0.0)
                        {
                            error = "--marginal-threshold expects a non-negative number.";
                            return null;
                        }

                        break;
                    case "--enemy-element":
                        if (!TryNext(args, ref i, arg, out text, out error) || !TryParseEnemyElement(text, options, out error))
                        {
                            return null;
                        }

                        break;
                    case "--max-time":
                        if (!TryNextInt(args, ref i, arg, 1, out options.MaxTime, out error))
                        {
                            return null;
                        }

                        break;
                    case "--seed":
                        if (!TryNextInt(args, ref i, arg, int.MinValue, out options.Seed, out error))
                        {
                            return null;
                        }

                        break;
                    case "--matrix-level":
                        if (!TryNextInt(args, ref i, arg, 1, out options.MatrixLevel, out error))
                        {
                            return null;
                        }

                        matrixLevelGiven = true;
                        break;
                    case "--out":
                        if (!TryNext(args, ref i, arg, out options.OutPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--roster":
                        if (!TryNext(args, ref i, arg, out options.RosterPath, out error))
                        {
                            return null;
                        }

                        break;
                    case "--encounters-file":
                        if (!TryNext(args, ref i, arg, out options.EncountersPath, out error))
                        {
                            return null;
                        }

                        break;
                    default:
                        error = "Unknown argument '" + arg + "'. Use --help for usage.";
                        return null;
                }
            }

            if (!options.Levels.Contains(options.MatrixLevel))
            {
                if (matrixLevelGiven)
                {
                    error = "--matrix-level " + options.MatrixLevel + " is not one of the simulated levels.";
                    return null;
                }

                options.MatrixLevel = options.Levels[options.Levels.Count - 1];
            }

            return options;
        }

        private static bool TryNext(string[] args, ref int i, string flag, out string value, out string error)
        {
            if (i + 1 >= args.Length)
            {
                value = null;
                error = flag + " needs a value.";
                return false;
            }

            i++;
            value = args[i];
            error = null;
            return true;
        }

        private static bool TryNextInt(string[] args, ref int i, string flag, int minimum, out int value, out string error)
        {
            value = 0;
            if (!TryNext(args, ref i, flag, out string text, out error))
            {
                return false;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < minimum)
            {
                error = flag + " expects an integer >= " + minimum + ", got '" + text + "'.";
                return false;
            }

            return true;
        }

        private static bool TryNextDouble(string[] args, ref int i, string flag, out double value, out string error)
        {
            value = 0.0;
            if (!TryNext(args, ref i, flag, out string text, out error))
            {
                return false;
            }

            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsNaN(value) || double.IsInfinity(value))
            {
                error = flag + " expects a number, got '" + text + "'.";
                return false;
            }

            return true;
        }

        private static bool TryParseLevels(string text, List<int> levels, out string error)
        {
            levels.Clear();
            foreach (string part in text.Split(','))
            {
                if (!int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level < 1)
                {
                    error = "--levels expects comma-separated integers >= 1, got '" + text + "'.";
                    return false;
                }

                if (!levels.Contains(level))
                {
                    levels.Add(level);
                }
            }

            levels.Sort();
            error = null;
            return true;
        }

        private static bool TryParseSimMode(string text, SimOptions options, out string error)
        {
            error = null;
            switch (text.ToLowerInvariant())
            {
                case "pve":
                    options.RunPve = true;
                    options.RunPvp = false;
                    return true;
                case "pvp":
                    options.RunPve = false;
                    options.RunPvp = true;
                    return true;
                case "both":
                    options.RunPve = true;
                    options.RunPvp = true;
                    return true;
                default:
                    error = "--mode expects pve, pvp or both, got '" + text + "'.";
                    return false;
            }
        }

        private static bool TryParseKit(string text, List<KitMode> modes, out string error)
        {
            modes.Clear();
            error = null;

            switch (text.ToLowerInvariant())
            {
                case "elemental":
                    modes.Add(KitMode.Elemental);
                    return true;
                case "neutral":
                    modes.Add(KitMode.Neutral);
                    return true;
                case "both":
                    modes.Add(KitMode.Elemental);
                    modes.Add(KitMode.Neutral);
                    return true;
                default:
                    error = "--kit expects elemental, neutral or both, got '" + text + "'.";
                    return false;
            }
        }

        private static bool TryParseEnemyElement(string text, SimOptions options, out string error)
        {
            error = null;
            if (string.Equals(text, "authored", StringComparison.OrdinalIgnoreCase))
            {
                options.EnemyElementOverride = null;
                return true;
            }

            if (Enum.TryParse(text, true, out Element element) && Enum.IsDefined(typeof(Element), element))
            {
                options.EnemyElementOverride = element;
                return true;
            }

            error = "--enemy-element expects authored or an element name (None, Fire, Water, ...), got '" + text + "'.";
            return false;
        }

        public static string ModeName(KitMode mode)
        {
            return mode == KitMode.Elemental ? "elemental" : "neutral";
        }

        public static string Format(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>Signed, one decimal: "+3.2", "-0.4", "0.0".</summary>
        public static string Signed(double value)
        {
            string text = Format(value);
            if (text == "0.0" || text == "-0.0")
            {
                return "0.0";
            }

            return value > 0 ? "+" + text : text;
        }

        public static string FormatMultiplier(double value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public static string Join(IEnumerable<int> values)
        {
            List<string> parts = new List<string>();
            foreach (int value in values)
            {
                parts.Add(value.ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
        }

        public static BattleFormat FormatForTeamSize(int size)
        {
            if (size <= BattleFormat.Solo.MaxPartySize())
            {
                return BattleFormat.Solo;
            }

            return size <= BattleFormat.SmallGroup.MaxPartySize() ? BattleFormat.SmallGroup : BattleFormat.LargeGroup;
        }
    }
}
