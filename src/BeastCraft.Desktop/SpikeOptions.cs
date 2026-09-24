using System;
using System.Globalization;
using BeastCraft.Presentation.Content;

namespace BeastCraft.Desktop
{
    /// <summary>
    /// The command line.
    /// <code>
    ///   --screenshot PATH   render one frame to PATH (PNG) and exit, after:
    ///   --turns N           playing N turns (the Nth is the one shown; default 1)
    ///   --skill ID          ...then on, up to the first turn that fires skill ID (e.g. ember_shot)
    ///   --at MS             show that turn MS into its animation (default: its first matching
    ///                       beat mid-VFX: after the hit-stop, part-way through the flipbook)
    ///   --scale K           screenshot scale of the 640x360 frame (default 2)
    ///   --seed S            the battle's seed (default DemoBattle.DefaultSeed)
    ///   --level L           the team's level (default DemoBattle.DefaultLevel)
    ///   --enemy-level L     the encounter's level (default DemoBattle.DefaultEncounterLevel)
    ///   --content DIR       the content root (default: Content/ beside the app, or the repo's)
    /// </code>
    /// </summary>
    public sealed class SpikeOptions
    {
        public string ScreenshotPath;
        public int Turns = 1;
        public string Skill;
        public int? AtMs;
        public int Scale = 2;
        public int Seed = DemoBattle.DefaultSeed;
        public int Level = DemoBattle.DefaultLevel;
        public int EnemyLevel = DemoBattle.DefaultEncounterLevel;
        public string ContentRoot;

        public bool Screenshot
        {
            get { return !string.IsNullOrEmpty(ScreenshotPath); }
        }

        public static SpikeOptions Parse(string[] args, out string error)
        {
            SpikeOptions options = new SpikeOptions();
            error = null;
            for (int i = 0; i < args.Length; i++)
            {
                string flag = args[i];
                string value = i + 1 < args.Length ? args[i + 1] : null;
                switch (flag)
                {
                    case "--screenshot":
                        options.ScreenshotPath = value;
                        i++;
                        break;
                    case "--turns":
                        options.Turns = Int(value, flag, 1, ref error);
                        i++;
                        break;
                    case "--skill":
                        options.Skill = value;
                        i++;
                        break;
                    case "--at":
                        options.AtMs = Int(value, flag, 0, ref error);
                        i++;
                        break;
                    case "--scale":
                        options.Scale = Int(value, flag, 1, ref error);
                        i++;
                        break;
                    case "--seed":
                        options.Seed = Int(value, flag, int.MinValue, ref error);
                        i++;
                        break;
                    case "--level":
                        options.Level = Int(value, flag, 1, ref error);
                        i++;
                        break;
                    case "--enemy-level":
                        options.EnemyLevel = Int(value, flag, 1, ref error);
                        i++;
                        break;
                    case "--content":
                        options.ContentRoot = value;
                        i++;
                        break;
                    default:
                        error = "Unknown argument '" + flag + "'.";
                        break;
                }

                if (error != null)
                {
                    return null;
                }
            }

            if (options.Screenshot && options.ScreenshotPath == null)
            {
                error = "--screenshot needs a path.";
                return null;
            }

            return options;
        }

        private static int Int(string value, string flag, int min, ref string error)
        {
            if (value == null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed < min)
            {
                error = flag + " needs a whole number" + (min > int.MinValue ? " of at least " + min : string.Empty) + ".";
                return 0;
            }

            return parsed;
        }
    }
}
