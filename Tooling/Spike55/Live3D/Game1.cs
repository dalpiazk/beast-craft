using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NumMatrix = System.Numerics.Matrix4x4;
using NumVector3 = System.Numerics.Vector3;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Spike #55, fourth pass: real-time 3D in MonoGame. Loads the Blender-authored, skinned
    /// griffin_live.glb (see Tooling/Spike55/blender_export_live.py), GPU-skins it per instance per
    /// frame (a bone-palette vertex shader -- see Toon.fx and GpuMesh.cs), and draws it with a custom
    /// toon + inverted-hull-outline effect over a Verdant Hollow backdrop and a hex-grid board. See
    /// Tooling/Spike55/README.md for controls and the bench/screenshot CLI, and
    /// docs/spikes/055-3d-mini-spike.md's fourth-pass section for what this was built to answer and the
    /// lead-review fix round's before/after numbers.</summary>
    public sealed class Game1 : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private readonly LaunchOptions _options;
        private SpriteBatch _spriteBatch;
        private SpriteFont _font;

        private GltfSkinnedModel _bodyModel;
        private GltfSkinnedModel _crestModel;
        private GpuMesh _bodyMesh;
        private GpuMesh _crestMesh;
        private Effect _toonEffect;
        private Texture2D _baseColorTexture;
        private Texture2D _crestTexture; // crest_alt.glb ships no base-colour image (a flat Principled
                                         // BSDF colour in Blender) -- a 1x1 solid-colour stand-in texture
                                         // so the shared Toon.fx (which always samples BaseTexture) has
                                         // something to sample instead of an unbound/black sampler (a
                                         // first render with no texture bound at all showed the crest as
                                         // near-black, indistinguishable from its own outline pass).
        private Texture2D _backdropTexture;
        private int _headNodeIndex;

        // Anim-pilot griffin (issue #68, Tooling/Animation): when --pilot-sequence is given, _bodyModel
        // is griffin_anim.glb instead of griffin_live.glb (see LoadContent) -- a 25-bone rig with three
        // named clips (Idle/Move/Attack) and real spring-joint bones, vs. the fourth pass's 11-bone,
        // two-clip rig. These fields are only ever populated in that mode; SpringJointConfig.IsValid
        // guards every use site so a non-pilot run (every other mode) is completely unaffected.
        private SpringJointConfig _springTail;
        private SpringJointConfig _springWingL;
        private SpringJointConfig _springWingR;
        private int _pilotFrameIndex;
        private int _pilotWarmupFramesLeft = 6; // let the window/GPU settle before the first capture

        // Round 15: griffin_anim.glb's sidecar event-marker JSON (export_glb.py writes
        // griffin_anim_events.json next to it -- Tooling/Animation/anim/keyed.py's EVENT_MARKERS, e.g.
        // Cast's "cast_release" VFX cue, Hit's "hit_react"). Keyed by clip name (matching
        // AnimatedPose.Clip's names), each entry a marker's fraction (0..1) through that clip's own
        // duration -- fraction, not the authored frame number, is what's actually usable here since
        // this runtime's clip duration (from the GLB's own animation sampler) is the authority, not
        // keyed.py's 24fps bake. Null/empty if the sidecar is missing (e.g. griffin_live.glb has none).
        private Dictionary<string, List<(string Name, float Fraction)>> _pilotEventMarkers;
        // Round 16: per-clip loop flag, same sidecar (see LoadPilotClipLoop) -- Idle/Move/Victory wrap
        // (true), Attack/Hit/Cast/KO clamp to their last key (false). Missing/unparseable defaults to
        // true per clip (the old, pre-round-16 always-wrap behaviour), via LoopFor's lookup below.
        private Dictionary<string, bool> _pilotClipLoop;
        // Crossing-detection state for --pilot-sequence logging (see LogPilotEventMarkerCrossings):
        // the previous captured frame's (clip, time-within-clip), so a marker is logged exactly once,
        // the first captured frame whose time has reached or passed it.
        private AnimatedPose.Clip _pilotMarkerLastClip = (AnimatedPose.Clip)(-1);
        private float _pilotMarkerLastTime = -1f;

        // Cached effect parameters (avoid a string-keyed lookup in EffectParameterCollection every draw
        // call -- looked up once here instead of via Parameters["..."] in the hot path).
        private EffectParameter _paramViewProjection;
        private EffectParameter _paramTintMultiply;
        private EffectParameter _paramBaseTexture;
        private EffectParameter _paramBones;

        // Fifth pass: swarm rendering via GPU skinning with a per-instance bone-array offset -- see
        // Toon.fx's "Fifth pass" section (this replaced an original Vertex-Animation-Texture design,
        // which MonoGame's effect compiler cannot build for the OpenGL profile at all) and
        // GpuMesh.BuildSwarmMerged. Only populated when --battle is passed.
        //
        // Fix-round redesign: the real, rigged Swarmling (blender_export_live_swarmling.py) is a real
        // skinned+animated GLB, loaded and posed exactly like the Griffin (GltfSkinnedModel +
        // AnimatedPose, evaluated at arbitrary time -- not the procedural placeholder's pre-baked,
        // nearest-frame-snapped bone matrices). What stays swarm-specific is BATCHING: the whole swarm
        // no longer fits in one merged draw call's worth of bone-palette registers at this rig's 6
        // bones/swarmling (see Toon.fx's header comment for the register arithmetic), so it draws in
        // fixed-size batches of SwarmBatchCapacity swarmlings, each with its own merged VertexBuffer
        // (GpuMesh.BuildSwarmMerged) and its own slice of bone data uploaded to Toon.fx's
        // SwarmBoneRows[] before that batch's 2 draw calls (Toon + Outline).
        private const int MaxSwarmInstances = 24; // must match Toon.fx's MAX_SWARM_INSTANCES
        private const int SwarmBatchCapacity = 12; // must match Toon.fx's SWARM_BATCH_CAPACITY
        private GltfSkinnedModel _swarmModel;
        private readonly List<GpuMesh> _swarmBatches = new List<GpuMesh>(); // one merged buffer per batch of <= SwarmBatchCapacity swarmlings
        private readonly List<int> _swarmBatchCounts = new List<int>(); // actual instance count in each batch (last one may be partial)
        private Texture2D _swarmTexture;
        private EffectParameter _paramSwarmBoneRows;
        private int _swarmInstanceCount;
        private int _swarmBonesPerInstance; // loaded from swarmling_live.glb's skin joint count; must equal Toon.fx's SWARM_BONES_PER_INSTANCE (6)
        private NumMatrix[] _swarmWorld = Array.Empty<NumMatrix>(); // per-instance world placement, rebuilt every frame from _swarmPosition + _swarmCurrentYaw (see UpdateFacing)
        private NumVector3[] _swarmPosition = Array.Empty<NumVector3>(); // fixed hex-cell position, set once at spawn (this spike has no movement)
        private float[] _swarmCurrentYaw = Array.Empty<float>(); // producer-feedback fix round: dynamic facing, same easing scheme as BeastInstance's
        private float[] _swarmTargetYaw = Array.Empty<float>();
        private bool[] _swarmAlive = Array.Empty<bool>(); // producer-feedback fix round: K / --kill N marks a swarmling dead (see KillRandomSwarmling)
        private float[] _swarmClockOffsets = Array.Empty<float>(); // per-instance desync, like BeastInstance.ClockOffset
        private Matrix4x4[][] _swarmNodeWorldScratch = Array.Empty<Matrix4x4[]>(); // per-instance, reused every frame (see AnimatedPose's doc comment)
        private Matrix4x4[][] _swarmSkinScratch = Array.Empty<Matrix4x4[]>();
        private XnaVector4[][] _swarmBoneRowsPalette = Array.Empty<XnaVector4[]>(); // one flat array per batch, uploaded to Toon.fx's SwarmBoneRows[] before that batch's draws

        // The crest's fixed local re-orientation (see SkinInstance's comment) plus, new this fix round,
        // a fixed facing rotation applied to every beast so the camera reads a 3/4 side profile with
        // the head on-screen-right -- matching content/art/beasts/griffin/griffin.png's orientation --
        // instead of the first version's straight head-on front view. See RebuildCamera's comment for
        // the other half of this fix (the camera itself moved from a frontal to a side position).
        // The crest's own fan-spread axis (blender_export_live.py's blade loop spreads blades along its
        // local X) used to face the old frontal camera directly. Now that every beast carries a fixed
        // FacingYaw, the crest (parented in head-local space, so it inherits that same yaw) needs a
        // compensating Y rotation or its fan reads edge-on (a thin sliver) from the new side camera --
        // confirmed by screenshot, fixed by this extra RotationY term.
        private static readonly NumMatrix CrestLocal = NumMatrix.CreateRotationY(MathF.PI / 2f) * NumMatrix.CreateScale(0.55f) * NumMatrix.CreateRotationX(-1.65f) * NumMatrix.CreateTranslation(0f, 0.05f, 0.05f);
        private static readonly NumMatrix FacingYaw = NumMatrix.CreateRotationY(-MathF.PI / 2f);
        private const float FacingYawAngle = -MathF.PI / 2f; // same angle as FacingYaw, as a float for the dynamic-facing fallback below
        // Battle-scene fix round (task 3, camera/facing): the Swarmling's bind pose shares the same
        // front convention as the Griffin's (both exported by the same blender_export_live*.py family --
        // confirmed by inspect_orientation.py's probe render, which shows the Swarmling's face/horns
        // when viewed from local -Y, same as the Griffin's head/beak). Griffins sit in the front rows
        // (lower row index, closer to the camera/player side) and swarmlings fill the rows behind them
        // (SetupBattleScene's startRow split) -- a mirrored yaw (+90 degrees instead of Griffins' -90)
        // turns the swarm to face back across the gap toward the Griffins, instead of either matching
        // the Griffins' own screen-right-facing orientation (would read as "ignoring" the front line) or
        // carrying no rotation at all (the placeholder's behaviour -- its squat, mostly-radially-
        // symmetric shape made an unrotated bind pose hard to fault by eye, but the real rigged mesh's
        // directional head/legs make facing actually visible, so this is a real fix, not cosmetic-only).
        private static readonly NumMatrix SwarmFacingYaw = NumMatrix.CreateRotationY(MathF.PI / 2f);
        private const float SwarmFacingYawAngle = MathF.PI / 2f; // same angle as SwarmFacingYaw, as a float for the dynamic-facing fallback below

        // Producer-feedback fix round: "each unit faces its nearest enemy" replaces both fixed yaws above
        // for the battle scene (SetStressLevel's single-species stress test is unaffected -- FacingYaw
        // stays a fixed constant there, no enemies exist to face). FacingYaw/SwarmFacingYaw's angles are
        // kept as the FALLBACK/initial yaw for a side with no living enemy (a Griffin with --swarm 0, or
        // the swarmling close-up's --griffins 0 -- "keep the last facing" needs *some* facing before any
        // enemy has ever existed) -- everything else below computes a real target-facing every frame once
        // an opposing unit exists, and eases toward it rather than snapping.
        //
        // Both assets share the same bind-pose forward convention (local +Z after the Blender->glTF axis
        // conversion -- see the fix-round doc's derivation from inspect_orientation.py's probe renders).
        // For this codebase's row-vector convention, Matrix.CreateRotationY(theta) maps a local-forward
        // vector (0,0,1) to world (sin(theta), 0, cos(theta)) (worked out from CreateRotationY's actual
        // matrix and v' = v * M -- NOT the "-90 turns +Z into +X" claim in this file's older comments,
        // which was never rechecked against the real formula and turns out to be backwards; harmless
        // there because FacingYaw/SwarmFacingYaw were only ever used as fixed decorative angles, but
        // load-bearing here since a wrong sign would make every unit face directly away from its target).
        // So facing world direction (dx, dz) needs theta = atan2(dx, dz) -- see YawTowards below.
        private const float TurnDurationSeconds = 0.25f; // a full 180-degree reversal takes this long; smaller turns are proportionally faster (a shortest-arc ease, not a fixed-time slerp)

        private static float WrapAngle(float radians)
        {
            radians %= MathF.PI * 2f;
            if (radians < -MathF.PI)
                radians += MathF.PI * 2f;
            else if (radians > MathF.PI)
                radians -= MathF.PI * 2f;
            return radians;
        }

        /// <summary>Eases `current` toward `target` by at most `maxDelta` radians this frame, always via
        /// the shortest arc (wrapping the difference to (-pi, pi] first) -- equivalent to a yaw-only
        /// quaternion slerp at a capped angular speed, without needing System.Numerics.Quaternion for a
        /// rotation that only ever happens around one axis.</summary>
        private static float StepTowardAngle(float current, float target, float maxDelta)
        {
            float delta = WrapAngle(target - current);
            if (MathF.Abs(delta) <= maxDelta)
                return WrapAngle(target);
            return WrapAngle(current + MathF.Sign(delta) * maxDelta);
        }

        /// <summary>The yaw (radians) that turns this asset's bind-pose forward (+Z) to point from `from`
        /// toward `to`, XZ-plane only (ground-plane facing, no pitch). See this section's header comment
        /// for the CreateRotationY(theta) -> world (sin theta, cos theta) derivation this inverts.</summary>
        private static float YawTowards(NumVector3 from, NumVector3 to)
        {
            float dx = to.X - from.X, dz = to.Z - from.Z;
            return MathF.Atan2(dx, dz);
        }

        // Lead-review fix round: the Swarmling's own exported bind pose (TARGET_HEIGHT=0.55 in
        // blender_export_live_swarmling.py) reads far smaller than its hex even after HexBoard's own
        // scale fix (a direct pixel comparison against the real 2D battle screen showed an enemy sprite
        // filling roughly two-thirds of its hex; this mesh's round body core -- not counting its
        // horn-tip-to-horn-tip bounding box, which is wider than the body actually looks -- was under
        // half that). A runtime scale multiplier (not a re-export) so this can be tuned without
        // rebuilding the asset; folded into each swarm instance's World matrix, same place FacingYaw is.
        private const float SwarmScale = 1.4f;

        private readonly List<BeastInstance> _instances = new List<BeastInstance>();
        private readonly Random _rng = new Random(12345); // fixed seed: reproducible desync, reproducible bench runs
        private static readonly int[] StressLevels = { 1, 3, 12, 24 };
        private int _stressIndex = 0;

        private AnimatedPose.Clip _clip = AnimatedPose.Clip.Idle;
        private float _elapsedSeconds;
        private int _tintIndex = 0;
        private static readonly XnaVector3[] Tints =
        {
            new XnaVector3(1.0f, 1.0f, 1.0f),   // natural
            new XnaVector3(0.55f, 0.85f, 1.15f), // cool azure colour-form
            new XnaVector3(1.25f, 0.65f, 0.80f), // rose-ember colour-form
        };

        private VertexBuffer _hexGridVertexBuffer;
        private int _hexGridVertexCount;
        private BasicEffect _lineEffect;
        private BasicEffect _backdropEffect; // full-screen quad in clip space, no SpriteBatch dependency needed

        private readonly StatsTracker _stats = new StatsTracker();
        // Measures actual frame-to-frame wall time (Update-start to Update-start), which is what "fps"
        // means -- it captures Present/vsync/driver stalls a Draw()-only stopwatch would miss (a first
        // version of this file timed only the Draw() method body and reported a wildly optimistic fps,
        // ~35x the true frame-to-frame rate measured by wall-clock frame count / bench duration; fixed
        // by moving frame timing to a stopwatch that runs continuously across the whole loop instead).
        private readonly Stopwatch _loopStopwatch = Stopwatch.StartNew();
        private bool _firstFrame = true;
        private readonly Stopwatch _skinStopwatch = new Stopwatch();

        private KeyboardState _prevKeyboard;

        // --bench state
        private double _benchElapsedSeconds;
        private readonly List<double> _benchFrameMs = new List<double>();
        private int _gc0Start, _gc1Start, _gc2Start;

        // --screenshot state: warm up by elapsed wall-clock time (not a fixed frame count) so the
        // on-screen fps stats are real by the time the shot is taken -- a lead-review fix: the first
        // version waited a fixed 30 frames, which at a few hundred fps is well under StatsTracker's
        // 3-second rolling window, so the overlay's fps read 0.0 in every screenshot.
        private const double ScreenshotWarmupSeconds = 3.5;
        private double _screenshotElapsedSeconds;

        private XnaMatrix _view;
        private XnaMatrix _projection;

        public Game1(LaunchOptions options)
        {
            _options = options;
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            // Portrait 1080x1920 is the task brief's target. Interactive play and --bench stay at a 50%-
            // scaled 540x960 so the window fits a normal desktop monitor without the person having to
            // move it (see README "Window size") and so bench numbers stay comparable across passes at a
            // fixed resolution; --screenshot renders at the real, full 1080x1920 -- fix-round change
            // (task 3): the task brief asks for portrait 1080x1920 framing specifically for the battle
            // screenshots, and half-scale screenshots made the swarm's already-small units harder to
            // judge for on-screen readability than the shipped game would be.
            if (_options.ScreenshotMode || _options.PilotSequenceMode)
            {
                _graphics.PreferredBackBufferWidth = 1080;
                _graphics.PreferredBackBufferHeight = 1920;
            }
            else
            {
                _graphics.PreferredBackBufferWidth = 540;
                _graphics.PreferredBackBufferHeight = 960;
            }
            _graphics.SynchronizeWithVerticalRetrace = !_options.BenchMode; // uncapped in --bench, for a true throughput number
            if (_options.BenchMode && _options.FpsCap > 0)
            {
                // A bench-mode frame-rate cap: MonoGame's own fixed-time-step throttle (not vsync, which
                // stays off above so this is a deliberate, measured cap rather than a monitor-refresh
                // accident) -- measures whether the scene can sustain the research-backed 30fps battle
                // target (docs/spikes/055-3d-mini-spike.md section 2.9), not just uncapped throughput.
                IsFixedTimeStep = true;
                TargetElapsedTime = TimeSpan.FromSeconds(1.0 / _options.FpsCap);
            }
            else
            {
                IsFixedTimeStep = false;
            }
            _graphics.ApplyChanges();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("DebugFont");
            _toonEffect = Content.Load<Effect>("Effects/Toon");

            string contentRoot = Path.Combine(AppContext.BaseDirectory, "Content");
            // Anim-pilot griffin: --pilot-sequence swaps in griffin_anim.glb (Tooling/Animation's 25-bone,
            // three-clip rig) in place of the fourth pass's griffin_live.glb -- same loader, same GPU mesh
            // builder, same toon/outline draw path; only the bone/joint-name conventions differ (this
            // rig's bones are named head/tail_04/wing_L_03/wing_R_03 etc, not bone_head -- see
            // rig_templates/winged_quadruped.py), so the node-name lookups below branch on which file was
            // loaded rather than needing a second code path.
            bool pilotMode = _options.PilotSequenceMode;
            string bodyGlbName = pilotMode ? "griffin_anim.glb" : "griffin_live.glb";
            _bodyModel = GltfSkinnedModel.Load(Path.Combine(contentRoot, "model", bodyGlbName));
            _crestModel = GltfSkinnedModel.Load(Path.Combine(contentRoot, "model", "crest_alt.glb"));
            _headNodeIndex = AnimatedPose.FindNodeIndexByName(_bodyModel, pilotMode ? "head" : "bone_head");

            if (pilotMode)
            {
                // Tuning by eye against the review GIF, not derived from any physical measurement (see
                // SpringBone.cs's header comment): the tail is heavier/longer and settles slower (lower
                // stiffness, more damping) than a light wing feather tip.
                _springTail = new SpringJointConfig(AnimatedPose.FindNodeIndexByName(_bodyModel, "tail_04"), 90f, 6f);
                _springWingL = new SpringJointConfig(AnimatedPose.FindNodeIndexByName(_bodyModel, "wing_L_03"), 140f, 8f);
                _springWingR = new SpringJointConfig(AnimatedPose.FindNodeIndexByName(_bodyModel, "wing_R_03"), 140f, 8f);

                string eventsSidecarPath = Path.Combine(contentRoot, "model", "griffin_anim_events.json");
                _pilotEventMarkers = LoadPilotEventMarkers(eventsSidecarPath);
                _pilotClipLoop = LoadPilotClipLoop(eventsSidecarPath);
            }

            _bodyMesh = GpuMesh.Build(GraphicsDevice, _bodyModel);
            _crestMesh = GpuMesh.Build(GraphicsDevice, _crestModel);

            // Every beast is yawed 90 degrees (FacingYaw, see RebuildCamera's comment) to face the
            // camera side-on, which swaps which bind-pose axis maps to world X (columns) vs world Z
            // (rows): local X (wingspan) ends up along world Z, local Z (chest-to-tail depth) ends up
            // along world X. A first version sized hex spacing from local X alone and got it wrong for
            // the now-relevant column axis -- fixed by taking the larger of the two bind-pose extents,
            // so hex spacing comfortably covers whichever axis ends up where post-yaw.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in _bodyModel.Positions)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
                maxZ = Math.Max(maxZ, p.Z);
            }
            HexBoard.SetScale(Math.Max(maxX - minX, maxZ - minZ));

            _baseColorTexture = LoadTextureFromBytes(_bodyModel.BaseColorImageBytes);

            // MGFX (MonoGame's effect compiler) does not honour a .fx file's HLSL default-value
            // initialisers at runtime -- every Effect parameter comes back zero until set from C#
            // (confirmed: a first run with only ViewProjection/BaseTexture/TintMultiply set from here
            // rendered the whole beast flat black, because LightDirection defaulted to a zero vector,
            // normalize(0,0,0) is undefined/NaN, every ndotl comparison then fell through to the
            // HighlightBoost band -- which was *also* unset/zero -- multiplying the sampled texture by
            // black regardless of what it actually was). Set the toon material's fixed constants once
            // here, not per frame (only ViewProjection/Bones/BaseTexture/TintMultiply change per
            // frame/instance). Lead-review fix round: LightColor/HighlightBoost pulled back toward
            // neutral -- the first tuning rendered noticeably more saturated/orange than both the
            // Meshy source texture and the approved illustration (sampled and compared, see Toon.fx's
            // header comment).
            _toonEffect.Parameters["LightDirection"].SetValue(XnaVector3.Normalize(new XnaVector3(0.45f, 0.65f, 0.60f)));
            _toonEffect.Parameters["LightColor"].SetValue(new XnaVector3(1.0f, 0.97f, 0.92f));
            _toonEffect.Parameters["ShadowTint"].SetValue(new XnaVector3(0x7C / 255f, 0x7A / 255f, 0xAE / 255f));
            _toonEffect.Parameters["HighlightBoost"].SetValue(new XnaVector3(1.03f, 1.0f, 0.96f));
            _toonEffect.Parameters["OutlineThickness"].SetValue(0.012f);
            _toonEffect.Parameters["OutlineColor"].SetValue(new XnaVector3(0x2E / 255f, 0x2A / 255f, 0x45 / 255f));
            _toonEffect.Parameters["BoneOutlineMask"].SetValue(BuildBoneOutlineMask(_bodyModel));
            _paramViewProjection = _toonEffect.Parameters["ViewProjection"];
            _paramTintMultiply = _toonEffect.Parameters["TintMultiply"];
            _paramBaseTexture = _toonEffect.Parameters["BaseTexture"];
            _paramBones = _toonEffect.Parameters["Bones"];
            using (var fs = File.OpenRead(Path.Combine(contentRoot, "backdrop.png")))
                _backdropTexture = Texture2D.FromStream(GraphicsDevice, fs);

            _crestTexture = new Texture2D(GraphicsDevice, 1, 1);
            _crestTexture.SetData(new[] { new XnaColor(64, 191, 179) }); // matches CrestAlt's Blender material (0.25, 0.75, 0.70)

            _lineEffect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
            _backdropEffect = new BasicEffect(GraphicsDevice) { TextureEnabled = true, VertexColorEnabled = false, World = XnaMatrix.Identity, View = XnaMatrix.Identity, Projection = XnaMatrix.Identity };

            if (_options.Battle)
            {
                _swarmModel = GltfSkinnedModel.Load(Path.Combine(contentRoot, "model", "swarmling_live.glb"));
                _swarmBonesPerInstance = _swarmModel.Joints.Length;
                if (_swarmBonesPerInstance != 6)
                    throw new InvalidOperationException(
                        $"swarmling_live.glb has {_swarmBonesPerInstance} joints; Toon.fx's SwarmBoneRows[] " +
                        "layout is hardcoded to SWARM_BONES_PER_INSTANCE=6 (see its header comment) -- " +
                        "re-export with the 6-bone rig or update both the shader and this check together.");
                _swarmTexture = LoadTextureFromBytes(_swarmModel.BaseColorImageBytes);
                _paramSwarmBoneRows = _toonEffect.Parameters["SwarmBoneRows"];
                // Lead-review fix round: a separate, proportionally smaller outline push-out distance for
                // the swarm pass -- see Toon.fx's SwarmOutlineThickness comment for why the Griffin's
                // fixed 0.012 world-unit thickness reads as an oversized ring on the much smaller
                // Swarmling. Scaled by the ratio of the two assets' own effective on-screen heights:
                // Griffin's 2.0 TARGET_HEIGHT (blender_export_live.py) vs the Swarmling's 0.55 TARGET_
                // HEIGHT (blender_export_live_swarmling.py) x the runtime SwarmScale multiplier above
                // (both hardcoded, not stored in either GLB) -- so the outline stays proportionally
                // consistent with SwarmScale if that constant is ever re-tuned.
                const float griffinTargetHeight = 2.0f;
                const float swarmlingTargetHeight = 0.55f;
                _toonEffect.Parameters["SwarmOutlineThickness"].SetValue(0.012f * (swarmlingTargetHeight * SwarmScale / griffinTargetHeight));

                var battleGridVerts = HexBoard.BuildGridLines(11, 15, new XnaColor(46, 42, 69, 140));
                _hexGridVertexBuffer = new VertexBuffer(GraphicsDevice, VertexPositionColor.VertexDeclaration, battleGridVerts.Length, BufferUsage.WriteOnly);
                _hexGridVertexBuffer.SetData(battleGridVerts);
                _hexGridVertexCount = battleGridVerts.Length;

                SetupBattleScene(_options.BattleGriffins, _options.BattleSwarm);

                // Producer feedback: "a --kill N option for screenshots" -- applied once, right after the
                // initial formation is built and its facing snapped, so a --kill screenshot shows the
                // Griffins already holding whatever facing they settled on once those Swarmlings were
                // removed (RecomputeFacingTargets below re-targets survivors' nearest enemies too, same as
                // the interactive K key does frame-by-frame).
                for (int i = 0; i < _options.Kill; i++)
                    KillRandomSwarmling();
                if (_options.Kill > 0)
                    SnapInitialFacing();
            }
            else
            {
                var gridVerts = HexBoard.BuildGridLines(HexBoard.ArenaWidth, HexBoard.ArenaHeight, new XnaColor(46, 42, 69, 140));
                _hexGridVertexBuffer = new VertexBuffer(GraphicsDevice, VertexPositionColor.VertexDeclaration, gridVerts.Length, BufferUsage.WriteOnly);
                _hexGridVertexBuffer.SetData(gridVerts);
                _hexGridVertexCount = gridVerts.Length;

                SetStressLevel(_options.BenchMode ? _options.BenchBeasts : (_options.ScreenshotMode ? _options.ScreenshotBeasts : 1));
                if (!_options.ScreenshotCrestOn || _options.PilotSequenceMode)
                    // The crest attachment's fixed local re-orientation (CrestLocal) is tuned for
                    // griffin_live.glb's "bone_head" bind pose -- on the pilot rig's differently-oriented
                    // "head" bone it renders as a stray floating plume, not a head decoration. The crest
                    // toggle is a separate fourth-pass feature from the anim-pilot deliverable, so it's
                    // simply off for every pilot-sequence capture rather than re-tuned for a rig it was
                    // never meant to drive.
                    foreach (var inst in _instances)
                        inst.CrestOn = false;
                _tintIndex = Math.Clamp(_options.ScreenshotTint, 0, Tints.Length - 1);
            }

            if (_options.BenchMode)
            {
                _gc0Start = GC.CollectionCount(0);
                _gc1Start = GC.CollectionCount(1);
                _gc2Start = GC.CollectionCount(2);
            }
        }

        private Texture2D LoadTextureFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            using (var ms = new MemoryStream(bytes))
                return Texture2D.FromStream(GraphicsDevice, ms);
        }

        private void SetStressLevel(int count)
        {
            while (_instances.Count < count)
                _instances.Add(new BeastInstance(_bodyModel));
            while (_instances.Count > count)
                _instances.RemoveAt(_instances.Count - 1);

            var cells = HexBoard.FillOrder(HexBoard.ArenaWidth, HexBoard.ArenaHeight, count);
            for (int i = 0; i < _instances.Count; i++)
            {
                var (col, row) = cells[i % Math.Max(1, cells.Count)];
                var center = HexBoard.CellCenter(col, row);
                // FacingYaw first (rotate the bind-pose beast to face +X, screen-right, under the new
                // side-on camera -- see RebuildCamera), then place on its hex cell.
                var world = FacingYaw * NumMatrix.CreateTranslation(center.X, 0f, center.Z);
                _instances[i].World = world;
                _instances[i].ClockOffset = (float)(_rng.NextDouble() * 4.0);
            }
            RebuildCamera();
        }

        /// <summary>The fifth-pass battle scene: `griffinCount` GPU-skinned beasts in a front row plus
        /// `swarmCount` GPU-skinned swarmlings filling the rows behind them, on an 11x15 arena ("game
        /// scale" per the task brief -- a Large arena per docs/art/hollow-art-slots.md's sizing). Unlike
        /// the placeholder's pre-baked design, the swarm's per-instance pose is evaluated every frame via
        /// AnimatedPose just like the Griffins' (see UpdateSwarmBones) -- what's still set up ONCE here
        /// is the batching (GpuMesh.BuildSwarmMerged per batch) and each instance's fixed hex-cell world
        /// placement/phase.</summary>
        private void SetupBattleScene(int griffinCount, int swarmCount)
        {
            bool closeUp = string.Equals(_options.CameraZoom, "close", StringComparison.OrdinalIgnoreCase);

            // Producer feedback (camera-yaw round): "player units at the bottom (nearest the camera),
            // enemies at the top" -- CameraDir's dominant +Z component means larger-row (larger world Z)
            // cells render nearer the camera/lower on screen (see CameraDir's own comment), so the
            // Griffins now start near the arena's HIGH row end (row 11 of 0..14) and the Swarm starts at
            // the LOW end (row 0), the opposite of this scene's original layout. `SwarmCenterColOffset`
            // ("offset the swarm a little so facing varies naturally") shifts the Swarm's fill centre two
            // columns right of the Griffins' so the two sides aren't perfectly column-aligned -- a
            // perfectly mirrored formation makes real nearest-enemy facing compute a near-zero yaw for
            // almost every unit (section 2.12's "honest finding"); this keeps that from being the only
            // thing a screenshot ever shows.
            const int griffinStartRow = 9;
            const int swarmStartRow = 0;
            const int swarmCenterColOffset = 2;

            _instances.Clear();
            var griffinCells = HexBoard.FillOrder(11, 15, griffinCount, startRow: griffinStartRow);
            for (int i = 0; i < griffinCount; i++)
            {
                var inst = new BeastInstance(_bodyModel);
                var (col, row) = griffinCells[i % Math.Max(1, griffinCells.Count)];
                var center = HexBoard.CellCenter(col, row);
                inst.Position = new NumVector3(center.X, 0f, center.Z);
                // Fallback yaw for a side with no living enemy yet (see this file's facing-header comment
                // above FacingYaw/SwarmFacingYaw) -- SnapInitialFacing below overrides it immediately if a
                // Swarmling exists.
                inst.CurrentYaw = inst.TargetYaw = FacingYawAngle;
                inst.World = NumMatrix.CreateRotationY(inst.CurrentYaw) * NumMatrix.CreateTranslation(center.X, 0f, center.Z);
                inst.ClockOffset = (float)(_rng.NextDouble() * 4.0);
                _instances.Add(inst);
            }

            swarmCount = Math.Min(swarmCount, MaxSwarmInstances);
            _swarmInstanceCount = swarmCount;

            _swarmBatches.Clear();
            _swarmBatchCounts.Clear();
            int batchCount = swarmCount > 0 ? (swarmCount + SwarmBatchCapacity - 1) / SwarmBatchCapacity : 0;
            for (int b = 0; b < batchCount; b++)
            {
                int thisBatch = Math.Min(SwarmBatchCapacity, swarmCount - b * SwarmBatchCapacity);
                _swarmBatches.Add(GpuMesh.BuildSwarmMerged(GraphicsDevice, _swarmModel, thisBatch));
                _swarmBatchCounts.Add(thisBatch);
            }
            _swarmBoneRowsPalette = new XnaVector4[batchCount][];
            for (int b = 0; b < batchCount; b++)
            {
                var rows = new XnaVector4[SwarmBatchCapacity * _swarmBonesPerInstance * 3];
                for (int r = 0; r < rows.Length; r += 3)
                {
                    // Identity 3x4 rows for unused slots (a partial last batch): row0=(1,0,0,0),
                    // row1=(0,1,0,0), row2=(0,0,1,0) -- no vertex references these unless a batch is
                    // short, but keeping them identity rather than zero avoids a degenerate (all-zero)
                    // skin matrix if anything ever does.
                    rows[r + 0] = new XnaVector4(1f, 0f, 0f, 0f);
                    rows[r + 1] = new XnaVector4(0f, 1f, 0f, 0f);
                    rows[r + 2] = new XnaVector4(0f, 0f, 1f, 0f);
                }
                _swarmBoneRowsPalette[b] = rows;
            }

            _swarmWorld = swarmCount > 0 ? new NumMatrix[swarmCount] : Array.Empty<NumMatrix>();
            _swarmPosition = swarmCount > 0 ? new NumVector3[swarmCount] : Array.Empty<NumVector3>();
            _swarmCurrentYaw = swarmCount > 0 ? new float[swarmCount] : Array.Empty<float>();
            _swarmTargetYaw = swarmCount > 0 ? new float[swarmCount] : Array.Empty<float>();
            _swarmAlive = swarmCount > 0 ? new bool[swarmCount] : Array.Empty<bool>();
            _swarmClockOffsets = swarmCount > 0 ? new float[swarmCount] : Array.Empty<float>();
            _swarmNodeWorldScratch = swarmCount > 0 ? new Matrix4x4[swarmCount][] : Array.Empty<Matrix4x4[]>();
            _swarmSkinScratch = swarmCount > 0 ? new Matrix4x4[swarmCount][] : Array.Empty<Matrix4x4[]>();
            if (swarmCount > 0)
            {
                // Lead-review fix round: `--zoom close` is the dedicated single-unit beauty-shot mode
                // (the swarmling close-up) -- using the battle-formation SwarmFacingYawAngle there pointed
                // the one swarmling's face away from the same camera angle that shows a Griffin's front
                // (confirmed by screenshot: the close-up read as "from behind/above"). It has no reason to
                // apply to an isolated close-up with no Griffins in the shot at all (no enemy ever exists
                // there to override this fallback) -- identity (0 yaw) instead, same camera-facing
                // convention that already reads correctly for the Griffin close-up.
                float fallbackYaw = closeUp ? 0f : SwarmFacingYawAngle;
                var swarmCells = HexBoard.FillOrder(11, 15, swarmCount, startRow: swarmStartRow, centerColOverride: 11 / 2 + swarmCenterColOffset);
                for (int i = 0; i < swarmCount; i++)
                {
                    var (col, row) = swarmCells[i % Math.Max(1, swarmCells.Count)];
                    var center = HexBoard.CellCenter(col, row);
                    _swarmPosition[i] = new NumVector3(center.X, 0f, center.Z);
                    _swarmCurrentYaw[i] = _swarmTargetYaw[i] = fallbackYaw;
                    // SwarmScale first (local-space, before any rotation/translation), then the fallback
                    // yaw (SnapInitialFacing below overrides it immediately if a Griffin exists), then
                    // place on its cell.
                    _swarmWorld[i] = NumMatrix.CreateScale(SwarmScale) * NumMatrix.CreateRotationY(fallbackYaw) * NumMatrix.CreateTranslation(center.X, 0f, center.Z);
                    _swarmAlive[i] = true;
                    _swarmClockOffsets[i] = (float)(_rng.NextDouble() * 4.0);
                    _swarmNodeWorldScratch[i] = new Matrix4x4[_swarmModel.Nodes.Length];
                    _swarmSkinScratch[i] = new Matrix4x4[_swarmModel.Joints.Length];
                }
            }

            // Producer-feedback fix round ("keep the last facing"): every unit's fallback yaw above is
            // overridden immediately, once, so nothing visibly snaps on the very first frame if an
            // opposing unit already exists (UpdateFacing's own per-frame easing is what animates any
            // *later* change, e.g. a kill removing the current nearest target).
            SnapInitialFacing();

            RebuildCamera();
        }

        /// <summary>Recomputes every unit's TargetYaw from the current nearest-living-enemy search (see
        /// UpdateFacing's own doc comment for the search itself), then immediately sets CurrentYaw to
        /// match -- used once, right after SetupBattleScene builds the initial formation, so the very
        /// first frame already shows units facing their nearest enemy instead of animating from the
        /// fallback FacingYawAngle/SwarmFacingYawAngle every time the scene is (re)built.</summary>
        private void SnapInitialFacing()
        {
            RecomputeFacingTargets();
            foreach (var inst in _instances)
                inst.CurrentYaw = inst.TargetYaw;
            for (int i = 0; i < _swarmInstanceCount; i++)
                _swarmCurrentYaw[i] = _swarmTargetYaw[i];
            ApplyFacingToWorldMatrices();
        }

        /// <summary>Producer-feedback fix round: "each unit faces its nearest enemy" -- updates every
        /// Griffin's TargetYaw to face its nearest living Swarmling, and every living Swarmling's
        /// TargetYaw to face its nearest Griffin (Griffins never die in this scene, so "nearest Griffin"
        /// never needs a liveness check). A side with no living enemy is left alone -- its TargetYaw stays
        /// whatever it last was, which is exactly "keep the last facing": UpdateFacing's caller still eases
        /// CurrentYaw toward that unchanged target every frame, so a facing already in flight when the
        /// last enemy dies finishes its turn and then genuinely stops, rather than snapping back to a
        /// default. Cheap at this scale (<=3 Griffins x <=24 Swarmlings, no spatial index needed) and run
        /// unconditionally every frame rather than only "when units move or an enemy is removed" -- this
        /// spike has no movement, so the only thing that can actually change target is a kill, and
        /// checking every frame is simpler and just as correct as trying to detect that event.</summary>
        private void RecomputeFacingTargets()
        {
            foreach (var inst in _instances)
            {
                NumVector3 pos = inst.Position;
                bool found = false;
                float bestDistSq = float.MaxValue;
                NumVector3 best = default;
                for (int i = 0; i < _swarmInstanceCount; i++)
                {
                    if (!_swarmAlive[i])
                        continue;
                    float dx = _swarmPosition[i].X - pos.X, dz = _swarmPosition[i].Z - pos.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = _swarmPosition[i];
                        found = true;
                    }
                }
                if (found)
                    inst.TargetYaw = YawTowards(pos, best);
            }

            for (int i = 0; i < _swarmInstanceCount; i++)
            {
                if (!_swarmAlive[i])
                    continue;
                NumVector3 pos = _swarmPosition[i];
                bool found = false;
                float bestDistSq = float.MaxValue;
                NumVector3 best = default;
                foreach (var inst in _instances)
                {
                    float dx = inst.Position.X - pos.X, dz = inst.Position.Z - pos.Z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        best = inst.Position;
                        found = true;
                    }
                }
                if (found)
                    _swarmTargetYaw[i] = YawTowards(pos, best);
            }
        }

        /// <summary>Eases every unit's CurrentYaw toward its TargetYaw (see RecomputeFacingTargets) at a
        /// capped angular speed, shortest arc, then rebuilds World from the result -- called every battle
        /// frame from Update, before the pose/skin work that reads World.</summary>
        private void UpdateFacing(float dtSeconds)
        {
            RecomputeFacingTargets();
            float maxDelta = (MathF.PI / TurnDurationSeconds) * dtSeconds;

            foreach (var inst in _instances)
                inst.CurrentYaw = StepTowardAngle(inst.CurrentYaw, inst.TargetYaw, maxDelta);
            for (int i = 0; i < _swarmInstanceCount; i++)
                _swarmCurrentYaw[i] = StepTowardAngle(_swarmCurrentYaw[i], _swarmTargetYaw[i], maxDelta);

            ApplyFacingToWorldMatrices();
        }

        /// <summary>Rebuilds every unit's World matrix from its fixed Position and current (eased) yaw --
        /// shared by SnapInitialFacing (no easing, first frame) and UpdateFacing (eased, every later
        /// frame) so both end up with a World matrix in the exact same shape.</summary>
        private void ApplyFacingToWorldMatrices()
        {
            foreach (var inst in _instances)
                inst.World = NumMatrix.CreateRotationY(inst.CurrentYaw) * NumMatrix.CreateTranslation(inst.Position.X, 0f, inst.Position.Z);
            for (int i = 0; i < _swarmInstanceCount; i++)
                _swarmWorld[i] = NumMatrix.CreateScale(SwarmScale) * NumMatrix.CreateRotationY(_swarmCurrentYaw[i]) * NumMatrix.CreateTranslation(_swarmPosition[i].X, 0f, _swarmPosition[i].Z);
        }

        /// <summary>Producer feedback: "add a key ... that removes a random swarmling so this can be seen
        /// interactively" (K, see Update) "and a --kill N option for screenshots" (see LoadContent). Marks
        /// one living Swarmling dead -- UpdateSwarmBones writes an all-zero (degenerate) skin for a dead
        /// instance instead of a real pose (see its own comment), collapsing every one of its vertices to
        /// the origin so it simply doesn't rasterise, without touching the merged batch's VertexBuffer or
        /// the SwarmBoneRows[] register layout at all. No-op if every Swarmling is already dead.</summary>
        private void KillRandomSwarmling()
        {
            int aliveCount = 0;
            for (int i = 0; i < _swarmInstanceCount; i++)
                if (_swarmAlive[i])
                    aliveCount++;
            if (aliveCount == 0)
                return;
            int target = _rng.Next(aliveCount);
            int seen = 0;
            for (int i = 0; i < _swarmInstanceCount; i++)
            {
                if (!_swarmAlive[i])
                    continue;
                if (seen == target)
                {
                    _swarmAlive[i] = false;
                    return;
                }
                seen++;
            }
        }

        // Lead-review fix (fourth pass): the first camera sat on the Z axis looking straight at the
        // beast's front (its glTF-space forward, +Z, points directly at a camera offset in +Z), reading
        // as a flat head-on view with wings straight up. Beasts in the game are seen in a 3/4 side view
        // (content/art/beasts/griffin/griffin.png) -- fixed by rotating every beast FacingYaw=-90
        // degrees (see SetStressLevel) so its forward axis points world +X instead of +Z, and moving the
        // camera to the *side* (offset mostly along Z, a little along X for a slight 3/4 turn rather
        // than a flat profile), elevated and tilted down for the hex-board angle the earlier Blender
        // passes also used. A hex board's rows are separated along world Z; an orthographic camera
        // looking *straight* down Z would collapse all rows onto the same screen position no matter how
        // many world units apart they are -- confirmed: the first tilt value (24 degrees, chosen for a
        // single beast's close-up 3/4 read) made a 3-row, 24-beast formation collapse into one
        // overlapping clump on screen even though the fit math below correctly measured the beasts as
        // spread across ~20 world units. 48 degrees gave the rows real screen-Y separation while still
        // reading as a 3/4, not top-down, view for the close-up single-beast case.
        //
        // Producer feedback (second round): 48 degrees read as too steep -- mostly tops of units, not
        // their sides. Lowered to 33 (within the requested ~30-35 range, picked by eye against the same
        // screenshots) -- shallower elevation, more side profile, still tilted enough that a 15-row-deep
        // arena's rows separate on screen rather than collapsing (the same failure the original 24-degree
        // attempt hit). Shared by every camera mode (arena/front/close and the non-battle stress test) for
        // one consistent look rather than a battle-only special case.
        private const float CameraTiltDeg = 33f;
        // Producer feedback (camera-yaw round): a real yaw around the board (not the old fixed 0.22
        // lateral nudge this replaces) -- "angle the camera ~35 degrees around the board so both sides
        // read in three-quarter view (flanks and faces) instead of fronts and backs, the standard
        // portrait-tactics look". Combined with real nearest-enemy facing (section 2.12), a unit whose
        // bind-pose forward (+Z) points roughly down the board's own Z axis now reads at a genuine 3/4
        // angle to THIS camera even when its yaw relative to its target is itself near zero -- the yaw
        // this round adds is a camera-side fix for the "flat front/back" finding section 2.12 called out
        // honestly, not a change to the facing math itself (still real, unchanged, world-space). 35
        // degrees is the midpoint of the requested ~30-40 range; judged by eye against the actual
        // screenshots, not re-derived analytically.
        private const float CameraYawDeg = 35f;
        private const float CameraDistance = 8f;

        private XnaVector3 CameraDir()
        {
            // Lead-review fix round: --pilot-side-camera swaps in a camera perpendicular to the
            // walk direction. Every beast is yawed by FacingYaw to face world +X (see
            // SetStressLevel's comment), and the DEFAULT battle camera's yaw=35 is a small offset
            // from yaw=0 specifically because yaw=0 (a pure world-Z offset) is already "a genuine
            // 3/4 angle" to an X-facing unit -- i.e. yaw=0 is itself close to a true side view. So
            // the side camera is yaw=0 (not the battle camera's 35), with a shallower tilt so the
            // gait's fore-aft leg swing reads clearly instead of being foreshortened by elevation.
            // Every other mode is completely unaffected.
            //
            // Round 17: --pilot-top-camera looks straight down (tilt=90) -- same idea as the side
            // camera above, a dedicated angle for judging a pose the default 3/4 battle camera
            // can't show clearly (there, a flat-on-the-ground pose; here, whether limbs/wings are
            // splayed out to the sides correctly when seen from directly overhead).
            float tiltDeg = _options.PilotTopCamera ? 89.9f : _options.PilotSideCamera ? 10f : CameraTiltDeg;
            float yawDeg = _options.PilotSideCamera ? 0f : CameraYawDeg;
            float tilt = MathHelper.ToRadians(tiltDeg);
            float yaw = MathHelper.ToRadians(yawDeg);
            // Horizontal (XZ-plane) magnitude of the tilt direction, then rotated by `yaw` around world Y
            // -- replaces the old fixed (0.22, _, cos(tilt)) approximation (a small, non-adjustable lateral
            // nudge) with a real, tunable rotation around the board. +Y from the tilt is unaffected by yaw
            // (still the same elevated look).
            float horiz = (float)Math.Cos(tilt);
            float hx = horiz * (float)Math.Sin(yaw);
            float hz = horiz * (float)Math.Cos(yaw);
            return XnaVector3.Normalize(new XnaVector3(hx, (float)Math.Sin(tilt), hz));
        }

        /// <summary>Shared ortho-camera fit: given a look-at target and a set of (ground point, height
        /// margin above it) samples, points the camera at the fixed tilt/direction above and sizes the
        /// orthographic projection so every sample's ground point AND its height-margin point both land
        /// on screen, with `edgeMargin` world units of breathing room beyond that on every side. Used by
        /// both RebuildCameraFitInstances (fits to wherever instances actually are, for the single-
        /// species stress test) and RebuildCameraArena (fits to the fixed board, fix-round task 3).</summary>
        private void ApplyCamera(XnaVector3 target, IReadOnlyList<(XnaVector3 basePt, float heightMargin)> samples, float edgeMargin, bool preferWidth = false, float zoom = 1f)
        {
            var camDir = CameraDir();
            var camPos = target + camDir * CameraDistance;
            _view = XnaMatrix.CreateLookAt(camPos, target, XnaVector3.Up);

            float minVX = float.MaxValue, maxVX = float.MinValue, minVY = float.MaxValue, maxVY = float.MinValue;
            if (samples.Count == 0)
            {
                minVX = maxVX = minVY = maxVY = 0f;
            }
            foreach (var (basePt, heightMargin) in samples)
            {
                var topPt = basePt + new XnaVector3(0f, heightMargin, 0f);
                var vpBase = XnaVector3.Transform(basePt, _view);
                var vpTop = XnaVector3.Transform(topPt, _view);
                minVX = Math.Min(minVX, Math.Min(vpBase.X, vpTop.X));
                maxVX = Math.Max(maxVX, Math.Max(vpBase.X, vpTop.X));
                minVY = Math.Min(minVY, Math.Min(vpBase.Y, vpTop.Y));
                maxVY = Math.Max(maxVY, Math.Max(vpBase.Y, vpTop.Y));
            }
            float viewSpanX = Math.Max(1.6f, (maxVX - minVX) + edgeMargin * 2f);
            float viewSpanY = Math.Max(1.6f, (maxVY - minVY) + edgeMargin * 2f);

            float aspect = (float)_graphics.PreferredBackBufferHeight / _graphics.PreferredBackBufferWidth;
            // Lead-review fix round: `preferWidth` (arena framing only) makes viewSpanX the binding
            // constraint outright, instead of Math.Max(viewSpanX, viewSpanY / aspect) -- the original
            // "fit both axes, whichever needs more room wins" rule widened orthoWidth whenever the
            // sampled row range's vertical extent (up to 15 rows deep, under the board tilt) exceeded
            // what the portrait aspect would otherwise show, which is exactly what made the arena read
            // as "too far out": the board's own *width* was never actually the limiting factor, its
            // *depth* was. The real 2D battle screen crops rows top/bottom rather than shrinking to fit
            // them all (confirmed against a real `BeastCraft.Desktop --screenshot` battle-opening shot),
            // so arena framing does the same here: width always fills the frame, and a tall arena's
            // far rows may run off the top of the portrait screen instead of shrinking everything to
            // keep them all visible.
            float orthoWidth = preferWidth ? viewSpanX : Math.Max(viewSpanX, viewSpanY / aspect);
            // Producer feedback (third round): `zoom` (>1 = closer) shrinks the fitted ortho size after
            // the fit above, on purpose letting content run off the edges rather than changing what
            // counts as "fitted" -- used only by the default arena shot (see RebuildCameraArena) so units
            // read larger even though that means the far/rear rows and side columns now crop.
            orthoWidth /= zoom;
            float orthoHeight = orthoWidth * aspect;
            _projection = XnaMatrix.CreateOrthographic(orthoWidth, orthoHeight, 0.05f, 50f);
        }

        private void RebuildCamera()
        {
            // "close" (battle mode only): a tight instance-fit close-up, same framing style as the
            // non-battle stress test -- used for the fix round's swarmling close-up shot
            // (--battle --griffins 0 --swarm 1 --zoom close), where the arena-fit framing below would
            // correctly, but unhelpfully, show one tiny swarmling in a full board's worth of empty space.
            if (_options.Battle && !string.Equals(_options.CameraZoom, "close", StringComparison.OrdinalIgnoreCase))
                RebuildCameraArena();
            else
                RebuildCameraFitInstances();
        }

        /// <summary>Single-species stress test (Tab 1/3/12/24): fits the camera to wherever the current
        /// instances actually are, exactly as before the fix round -- unchanged behaviour, just factored
        /// through the shared ApplyCamera helper above.</summary>
        private void RebuildCameraFitInstances()
        {
            NumVector3 boardCenterNum = NumVector3.Zero;
            int centerCount = 0;
            foreach (var inst in _instances)
            {
                boardCenterNum += new NumVector3(inst.World.M41, inst.World.M42, inst.World.M43);
                centerCount++;
            }
            foreach (var w in _swarmWorld)
            {
                boardCenterNum += new NumVector3(w.M41, w.M42, w.M43);
                centerCount++;
            }
            if (centerCount > 0)
                boardCenterNum /= centerCount;
            var target = new XnaVector3(boardCenterNum.X, 0.85f, boardCenterNum.Z);

            var samples = new List<(XnaVector3, float)>();
            foreach (var inst in _instances)
                samples.Add((new XnaVector3(inst.World.M41, inst.World.M42, inst.World.M43), 2.2f));
            foreach (var w in _swarmWorld)
                samples.Add((new XnaVector3(w.M41, w.M42, w.M43), 0.6f)); // swarmlings are much shorter than beasts

            const float margin = 1.4f; // wing spread + outline thickness clearance around each beast
            ApplyCamera(target, samples, margin);
        }

        /// <summary>Fix round, task 3: frames the fixed 11-wide arena (not wherever the current griffins/
        /// swarm happen to be placed) so the battle board fills the screen width the way the task brief
        /// asks -- "portrait 1080x1920, the 11x15 arena filling the width at the board tilt, units sized
        /// to their hex footprint like the 2D sprites". The previous instance-fit camera (still used by
        /// the single-species stress test above) read as "too far out" for the battle scene specifically
        /// because a sparse, off-centre placement (Griffins near row 9, Swarmlings starting row 0 -- see
        /// SetupBattleScene's producer-feedback comment for why that's the near/far split, not the
        /// original's reverse) doesn't, by itself, tell the camera how wide the *board* actually is --
        /// fitting to a fixed grid of sample points across the real arena width/row-range fixes that by
        /// construction, independent of how many units are actually on it. `_options.CameraZoom` ("arena",
        /// the default, vs "front") switches between the full-board shot and a tighter close-up of the
        /// active rows for a readable mid-zoom shot of the front line (both requested by the task
        /// brief).</summary>
        private void RebuildCameraArena()
        {
            bool front = string.Equals(_options.CameraZoom, "front", StringComparison.OrdinalIgnoreCase);
            // "front": a genuinely tighter crop around both active bands (the Griffins' row near the
            // bottom and the Swarmlings' rows near the top -- see SetupBattleScene), not just a shorter
            // row range at the full 11-column width (which, since the ortho fit is column/width-bound in
            // both cases -- see ApplyCamera -- barely changed the framing in a first version of this
            // method: cutting rows alone left orthoWidth unchanged because 11 columns was still the
            // binding constraint). Narrowing the column range too is what actually zooms in. The row range
            // covers 0..9 (Swarmling rows 0-2 through the Griffins' row 9), skipping the empty rows 10-14
            // past the Griffins; the column range is widened slightly from the arena shot's own crop to
            // comfortably cover both the Griffins' column-5 centre and the Swarm's offset column-7 centre
            // (SwarmCenterColOffset).
            int colStart = front ? 1 : 0;
            int colEnd = front ? 10 : 11;
            int rowStart = 0;
            int rowEnd = front ? 10 : 15;

            // Sample every cell centre across the chosen range, not just the four corners: the hex
            // offset-coordinate stagger (HexBoard.CellCenter shifts X by row/2) means the true left/right
            // extent is reached at different rows depending on the stagger direction, which corner-only
            // sampling could under-estimate. At most 11*15 = 165 points -- cheap, done once per scene
            // rebuild, not per frame.
            var samples = new List<(XnaVector3, float)>();
            NumVector3 centerSum = NumVector3.Zero;
            for (int row = rowStart; row < rowEnd; row++)
            {
                for (int col = colStart; col < colEnd; col++)
                {
                    var c = HexBoard.CellCenter(col, row);
                    var pt = new XnaVector3(c.X, c.Y, c.Z);
                    // 2.2: tall enough to cover a standing Griffin's wingtip height (the instance-fit
                    // path's own Griffin margin) -- applied at every cell, not just occupied ones, so
                    // framing is stable regardless of which cells actually hold a beast this run.
                    samples.Add((pt, 2.2f));
                    centerSum += new NumVector3(c.X, c.Y, c.Z);
                }
            }
            centerSum /= Math.Max(1, samples.Count);
            var target = new XnaVector3(centerSum.X, 0.85f, centerSum.Z);

            // Lead-review fix round: a near-zero edge margin (a sliver of the hex's own corner radius),
            // not the previous full-HexSize margin -- the real 2D battle screen runs its hex grid to, and
            // slightly past, the screen edges (edge hexes visibly cropped, confirmed against a real
            // `BeastCraft.Desktop --screenshot` battle shot), not comfortably inset from them.
            // `preferWidth: true` makes the arena's width the sole binding constraint (see ApplyCamera's
            // comment) so a tall row range crops top/bottom instead of shrinking the whole board to fit.
            //
            // Producer feedback (third round): the default "arena" shot -- not "front", already a tighter
            // crop via the narrower column/row range above -- zooms in an extra 20% (units read larger,
            // trading away showing literally all 11x15 cells; far/rear rows and the outermost columns may
            // now run off the frame, judged acceptable by eye as long as the front line and most units
            // stay in view).
            float zoom = front ? 1f : 1.2f;
            ApplyCamera(target, samples, HexBoard.HexSize * 0.1f, preferWidth: true, zoom: zoom);
        }

        protected override void Update(GameTime gameTime)
        {
            double loopMs = _loopStopwatch.Elapsed.TotalMilliseconds;
            _loopStopwatch.Restart();
            if (_firstFrame)
                _firstFrame = false; // skip the startup-to-first-frame interval, not a representative sample
            else
            {
                _stats.RecordFrame(loopMs);
                if (_options.BenchMode)
                    _benchFrameMs.Add(loopMs);
            }

            var kb = Keyboard.GetState();
            if (kb.IsKeyDown(Keys.Escape))
                Exit();

            if (!_options.BenchMode && !_options.ScreenshotMode && !_options.PilotSequenceMode)
            {
                if (WasPressed(kb, Keys.Tab))
                {
                    _stressIndex = (_stressIndex + 1) % StressLevels.Length;
                    SetStressLevel(StressLevels[_stressIndex]);
                }
                if (WasPressed(kb, Keys.C))
                    foreach (var inst in _instances)
                        inst.CrestOn = !inst.CrestOn;
                if (WasPressed(kb, Keys.T))
                    _tintIndex = (_tintIndex + 1) % Tints.Length;
                if (WasPressed(kb, Keys.M))
                    _clip = _clip == AnimatedPose.Clip.Idle ? AnimatedPose.Clip.Move : AnimatedPose.Clip.Idle;
                // Producer feedback: "add a key ... that removes a random swarmling so this can be seen
                // interactively" -- battle mode only (KillRandomSwarmling no-ops harmlessly otherwise,
                // since _swarmInstanceCount is 0 in the single-species stress test, but gating it here
                // keeps the key's effect obviously scoped to the battle scene it was asked for).
                if (_options.Battle && WasPressed(kb, Keys.K))
                    KillRandomSwarmling();
            }
            _prevKeyboard = kb;

            _elapsedSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;

            // Producer feedback: dynamic per-unit facing (battle only -- the single-species stress test
            // keeps its fixed FacingYaw, there being no "enemy" to face there). Must run before the pose/
            // skin work below, which reads each unit's just-updated World matrix.
            if (_options.Battle)
                UpdateFacing((float)gameTime.ElapsedGameTime.TotalSeconds);

            _skinStopwatch.Restart();
            if (_options.PilotSequenceMode)
            {
                // Deterministic, frame-indexed pose (see UpdateInstancePosePilot's doc comment) -- not
                // driven by _elapsedSeconds/real gameTime, so output is reproducible regardless of how
                // fast the real engine loop happens to run.
                foreach (var inst in _instances)
                    UpdateInstancePosePilot(inst);
            }
            else
            {
                foreach (var inst in _instances)
                    UpdateInstancePose(inst);
            }
            UpdateSwarmBones();
            _skinStopwatch.Stop();
            _stats.SkinningMsThisFrame = _skinStopwatch.Elapsed.TotalMilliseconds;

            if (_options.ScreenshotMode)
                _screenshotElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;

            if (_options.BenchMode)
            {
                _benchElapsedSeconds += gameTime.ElapsedGameTime.TotalSeconds;
                if (_benchElapsedSeconds >= _options.BenchSeconds)
                {
                    WriteBenchResult();
                    Exit();
                }
            }

            base.Update(gameTime);
        }

        private bool WasPressed(KeyboardState kb, Keys key) => kb.IsKeyDown(key) && !_prevKeyboard.IsKeyDown(key);

        private static XnaMatrix ToXna(in NumMatrix m) => new XnaMatrix(
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44);

        /// <summary>Per-instance-per-frame CPU work: evaluate the pose (11 joint world matrices, cheap
        /// -- no vertex work happens here any more) and write the resulting bone palette into the
        /// instance's reused XNA Matrix[] arrays, ready to upload to Toon.fx's `Bones` parameter in
        /// DrawInstance. No vertex buffer is touched here -- see GpuMesh/Toon.fx's header comments for
        /// why this replaced the first version's CPU-skin-into-a-DynamicVertexBuffer approach.</summary>
        private void UpdateInstancePose(BeastInstance inst)
        {
            float t = _elapsedSeconds + inst.ClockOffset;
            AnimatedPose.ComputeWorldMatrices(_bodyModel, _clip, t, inst.NodeWorldScratch);
            AnimatedPose.ComputeSkinMatrices(_bodyModel, inst.NodeWorldScratch, inst.World, inst.SkinScratch);
            for (int j = 0; j < inst.SkinScratch.Length && j < BeastInstance.MaxBones; j++)
                inst.BonePalette[j] = ToXna(inst.SkinScratch[j]);

            if (inst.CrestOn)
            {
                var crestTransform = CrestLocal * inst.NodeWorldScratch[_headNodeIndex] * inst.World;
                inst.CrestPalette[0] = ToXna(crestTransform);
            }
        }

        // Anim-pilot griffin (issue #68): the crossfaded Idle->Move->Attack->Idle reel's segment timing
        // (seconds). Tuned by eye against the review GIF, not derived from the clips' own authored
        // lengths (Idle=3s/Move=1s/Attack=1s, Tooling/Animation/anim/{keyed,gait}.py) -- the reel plays a
        // shorter slice of Idle/Move than their own full loop length so the reel itself stays a readable
        // few seconds, not a literal concatenation of every clip's full duration.
        private const float PilotIdleInLen = 1.5f;
        private const float PilotMoveLen = 1.2f;
        private const float PilotAttackLen = 1.0f;
        private const float PilotCrossfade = 0.2f;
        // Round 15: the reel now sequences all 7 clips, in the order the task brief specifies --
        // idle -> move -> attack -> hit -> cast -> victory -> KO -- each held for a representative
        // slice (full duration for the three new one-shot/short clips, same trimmed slices as
        // before for idle/move/attack), crossfaded between every pair. KO is last and does NOT
        // crossfade back to idle (it's a deliberate non-looping end-state -- see keyed.py) -- the
        // reel simply ends on KO's held final pose.
        private static readonly (AnimatedPose.Clip Clip, float Len)[] PilotReelSegments =
        {
            (AnimatedPose.Clip.Idle, PilotIdleInLen),
            (AnimatedPose.Clip.Move, PilotMoveLen),
            (AnimatedPose.Clip.Attack, PilotAttackLen),
            (AnimatedPose.Clip.Hit, 0.5f),
            (AnimatedPose.Clip.Cast, 1.2f),
            (AnimatedPose.Clip.Victory, 2.0f),
            (AnimatedPose.Clip.KO, 1.4f),
        };

        private static float PilotReelDuration()
        {
            float total = 0f;
            for (int i = 0; i < PilotReelSegments.Length; i++)
            {
                total += PilotReelSegments[i].Len;
                if (i < PilotReelSegments.Length - 1)
                    total += PilotCrossfade;
            }
            return total;
        }

        private float ClipDuration(AnimatedPose.Clip clip)
        {
            var anim = clip switch
            {
                AnimatedPose.Clip.Idle => _bodyModel.IdleAnimation,
                AnimatedPose.Clip.Move => _bodyModel.MoveAnimation,
                AnimatedPose.Clip.Attack => _bodyModel.AttackAnimation,
                AnimatedPose.Clip.Cast => _bodyModel.CastAnimation,
                AnimatedPose.Clip.Hit => _bodyModel.HitAnimation,
                AnimatedPose.Clip.KO => _bodyModel.KOAnimation,
                AnimatedPose.Clip.Victory => _bodyModel.VictoryAnimation,
                _ => null,
            };
            return anim?.Duration ?? 1f;
        }

        /// <summary>How many frames --pilot-sequence should render in total: one pass over the crossfaded
        /// reel's own duration at --pilot-fps for "reel" (the GIF deliverable), or --frames evenly spaced
        /// across exactly one loop of the named clip's own duration for a single clip (the contact-sheet
        /// deliverable -- Tooling/Animation/README.md's gate).</summary>
        private int TotalPilotFrames()
        {
            if (string.Equals(_options.PilotClip, "reel", StringComparison.OrdinalIgnoreCase))
                return Math.Max(1, (int)MathF.Ceiling(PilotReelDuration() * _options.PilotFps));
            return Math.Max(1, _options.PilotFrames);
        }

        private static AnimatedPose.Clip ParseClipName(string name) => name.ToLowerInvariant() switch
        {
            "move" => AnimatedPose.Clip.Move,
            "attack" => AnimatedPose.Clip.Attack,
            "cast" => AnimatedPose.Clip.Cast,
            "hit" => AnimatedPose.Clip.Hit,
            "ko" => AnimatedPose.Clip.KO,
            "victory" => AnimatedPose.Clip.Victory,
            _ => AnimatedPose.Clip.Idle,
        };

        /// <summary>Round 16: per-joint outline-thickness mask for Toon.fx's Outline technique (see its
        /// BoneOutlineMask comment for the full root-cause writeup -- confirmed, not assumed, via a
        /// --pilot-no-outline A/B capture). 1.0 (full outline) for every joint by default; ZERO for the
        /// wing bones (wing_L_01..03/wing_R_01..03 -- thin, loosely-connected feather-card islands whose
        /// inverted hull pokes through when a card turns edge-on to the camera) and the tail's last
        /// segment specifically (tail_04, the fluffy tip/"tuft" -- the task brief's own wording) rather
        /// than the whole tail, so the tail's own silhouette against the body still reads normally along
        /// tail_01-03. A first attempt at 0.15 (a visible-but-thin fringe, not fully off) still left a
        /// smaller residual dark patch at the most extreme edge-on wing angles (confirmed by re-rendering
        /// Attack's strike frame and zooming in) -- any non-zero push-out distance on a thin,
        /// inconsistently-wound card can still poke through at a steep enough viewing angle, so this
        /// settled on fully skipping the hull for these bones (mask 0.0) rather than just thinning it,
        /// matching the task brief's other explicitly-offered option ("skip the hull for wing
        /// submeshes"). The wing's own silhouette against the background still reads fine from the toon
        /// fill pass's own texture edge; only the per-card inverted-hull fringe is gone. Indexed by
        /// skin-joint slot, same order as Bones[]/model.Joints -- any slot beyond model.Joints.Length
        /// (padding up to BeastInstance.MaxBones) stays at the harmless default, 1.0, since nothing ever
        /// indexes it.</summary>
        private static float[] BuildBoneOutlineMask(GltfSkinnedModel model)
        {
            var mask = new float[BeastInstance.MaxBones];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = 1f;
            for (int j = 0; j < model.Joints.Length && j < mask.Length; j++)
            {
                string name = model.Joints[j].Node.Name ?? string.Empty;
                bool isWing = name.StartsWith("wing_L_", StringComparison.OrdinalIgnoreCase) ||
                              name.StartsWith("wing_R_", StringComparison.OrdinalIgnoreCase);
                bool isScapula = name.StartsWith("scapula_", StringComparison.OrdinalIgnoreCase);
                bool isTailTuft = string.Equals(name, "tail_04", StringComparison.OrdinalIgnoreCase);
                if (isWing || isScapula || isTailTuft)
                    mask[j] = 0f;
            }
            return mask;
        }

        /// <summary>Reads export_glb.py's griffin_anim_events.json sidecar (Tooling/Animation/anim/
        /// keyed.py's EVENT_MARKERS, re-keyed by export_glb.py -- see its header comment) into a
        /// clip-name-keyed lookup of (marker name, fraction-through-clip). Tolerant of a missing file
        /// (older/rebuilt content without the round-15 clips) and of any parse failure -- this is a
        /// pilot-harness diagnostic aid, not gameplay-critical, so a bad/missing sidecar just means no
        /// markers get logged, never a crash.</summary>
        private static Dictionary<string, List<(string Name, float Fraction)>> LoadPilotEventMarkers(string path)
        {
            var result = new Dictionary<string, List<(string Name, float Fraction)>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return result;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty("markers", out var markersEl))
                    markersEl = doc.RootElement; // tolerate a sidecar that's just {clip: [...]} with no wrapper
                foreach (var clipProp in markersEl.EnumerateObject())
                {
                    var list = new List<(string Name, float Fraction)>();
                    foreach (var markerEl in clipProp.Value.EnumerateArray())
                    {
                        string name = markerEl.TryGetProperty("name", out var n) ? n.GetString() : "marker";
                        float fraction = markerEl.TryGetProperty("fraction", out var f) ? f.GetSingle() : 0f;
                        list.Add((name, fraction));
                    }
                    result[clipProp.Name] = list;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[pilot] warning: failed to load event markers from {path}: {ex.Message}");
            }
            return result;
        }

        /// <summary>Reads the same griffin_anim_events.json sidecar's sibling "loop" map (Tooling/
        /// Animation/anim/keyed.py's CLIP_LOOP, forwarded by export_glb.py) -- clip name to whether its
        /// time should wrap (a seamless loop) or clamp to its last keyframe (a one-shot action). See
        /// LoopFor for the default when a clip/the whole sidecar is missing. Same tolerance as
        /// LoadPilotEventMarkers: a bad/missing file just means every clip falls back to wrapping
        /// (the pre-round-16 behaviour), never a crash.</summary>
        private static Dictionary<string, bool> LoadPilotClipLoop(string path)
        {
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return result;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("loop", out var loopEl))
                {
                    foreach (var clipProp in loopEl.EnumerateObject())
                        result[clipProp.Name] = clipProp.Value.GetBoolean();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[pilot] warning: failed to load clip loop flags from {path}: {ex.Message}");
            }
            return result;
        }

        /// <summary>Whether `clip`'s time should wrap (true) or clamp to its last keyframe (false) --
        /// looks up _pilotClipLoop by clip name, defaulting to true (wrap, the original behaviour) for
        /// any clip the sidecar doesn't mention or when there's no sidecar at all (e.g. griffin_live.glb,
        /// which never runs through the pilot path anyway).</summary>
        private bool LoopFor(AnimatedPose.Clip clip) =>
            _pilotClipLoop == null || !_pilotClipLoop.TryGetValue(clip.ToString(), out var loop) || loop;

        /// <summary>Called once per captured --pilot-sequence frame (not per instance -- markers are a
        /// property of the reel's timeline, not of any one beast) with the just-rendered frame's
        /// (clipFrom, tFrom) from GetPilotPoseParams. Logs (Console.WriteLine) the first captured frame
        /// whose time has reached or passed each of that clip's markers, exactly once per crossing --
        /// tracks the previous captured frame's (clip, time) in _pilotMarkerLast* to detect the crossing.
        /// A clip change (including the reel wrapping back to its first segment) resets the tracked time
        /// to "before the clip start" so that clip's own markers can still fire from frame zero.</summary>
        private void LogPilotEventMarkerCrossings(AnimatedPose.Clip clip, float timeInClip)
        {
            if (_pilotEventMarkers == null || _pilotEventMarkers.Count == 0)
                return;
            if (clip != _pilotMarkerLastClip || timeInClip < _pilotMarkerLastTime)
            {
                // New clip segment (or a loop wrap within the same clip): start fresh so this
                // segment's own markers aren't skipped as "already passed".
                _pilotMarkerLastClip = clip;
                _pilotMarkerLastTime = -1f;
            }

            if (_pilotEventMarkers.TryGetValue(clip.ToString(), out var markers))
            {
                float duration = ClipDuration(clip);
                foreach (var (name, fraction) in markers)
                {
                    float markerTime = fraction * duration;
                    if (markerTime > _pilotMarkerLastTime && markerTime <= timeInClip)
                    {
                        Console.WriteLine(
                            $"[pilot] event marker '{name}' fired -- clip={clip} frame={_pilotFrameIndex} t={timeInClip:F3}s (fraction {fraction:F2})");
                    }
                }
            }

            _pilotMarkerLastTime = timeInClip;
        }

        /// <summary>Maps a captured frame index to the (fromClip, fromTime, toClip, toTime, blend) that
        /// AnimatedPose.ComputeWorldMatricesBlended needs. blend=0 means "fully fromClip" (toClip/toTime
        /// are unused in that case but still well-defined, same clip/time as from).</summary>
        private void GetPilotPoseParams(int frameIndex, out AnimatedPose.Clip clipFrom, out float tFrom,
            out AnimatedPose.Clip clipTo, out float tTo, out float blend)
        {
            if (!string.Equals(_options.PilotClip, "reel", StringComparison.OrdinalIgnoreCase))
            {
                var clip = ParseClipName(_options.PilotClip);
                float duration = ClipDuration(clip);
                int frames = Math.Max(1, _options.PilotFrames);
                float t = frames > 1 ? frameIndex / (float)(frames - 1) * duration : 0f;
                clipFrom = clipTo = clip;
                tFrom = tTo = t;
                blend = 0f;
                return;
            }

            // Generic N-segment crossfaded sequence (round 15 -- replaces the old hard-coded
            // idle/move/attack-only 7-boundary version with a data-driven walk over
            // PilotReelSegments, so adding/reordering clips doesn't need new boundary variables).
            float reelDuration = PilotReelDuration();
            float simTime = frameIndex / (float)Math.Max(1, _options.PilotFps);
            float t2 = simTime % reelDuration;

            float cursor = 0f;
            for (int i = 0; i < PilotReelSegments.Length; i++)
            {
                var (clip, len) = PilotReelSegments[i];
                float holdEnd = cursor + len;
                if (t2 < holdEnd || i == PilotReelSegments.Length - 1)
                {
                    clipFrom = clipTo = clip;
                    tFrom = tTo = Math.Max(0f, t2 - cursor);
                    blend = 0f;
                    return;
                }
                float fadeEnd = holdEnd + PilotCrossfade;
                if (t2 < fadeEnd)
                {
                    var next = PilotReelSegments[i + 1].Clip;
                    clipFrom = clip;
                    clipTo = next;
                    tFrom = t2 - cursor;
                    tTo = t2 - holdEnd;
                    blend = (t2 - holdEnd) / PilotCrossfade;
                    return;
                }
                cursor = fadeEnd;
            }
            // Unreachable (the loop's last-segment branch above always returns), but keeps the
            // compiler happy about definite assignment.
            clipFrom = clipTo = AnimatedPose.Clip.Idle;
            tFrom = tTo = 0f;
            blend = 0f;
        }

        /// <summary>Pilot-mode equivalent of UpdateInstancePose: evaluates a crossfaded pose (see
        /// GetPilotPoseParams) instead of a single clip, then layers runtime spring bones (SpringBone.cs)
        /// on the tracked tail/wing joints before computing skin matrices. Uses a fixed per-captured-frame
        /// dt (1/--pilot-fps), not real elapsed time -- --pilot-sequence advances exactly one output frame
        /// per Update() call regardless of the engine's real frame rate (see Draw()'s capture block), so
        /// the spring simulation needs a matching fixed step to stay deterministic/reproducible.</summary>
        private void UpdateInstancePosePilot(BeastInstance inst)
        {
            float dt = 1f / Math.Max(1, _options.PilotFps);
            GetPilotPoseParams(_pilotFrameIndex, out var clipFrom, out var tFrom, out var clipTo, out var tTo, out var blend);
            AnimatedPose.ComputeWorldMatricesBlended(_bodyModel, clipFrom, tFrom, clipTo, tTo, blend,
                inst.NodeWorldScratch, LoopFor(clipFrom), LoopFor(clipTo));

            ApplySpringJoint(ref inst.TailSpring, _springTail, inst.NodeWorldScratch, inst.World, dt);
            ApplySpringJoint(ref inst.WingLSpring, _springWingL, inst.NodeWorldScratch, inst.World, dt);
            ApplySpringJoint(ref inst.WingRSpring, _springWingR, inst.NodeWorldScratch, inst.World, dt);

            AnimatedPose.ComputeSkinMatrices(_bodyModel, inst.NodeWorldScratch, inst.World, inst.SkinScratch);
            for (int j = 0; j < inst.SkinScratch.Length && j < BeastInstance.MaxBones; j++)
                inst.BonePalette[j] = ToXna(inst.SkinScratch[j]);

            if (inst.CrestOn)
            {
                var crestTransform = CrestLocal * inst.NodeWorldScratch[_headNodeIndex] * inst.World;
                inst.CrestPalette[0] = ToXna(crestTransform);
            }
        }

        /// <summary>Nudges `nodeWorld[config.NodeIndex]` (in-place) so the joint's effective WORLD
        /// position lags toward a damped-spring-simulated point instead of snapping straight to the baked
        /// clip pose -- see SpringBone.cs's header comment for why this is a single point-mass spring per
        /// joint, not a real chain solver. No-ops if `config` has no resolved node (a model with no
        /// matching bone name, e.g. griffin_live.glb).</summary>
        private static void ApplySpringJoint(ref SpringJointState state, SpringJointConfig config,
            NumMatrix[] nodeWorld, NumMatrix instanceWorld, float dt)
        {
            if (!config.IsValid)
                return;
            var baked = nodeWorld[config.NodeIndex];
            var bakedWorldPos = NumVector3.Transform(NumVector3.Zero, baked * instanceWorld);
            state.Update(bakedWorldPos, config.Stiffness, config.Damping, dt);
            var worldOffset = state.Offset(bakedWorldPos);
            var invWorld = NumMatrix.Invert(instanceWorld, out var inv) ? inv : NumMatrix.Identity;
            var localOffset = NumVector3.TransformNormal(worldOffset, invWorld);
            nodeWorld[config.NodeIndex] = baked * NumMatrix.CreateTranslation(localOffset);
        }

        /// <summary>The whole swarm's per-frame CPU cost: for each instance, evaluate its pose via
        /// AnimatedPose (exactly like UpdateInstancePose does for a Griffin -- real interpolated
        /// animation sampling, not the placeholder's nearest-baked-frame snap) and write the resulting
        /// skin matrices into that instance's slice of its batch's flat SwarmBoneRows palette. Each
        /// row-vector skin matrix (inverseBind * jointWorld * instanceWorld, translation in row 4) is
        /// transposed before upload: Toon.fx's SkinSwarmPositionNormal reconstructs a compact 3x4 affine
        /// transform from 3 plain float4 "rows" via a row-dot against [position,1] -- after transposing,
        /// the transposed matrix's first 3 ROWS are exactly (Xaxis.x, Yaxis.x, Zaxis.x, Tx),
        /// (Xaxis.y, ..., Ty), (Xaxis.z, ..., Tz) -- i.e. row i dotted with [pos,1] gives world axis i
        /// directly, which is what the shader needs (see Toon.fx's header comment for the full
        /// derivation). The transposed matrix's 4th row is always (0,0,0,1) for an affine transform and
        /// is simply not uploaded (the shader never reads a "row 3").</summary>
        private void UpdateSwarmBones()
        {
            if (_swarmInstanceCount == 0)
                return;
            int boneCount = _swarmBonesPerInstance;
            for (int i = 0; i < _swarmInstanceCount; i++)
            {
                int batchIndex = i / SwarmBatchCapacity;
                int localId = i % SwarmBatchCapacity;
                var rows = _swarmBoneRowsPalette[batchIndex];
                int baseRow = localId * boneCount * 3;

                if (!_swarmAlive[i])
                {
                    // Producer feedback (K / --kill N): a dead Swarmling gets an all-zero skin instead of
                    // a real pose -- every vertex referencing these rows (with any blend weight) maps to
                    // (0,0,0) regardless of its bind-pose position, collapsing the whole instance to a
                    // single point (zero-area triangles, nothing rasterises). Cheaper than skipping it in
                    // the draw call too: the merged batch has no per-instance visibility flag, and adding
                    // one would cost a CPU-side vertex/index buffer rebuild per kill instead of just not
                    // calling AnimatedPose for this slot.
                    for (int r = baseRow; r < baseRow + boneCount * 3; r++)
                        rows[r] = XnaVector4.Zero;
                    continue;
                }

                float t = _elapsedSeconds + _swarmClockOffsets[i];
                AnimatedPose.ComputeWorldMatrices(_swarmModel, _clip, t, _swarmNodeWorldScratch[i]);
                AnimatedPose.ComputeSkinMatrices(_swarmModel, _swarmNodeWorldScratch[i], _swarmWorld[i], _swarmSkinScratch[i]);

                for (int b = 0; b < boneCount; b++)
                {
                    var skinT = Matrix4x4.Transpose(_swarmSkinScratch[i][b]);
                    int r = baseRow + b * 3;
                    rows[r + 0] = new XnaVector4(skinT.M11, skinT.M12, skinT.M13, skinT.M14);
                    rows[r + 1] = new XnaVector4(skinT.M21, skinT.M22, skinT.M23, skinT.M24);
                    rows[r + 2] = new XnaVector4(skinT.M31, skinT.M32, skinT.M33, skinT.M34);
                }
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            _stats.DrawCallsThisFrame = 0;
            _stats.TrianglesThisFrame = 0;

            GraphicsDevice.Clear(new XnaColor(10, 10, 14));

            DrawBackdrop();
            DrawHexGrid();
            DrawBeasts();
            DrawSwarm();

            // Lead-review fix round: screenshots hide the debug stats overlay unconditionally now (so
            // they read like the actual game, not a debug HUD) -- previously only `--hide-stats` did
            // that, so every screenshot in the spike doc up to and including the last pass carried the
            // fps/draw-call/memory text baked into the image. `--hide-stats` still parses (now a no-op
            // for screenshots specifically) rather than erroring on old invocations.
            bool showStats = !_options.BenchMode && !_options.ScreenshotMode && !_options.PilotSequenceMode;
            if (showStats)
                DrawStatsOverlay();

            base.Draw(gameTime);

            if (_options.ScreenshotMode && _screenshotElapsedSeconds >= ScreenshotWarmupSeconds)
            {
                SaveScreenshot(_options.ScreenshotPath);
                Exit();
            }

            // Anim-pilot griffin (issue #68): capture one numbered PNG per Update()/Draw() pair once the
            // window has had a few frames to settle (same reasoning as --screenshot's warmup, just frame-
            // counted instead of time-counted since pilot time itself is frame-indexed -- see
            // UpdateInstancePosePilot). Advances _pilotFrameIndex (consumed by GetPilotPoseParams next
            // Update()) and exits once TotalPilotFrames() frames have been written.
            if (_options.PilotSequenceMode)
            {
                if (_pilotWarmupFramesLeft > 0)
                {
                    _pilotWarmupFramesLeft--;
                }
                else
                {
                    // Round 15: log any event marker (e.g. Cast's "cast_release", Hit's "hit_react" --
                    // Tooling/Animation/anim/keyed.py) crossed by the frame about to be captured, using
                    // the same (clipFrom, tFrom) GetPilotPoseParams already computed for this frame's
                    // pose in UpdateInstancePosePilot.
                    GetPilotPoseParams(_pilotFrameIndex, out var markerClip, out var markerTime, out _, out _, out _);
                    LogPilotEventMarkerCrossings(markerClip, markerTime);

                    // SaveScreenshot itself creates the output directory if needed.
                    string path = Path.Combine(_options.PilotSequenceDir, $"frame_{_pilotFrameIndex:D4}.png");
                    SaveScreenshot(path);
                    _pilotFrameIndex++;
                    if (_pilotFrameIndex >= TotalPilotFrames())
                        Exit();
                }
            }
        }

        private void DrawBackdrop()
        {
            if (_backdropTexture == null)
                return;
            GraphicsDevice.DepthStencilState = DepthStencilState.None;
            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            _backdropEffect.Texture = _backdropTexture;
            var quad = new[]
            {
                new VertexPositionTexture(new XnaVector3(-1, -1, 0), new XnaVector2(0, 1)),
                new VertexPositionTexture(new XnaVector3(-1, 1, 0), new XnaVector2(0, 0)),
                new VertexPositionTexture(new XnaVector3(1, -1, 0), new XnaVector2(1, 1)),
                new VertexPositionTexture(new XnaVector3(1, 1, 0), new XnaVector2(1, 0)),
            };
            foreach (var pass in _backdropEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2);
            }
            _stats.DrawCallsThisFrame++;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        }

        private void DrawHexGrid()
        {
            GraphicsDevice.SetVertexBuffer(_hexGridVertexBuffer);
            _lineEffect.World = XnaMatrix.Identity;
            _lineEffect.View = _view;
            _lineEffect.Projection = _projection;
            GraphicsDevice.BlendState = BlendState.AlphaBlend;
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in _lineEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawPrimitives(PrimitiveType.LineList, 0, _hexGridVertexCount / 2);
            }
            _stats.DrawCallsThisFrame++;
        }

        private void DrawBeasts()
        {
            var viewProjection = _view * _projection;
            _paramViewProjection.SetValue(viewProjection);
            _paramTintMultiply.SetValue(Tints[_tintIndex]);

            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            // glTF/OpenGL's winding convention is the opposite of MonoGame/XNA's default
            // (RasterizerState.CullCounterClockwise, which treats *clockwise* as front-facing) -- the
            // griffin_live.glb index data was loaded as-is (no re-winding), so the front-facing set
            // under glTF's right-handed CCW convention would be CullClockwise's kept set here.
            //
            // Round 9 (anim-pilot griffin, lead review): the main toon pass is drawn double-sided
            // (CullNone) instead, not single-sided -- griffin_anim.glb's wings are a bundle of many
            // (7-8 per wing) only loosely-connected feather-card islands (see Tooling/Animation/
            // prep_mesh.py's docstring), each decimated independently, with no reliable way to force
            // every card's winding consistently outward (tried: Blender's own
            // normals_make_consistent "inside/outside" heuristic guessed wrong often enough on
            // these thin disconnected cards to make the dark gaps WORSE, not better -- confirmed by
            // rendering actual Attack frames before reverting that attempt). A single-sided,
            // winding-dependent cull was always going to be fragile against a wing made of many
            // independent islands; double-sided removes the dependency on winding being consistent
            // at all, at the cost of drawing each triangle's backface too -- a small, acceptable
            // GPU cost for this pilot's mesh budgets, and harmless on any OTHER beast's mesh that IS
            // a clean, consistently-wound single shell (CullNone draws its backfaces too, but they
            // sit behind the already-drawn front faces in the depth buffer, so nothing visible
            // changes there).
            GraphicsDevice.RasterizerState = RasterizerState.CullNone;
            var toonTechnique = _toonEffect.Techniques["Toon"];
            foreach (var inst in _instances)
                DrawInstance(inst, toonTechnique);

            // Inverted-hull outline pass: reversed culling, so only the expanded shell's silhouette shows.
            // Round 16: --pilot-no-outline skips this entirely (diagnostic only -- see LaunchOptions.
            // PilotNoOutline's comment).
            if (!_options.PilotNoOutline)
            {
                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                var outlineTechnique = _toonEffect.Techniques["Outline"];
                foreach (var inst in _instances)
                    DrawInstance(inst, outlineTechnique);
            }
        }

        private void DrawInstance(BeastInstance inst, EffectTechnique technique)
        {
            _paramBaseTexture.SetValue(_baseColorTexture);
            _paramBones.SetValue(inst.BonePalette);
            GraphicsDevice.SetVertexBuffer(_bodyMesh.Vertices);
            GraphicsDevice.Indices = _bodyMesh.Indices;
            foreach (var pass in technique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _bodyMesh.TriangleCount);
            }
            _stats.DrawCallsThisFrame++;
            _stats.TrianglesThisFrame += _bodyMesh.TriangleCount;

            if (inst.CrestOn)
            {
                _paramBaseTexture.SetValue(_crestTexture);
                _paramBones.SetValue(inst.CrestPalette);
                GraphicsDevice.SetVertexBuffer(_crestMesh.Vertices);
                GraphicsDevice.Indices = _crestMesh.Indices;
                foreach (var pass in technique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _crestMesh.TriangleCount);
                }
                _stats.DrawCallsThisFrame++;
                _stats.TrianglesThisFrame += _crestMesh.TriangleCount;
            }
        }

        /// <summary>Draws the whole swarm across its batches (SwarmBatchCapacity swarmlings each) -- 2
        /// draw calls per batch (toon fill + outline), so a 24-swarmling battle draws 4 total instead of
        /// the single-merged-buffer design's 2 (see Toon.fx's header comment for why the register budget
        /// forced this at the real rig's 6 bones/swarmling). ViewProjection/TintMultiply are already set
        /// by DrawBeasts, called just before this every frame.</summary>
        private void DrawSwarm()
        {
            if (_swarmInstanceCount == 0)
                return;

            _paramBaseTexture.SetValue(_swarmTexture);

            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            for (int b = 0; b < _swarmBatches.Count; b++)
            {
                var batch = _swarmBatches[b];
                _paramSwarmBoneRows.SetValue(_swarmBoneRowsPalette[b]);
                GraphicsDevice.SetVertexBuffer(batch.Vertices);
                GraphicsDevice.Indices = batch.Indices;

                GraphicsDevice.RasterizerState = RasterizerState.CullClockwise;
                foreach (var pass in _toonEffect.Techniques["ToonSwarm"].Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.TriangleCount);
                }
                _stats.DrawCallsThisFrame++;
                _stats.TrianglesThisFrame += batch.TriangleCount;

                GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                foreach (var pass in _toonEffect.Techniques["OutlineSwarm"].Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, batch.TriangleCount);
                }
                _stats.DrawCallsThisFrame++;
                _stats.TrianglesThisFrame += batch.TriangleCount;
            }
        }

        private void DrawStatsOverlay()
        {
            _spriteBatch.Begin();
            long managedBytes = StatsTracker.EstimateManagedBytes();
            long gpuEstimateBytes = EstimateGpuBytes();
            int swarmAliveCount = 0;
            for (int i = 0; i < _swarmInstanceCount; i++)
                if (_swarmAlive[i])
                    swarmAliveCount++;
            string text =
                $"beasts: {_instances.Count}  swarm: {swarmAliveCount}/{_swarmInstanceCount}  clip: {_clip}  crest: {(_instances.Count > 0 && _instances[0].CrestOn ? "on" : "off")}  tint: {_tintIndex}\n" +
                $"fps avg: {_stats.AverageFps():0.0}  fps 1% low: {_stats.OnePercentLowFps():0.0}\n" +
                $"draw calls: {_stats.DrawCallsThisFrame}  tris: {_stats.TrianglesThisFrame:N0}\n" +
                $"skin: {_stats.SkinningMsEma:0.00} ms  managed: {managedBytes / (1024.0 * 1024.0):0.0} MB  gpu(est): {gpuEstimateBytes / (1024.0 * 1024.0):0.0} MB\n" +
                (_options.Battle
                    ? "Tab: stress 1/3/12/24   C: crest   T: tint   M: idle/move   K: kill swarmling   Esc: quit"
                    : "Tab: stress 1/3/12/24   C: crest   T: tint   M: idle/move   Esc: quit");
            _spriteBatch.DrawString(_font, text, new XnaVector2(12, 12), XnaColor.White);
            _spriteBatch.End();
        }

        private long EstimateGpuBytes()
        {
            // GPU skinning (lead-review fix round) means the vertex buffers are shared/static now, not
            // per-instance -- one body + one crest buffer total, regardless of beast count. Per-instance
            // GPU cost is now just the small Bones[] uniform upload (16 float4x4 = 1 KiB), not counted
            // here (it's uniform/constant memory, not buffer memory, and is reused/overwritten per draw
            // rather than resident per instance).
            long vb = (long)_bodyModel.Positions.Length * SkinnedVertex.VertexDeclaration.VertexStride
                    + (long)_crestModel.Positions.Length * SkinnedVertex.VertexDeclaration.VertexStride;
            long ib = (long)(_bodyModel.Indices.Length + _crestModel.Indices.Length) * sizeof(short);
            long tex = _baseColorTexture != null ? (long)_baseColorTexture.Width * _baseColorTexture.Height * 4 : 0;
            long backdrop = _backdropTexture != null ? (long)_backdropTexture.Width * _backdropTexture.Height * 4 : 0;

            // Swarm (fifth pass): each batch's merged VB scales with that batch's instance count (every
            // copy repeats the same ~620-vertex swarmling), but the shared swatch texture is built/
            // loaded ONCE regardless of swarm size; the per-batch bone-row palettes are managed arrays,
            // not GPU resources, so not counted here.
            long swarmVb = 0, swarmIb = 0;
            foreach (var batch in _swarmBatches)
            {
                swarmVb += (long)batch.Vertices.VertexCount * SwarmVertex.VertexDeclaration.VertexStride;
                swarmIb += (long)batch.Indices.IndexCount * sizeof(short);
            }
            long swarmTex = _swarmTexture != null ? (long)_swarmTexture.Width * _swarmTexture.Height * 4 : 0;

            return vb + ib + tex + backdrop + swarmVb + swarmIb + swarmTex;
        }

        private void SaveScreenshot(string path)
        {
            var pp = GraphicsDevice.PresentationParameters;
            int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
            var data = new XnaColor[w * h];
            GraphicsDevice.GetBackBufferData(data);
            using (var tex = new Texture2D(GraphicsDevice, w, h))
            {
                tex.SetData(data);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                using (var fs = File.Create(path))
                    tex.SaveAsPng(fs, w, h);
            }
            Console.WriteLine($"Screenshot written: {path}");
        }

        private void WriteBenchResult()
        {
            double meanMs = 0;
            foreach (var ms in _benchFrameMs)
                meanMs += ms;
            meanMs = _benchFrameMs.Count > 0 ? meanMs / _benchFrameMs.Count : 0;
            var sorted = _benchFrameMs.ConvertAll(x => x);
            sorted.Sort();
            sorted.Reverse();
            int onePercentN = Math.Max(1, (int)Math.Ceiling(sorted.Count * 0.01));
            double onePercentMean = 0;
            for (int i = 0; i < onePercentN; i++)
                onePercentMean += sorted[i];
            onePercentMean /= onePercentN;

            long managedBytes = StatsTracker.EstimateManagedBytes();
            long gpuBytes = EstimateGpuBytes();
            int gc0 = GC.CollectionCount(0) - _gc0Start;
            int gc1 = GC.CollectionCount(1) - _gc1Start;
            int gc2 = GC.CollectionCount(2) - _gc2Start;

            string json = "{\n" +
                $"  \"beasts\": {_instances.Count},\n" +
                $"  \"swarm\": {_swarmInstanceCount},\n" +
                $"  \"fpsCap\": {_options.FpsCap},\n" +
                $"  \"seconds\": {_benchElapsedSeconds:0.###},\n" +
                $"  \"frameCount\": {_benchFrameMs.Count},\n" +
                $"  \"fpsAvg\": {(meanMs > 0 ? 1000.0 / meanMs : 0):0.###},\n" +
                $"  \"fps1PercentLow\": {(onePercentMean > 0 ? 1000.0 / onePercentMean : 0):0.###},\n" +
                $"  \"frameMsAvg\": {meanMs:0.###},\n" +
                $"  \"drawCalls\": {_stats.DrawCallsThisFrame},\n" +
                $"  \"triangles\": {_stats.TrianglesThisFrame},\n" +
                $"  \"skinningMsAvg\": {_stats.SkinningMsEma:0.###},\n" +
                $"  \"managedMemoryBytes\": {managedBytes},\n" +
                $"  \"gpuMemoryEstimateBytes\": {gpuBytes},\n" +
                $"  \"gcGen0Collections\": {gc0},\n" +
                $"  \"gcGen1Collections\": {gc1},\n" +
                $"  \"gcGen2Collections\": {gc2}\n" +
                "}\n";
            File.WriteAllText(_options.BenchOutPath, json);
            Console.WriteLine($"Bench result written: {_options.BenchOutPath}");
            Console.WriteLine(json);
        }
    }
}
