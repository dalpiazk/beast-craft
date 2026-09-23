using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>Which element the standard kit is authored in for a run.</summary>
    public enum KitMode
    {
        /// <summary>Both kit skills carry the beast's own (first) element.</summary>
        Elemental = 0,

        /// <summary>Both kit skills are <c>Element.None</c>: isolates stat distribution from the chart.</summary>
        Neutral = 1
    }

    /// <summary>
    /// Every tunable the simulator has, in one place. The constants are the defaults; the CLI
    /// overrides the subset exposed by <see cref="Parse"/>.
    /// </summary>
    public class SimOptions
    {
        // ------------------------------------------------------------------------------------
        // Standard kit. The roster has no authored skills yet, so every beast fights with this.
        // Loadout order is fire priority: Blast is offered first, then Strike.
        // ------------------------------------------------------------------------------------
        public const string StrikeId = "sim_strike";
        public const DamageCategory StrikeCategory = DamageCategory.Physical;
        public const float StrikePower = 40f;
        public const int StrikeRange = 1;
        public const int StrikeCooldown = 1;

        public const string BlastId = "sim_blast";
        public const DamageCategory BlastCategory = DamageCategory.Special;
        public const float BlastPower = 40f;
        public const int BlastRange = 3;
        public const int BlastCooldown = 2;

        // ------------------------------------------------------------------------------------
        // Battle setup.
        // ------------------------------------------------------------------------------------
        public const ArenaSize Arena = ArenaSize.Medium;

        /// <summary>Unit ids per side. Speed ties break on ordinal id, so every pairing is also run side-swapped.</summary>
        public const string PlayerUnitId = "p";
        public const string EnemyUnitId = "e";

        // ------------------------------------------------------------------------------------
        // Report thresholds.
        // ------------------------------------------------------------------------------------
        public const double HighWinRate = 60.0;
        public const double LowWinRate = 40.0;
        public const double MaxLevelSwing = 25.0;

        // ------------------------------------------------------------------------------------
        // CLI defaults.
        // ------------------------------------------------------------------------------------
        public static readonly int[] DefaultLevels = { 1, 25, 50, 100 };
        public const int DefaultMatrixLevel = 50;
        public const int DefaultSeed = 12345;

        public List<int> Levels = new List<int>(DefaultLevels);
        public List<KitMode> Modes = new List<KitMode> { KitMode.Elemental, KitMode.Neutral };
        public int MaxRounds = BattleTurnExecutor.DefaultMaxRounds;
        public int Seed = DefaultSeed;
        public int MatrixLevel = DefaultMatrixLevel;
        public string OutPath;
        public string RosterPath;
        public bool SelfCheck;
        public bool ShowHelp;

        public const string Usage =
            "Beast Craft headless balance simulator (local-only tooling).\n" +
            "\n" +
            "Usage: dotnet run --project Tooling/BalanceSim -c Release -- [options]\n" +
            "\n" +
            "  --levels <list>      Comma-separated beast levels (default 1,25,50,100).\n" +
            "  --mode <m>           elemental | neutral | both (default both).\n" +
            "  --max-rounds <n>     Round cap before a battle is a stalemate (default 200).\n" +
            "  --seed <n>           Base seed; each battle derives its own (default 12345).\n" +
            "  --matrix-level <n>   Level the win matrix is drawn at (default 50, else the highest level).\n" +
            "  --roster <path>      beast-roster.json (default: found by walking up from the working directory).\n" +
            "  --out <path>         Also write the Markdown report to this file.\n" +
            "  --self-check         Run the whole simulation twice and fail unless both reports are identical.\n" +
            "  --help               Show this text.\n";

        /// <summary>Parses the command line. Returns null and fills <paramref name="error"/> on bad input.</summary>
        public static SimOptions Parse(string[] args, out string error)
        {
            SimOptions options = new SimOptions();
            bool matrixLevelGiven = false;
            error = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

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
                        if (!TryNext(args, ref i, arg, out string levels, out error) || !TryParseLevels(levels, options.Levels, out error))
                        {
                            return null;
                        }

                        break;
                    case "--mode":
                        if (!TryNext(args, ref i, arg, out string mode, out error) || !TryParseMode(mode, options.Modes, out error))
                        {
                            return null;
                        }

                        break;
                    case "--max-rounds":
                        if (!TryNextInt(args, ref i, arg, 1, out options.MaxRounds, out error))
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

        private static bool TryParseMode(string text, List<KitMode> modes, out string error)
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
                    error = "--mode expects elemental, neutral or both, got '" + text + "'.";
                    return false;
            }
        }

        public static string ModeName(KitMode mode)
        {
            return mode == KitMode.Elemental ? "elemental" : "neutral";
        }

        public static string Format(double value)
        {
            return value.ToString("0.0", CultureInfo.InvariantCulture);
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
    }
}
