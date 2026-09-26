using System;
using System.Globalization;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Save;

namespace BeastCraft.Game
{
    /// <summary>
    /// The command line.
    /// <code>
    ///   --screenshot PATH   render one frame to PATH (PNG) and exit, after:
    ///   --turns N           playing N turns (the Nth is the one shown; default 1; 0 = the opening
    ///                       board before any turn, in the fit-all view)
    ///   --skill ID          ...then on, up to the first turn that fires skill ID (e.g. ember_shot)
    ///   --at MS             show that turn MS into its animation (default: its first matching
    ///                       beat mid-VFX: after the hit-stop, part-way through the flipbook)
    ///   --scale K           screenshot size: K x 540x960, the portrait canvas at K/2 (default 2:
    ///                       1080x1920, the canvas 1:1)
    ///   --select-skill N    show the acting unit's Nth skill (0-based) selected, with its range diagram
    ///   --speed S           start at playback speed S (1-3; default 1)
    ///   --safe-inset L,T,R,B  pretend the screen has these safe-area insets (px), e.g. a notch
    ///   --seed S            the battle's seed (default DemoBattle.DefaultSeed)
    ///   --level L           the team's level (default DemoBattle.DefaultLevel)
    ///   --enemy-level L     the encounter's level (default DemoBattle.DefaultEncounterLevel)
    ///   --encounter ID      the encounter template to fight (default DemoBattle.DefaultEncounterId),
    ///                       or a shape id (e.g. horde) for a generated lineup drawn with the seed
    ///   --arena SIZE        fight on Small, Medium or Large instead of the encounter's own arena
    ///                       (the lineup must seat on it)
    ///   --team A,B,...      the team's species ids, 1 to 6 (default DemoBattle.DefaultTeam), e.g.
    ///                       to look at other beasts' art; which battle it is changes, the rules do not
    ///   --content DIR       the content root (default: Content/ beside the app, or the repo's)
    ///   --effects LEVEL     effects intensity: full, reduced or minimal (default: the saved
    ///                       setting; a screenshot uses full unless told)
    ///   --no-shake          screen shake off
    ///   --no-flashes        hit flashes and bright bursts off
    ///   --show-settings     open the settings overlay (for screenshots)
    ///   --glossary TERM     with --select-skill: open that glossary term's definition (a TermId
    ///                       or Term, e.g. burn)
    /// </code>
    /// In a screenshot the effects settings come from these flags only (the saved settings are
    /// neither read nor written); in a window they override the saved ones until changed there.
    /// </summary>
    public sealed class ViewerOptions
    {
        public string ScreenshotPath;
        public int Turns = 1;
        public string Skill;
        public int? AtMs;
        public int Scale = 2;
        public int SelectSkill = -1;
        public int Speed = 1;
        public SafeInsets SafeInsets = SafeInsets.None;
        public int Seed = DemoBattle.DefaultSeed;
        public int Level = DemoBattle.DefaultLevel;
        public int EnemyLevel = DemoBattle.DefaultEncounterLevel;
        public string Encounter = DemoBattle.DefaultEncounterId;
        public ArenaSize? Arena;
        public string[] Team;
        public string ContentRoot;
        public EffectsIntensity? Effects;
        public bool NoShake;
        public bool NoFlashes;
        public bool ShowSettings;
        public string Glossary;

        public bool Screenshot
        {
            get { return !string.IsNullOrEmpty(ScreenshotPath); }
        }

        public static ViewerOptions Parse(string[] args, out string error)
        {
            ViewerOptions options = new ViewerOptions();
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
                        options.Turns = Int(value, flag, 0, ref error);
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
                    case "--select-skill":
                        options.SelectSkill = Int(value, flag, 0, ref error);
                        i++;
                        break;
                    case "--speed":
                        options.Speed = Math.Min(3, Int(value, flag, 1, ref error));
                        i++;
                        break;
                    case "--safe-inset":
                        options.SafeInsets = Insets(value, ref error);
                        i++;
                        break;
                    case "--encounter":
                        options.Encounter = value;
                        i++;
                        break;
                    case "--arena":
                        if (Enum.TryParse(value ?? string.Empty, true, out ArenaSize arena) && Enum.IsDefined(typeof(ArenaSize), arena))
                        {
                            options.Arena = arena;
                        }
                        else
                        {
                            error = "--arena needs Small, Medium or Large.";
                        }

                        i++;
                        break;
                    case "--team":
                        options.Team = TeamIds(value, ref error);
                        i++;
                        break;
                    case "--content":
                        options.ContentRoot = value;
                        i++;
                        break;
                    case "--effects":
                        options.Effects = Intensity(value, ref error);
                        i++;
                        break;
                    case "--no-shake":
                        options.NoShake = true;
                        break;
                    case "--no-flashes":
                        options.NoFlashes = true;
                        break;
                    case "--show-settings":
                        options.ShowSettings = true;
                        break;
                    case "--glossary":
                        options.Glossary = value;
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

        /// <summary>Applies the effects flags to <paramref name="settings"/> (unset flags leave it as it is).</summary>
        public void ApplyTo(PlayerSettings settings)
        {
            if (Effects.HasValue)
            {
                settings.EffectsIntensity = Effects.Value;
            }

            if (NoShake)
            {
                settings.ScreenShake = false;
            }

            if (NoFlashes)
            {
                settings.Flashes = false;
            }
        }

        private static EffectsIntensity Intensity(string value, ref string error)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "full":
                    return EffectsIntensity.Full;
                case "reduced":
                    return EffectsIntensity.Reduced;
                case "minimal":
                    return EffectsIntensity.Minimal;
                default:
                    error = "--effects needs full, reduced or minimal.";
                    return EffectsIntensity.Full;
            }
        }

        private static string[] TeamIds(string value, ref string error)
        {
            string[] ids = (value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (ids.Length < 1 || ids.Length > 6)
            {
                error = "--team needs 1 to 6 species ids, comma-separated.";
                return null;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = ids[i].Trim();
            }

            return ids;
        }

        private static SafeInsets Insets(string value, ref string error)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            int[] px = new int[4];
            for (int i = 0; i < 4; i++)
            {
                if (parts.Length != 4 || !int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out px[i]) || px[i] < 0)
                {
                    error = "--safe-inset needs four whole numbers L,T,R,B (pixels, at least 0).";
                    return SafeInsets.None;
                }
            }

            return new SafeInsets(px[0], px[1], px[2], px[3]);
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
