using System;
using System.Globalization;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Parsed command-line options -- see Program.cs's doc comment for the two headless modes.</summary>
    public sealed class LaunchOptions
    {
        public bool BenchMode;
        public int BenchBeasts = 1;
        public double BenchSeconds = 10.0;
        public string BenchOutPath = "result.json";

        public bool ScreenshotMode;
        public string ScreenshotPath;
        public int ScreenshotBeasts = 1;
        public bool ScreenshotCrestOn = true;
        public int ScreenshotTint = 0;
        public bool ScreenshotHideStats = false;

        // Fifth-pass fix round (task 3, camera framing): in --battle mode the camera frames the fixed
        // arena, not a dynamic fit to wherever instances happen to be (see Game1.RebuildCamera) --
        // "arena" (default) fits the whole 11x15 board, "front" fits a tighter row range around the
        // Griffins' front line for a readable close-up (docs/spikes/055-3d-mini-spike.md's fifth-pass
        // fix-round section explains why a dynamic instance-fit camera read as "too far out").
        public string CameraZoom = "arena";

        // Fifth pass (swarm via VAT): --battle sets up a fixed-composition scene (BattleGriffins
        // GPU-skinned beasts + BattleSwarm VAT swarmlings on an 11x15 arena, "game scale" per the task
        // brief) instead of the fourth pass's single-species Tab-cycling stress test. BattleGriffins=0
        // gives "swarm alone" for the fifth-pass bench comparison the task brief asks for.
        public bool Battle;
        public int BattleGriffins = 3;
        public int BattleSwarm = 24;

        // A bench-only frame-rate cap (MonoGame's own fixed-time-step throttle, not vsync) -- the
        // research-backed 30fps battle target (docs/spikes/055-3d-mini-spike.md section 2.9) measured
        // alongside the uncapped throughput number, not instead of it.
        public int FpsCap = 0;

        // Producer-feedback fix round: "a --kill N option for screenshots" -- marks N random Swarmlings
        // dead right after the battle scene (and its initial facing) is built, so a screenshot can show
        // the survivors' facing after some of their nearest enemies are gone (see Game1.KillRandomSwarmling
        // and the interactive K key, the same mechanism). Clamped to the actual swarm count by
        // KillRandomSwarmling itself (no-ops once every Swarmling is dead).
        public int Kill = 0;

        // Anim-pilot griffin (issue #68): a dedicated headless mode that loads griffin_anim.glb (not
        // griffin_live.glb -- see Game1.LoadContent) and renders a numbered PNG sequence for the
        // producer-review deliverables (Tooling/Animation/README.md's gate): either one clip's own loop
        // (--pilot-clip idle|move|attack, for a contact sheet -- evenly spaced frames across exactly one
        // clip cycle) or the full crossfaded reel (--pilot-clip reel, the default -- Idle -> Move ->
        // Attack -> Idle with real crossfades, for the GIF). Mutually exclusive with --battle/--bench/
        // --screenshot in practice (not enforced here; Game1 just checks PilotSequenceMode first).
        public bool PilotSequenceMode;
        public string PilotSequenceDir;
        public string PilotClip = "reel";
        public int PilotFrames = 10; // contact-sheet frame count per clip; ignored for "reel"
        public int PilotFps = 24; // reel playback rate

        // Lead-review fix round: a side-view contact sheet for Move specifically (camera
        // perpendicular to the walk direction, not the battle camera angle) so the gait's fore-aft
        // leg swing actually reads instead of being foreshortened by the battle camera's yaw/tilt.
        public bool PilotSideCamera;

        // Round 16 (producer review -- wing blotch diagnosis): a debug-only switch that skips the
        // inverted-hull Outline draw call entirely, so the SAME --pilot-sequence frames can be
        // re-captured with the outline pass off, isolating whether a visual defect comes from the
        // outline pass or the toon fill pass underneath it. Used to confirm (not just assume) the
        // wing-blotch hypothesis before writing a fix -- see Game1.DrawBeasts and Toon.fx's
        // BoneOutlineMask comment for what the fix turned out to be.
        public bool PilotNoOutline;

        // Round 17 (producer review -- KO's lie-down redo): a straight-down camera, the same
        // pattern as PilotSideCamera above, so a flat-on-the-ground pose can actually be judged
        // from directly overhead -- the default 3/4 battle camera auto-fits its orthographic
        // projection to the instance's current bounds every frame (see ApplyCamera), which keeps
        // the subject nicely framed but means it never visibly "sinks" toward a ground reference
        // as the body lowers, making a lying-down pose hard to confirm from that angle alone.
        public bool PilotTopCamera;

        // v18 (wingless quadrupeds -- Golem/Kirin/Tarasque/Basilisk): --pilot-sequence was hard-coded
        // to "griffin_anim.glb"/"griffin_anim_events.json" (see Game1.LoadContent). Rather than fork
        // Game1/LaunchOptions per creature, --pilot-model overrides the GLB/events basename pair --
        // "golem" loads Content/model/golem_anim.glb + golem_anim_events.json the same way the
        // Griffin path always has. Defaults to "griffin" so every existing --pilot-sequence call
        // (scripts, README commands, CI) keeps behaving exactly as before with no flag change needed.
        public string PilotModel = "griffin";

        public static LaunchOptions Parse(string[] args)
        {
            var o = new LaunchOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--bench":
                        o.BenchMode = true;
                        o.BenchBeasts = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--seconds":
                        o.BenchSeconds = double.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--out":
                        o.BenchOutPath = args[++i];
                        break;
                    case "--screenshot":
                        o.ScreenshotMode = true;
                        o.ScreenshotPath = args[++i];
                        break;
                    case "--beasts":
                        o.ScreenshotBeasts = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--crest":
                        o.ScreenshotCrestOn = args[++i] != "off";
                        break;
                    case "--tint":
                        o.ScreenshotTint = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--hide-stats":
                        o.ScreenshotHideStats = true;
                        break;
                    case "--battle":
                        o.Battle = true;
                        break;
                    case "--griffins":
                        o.BattleGriffins = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--swarm":
                        o.BattleSwarm = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--fps-cap":
                        o.FpsCap = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--zoom":
                        o.CameraZoom = args[++i];
                        break;
                    case "--kill":
                        o.Kill = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--pilot-sequence":
                        o.PilotSequenceMode = true;
                        o.PilotSequenceDir = args[++i];
                        break;
                    case "--pilot-clip":
                        o.PilotClip = args[++i];
                        break;
                    case "--pilot-fps":
                        o.PilotFps = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--frames":
                        o.PilotFrames = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        break;
                    case "--pilot-side-camera":
                        o.PilotSideCamera = true;
                        break;
                    case "--pilot-no-outline":
                        o.PilotNoOutline = true;
                        break;
                    case "--pilot-top-camera":
                        o.PilotTopCamera = true;
                        break;
                    case "--pilot-model":
                        o.PilotModel = args[++i];
                        break;
                    default:
                        Console.Error.WriteLine("Unrecognised argument: " + args[i]);
                        break;
                }
            }
            return o;
        }
    }
}
