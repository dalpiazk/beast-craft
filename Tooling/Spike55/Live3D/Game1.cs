using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NumMatrix = System.Numerics.Matrix4x4;
using NumVector3 = System.Numerics.Vector3;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

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

        // Cached effect parameters (avoid a string-keyed lookup in EffectParameterCollection every draw
        // call -- looked up once here instead of via Parameters["..."] in the hot path).
        private EffectParameter _paramViewProjection;
        private EffectParameter _paramTintMultiply;
        private EffectParameter _paramBaseTexture;
        private EffectParameter _paramBones;

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
            // Portrait 1080x1920 is the task brief's target; scaled down 50% here so the window fits a
            // normal desktop monitor without the person having to move it (see README "Window size").
            _graphics.PreferredBackBufferWidth = 540;
            _graphics.PreferredBackBufferHeight = 960;
            _graphics.SynchronizeWithVerticalRetrace = !_options.BenchMode; // uncapped in --bench, for a true throughput number
            IsFixedTimeStep = false;
            _graphics.ApplyChanges();
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _font = Content.Load<SpriteFont>("DebugFont");
            _toonEffect = Content.Load<Effect>("Effects/Toon");

            string contentRoot = Path.Combine(AppContext.BaseDirectory, "Content");
            _bodyModel = GltfSkinnedModel.Load(Path.Combine(contentRoot, "model", "griffin_live.glb"));
            _crestModel = GltfSkinnedModel.Load(Path.Combine(contentRoot, "model", "crest_alt.glb"));
            _headNodeIndex = AnimatedPose.FindNodeIndexByName(_bodyModel, "bone_head");

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

            var gridVerts = HexBoard.BuildGridLines(HexBoard.ArenaWidth, HexBoard.ArenaHeight, new XnaColor(46, 42, 69, 140));
            _hexGridVertexBuffer = new VertexBuffer(GraphicsDevice, VertexPositionColor.VertexDeclaration, gridVerts.Length, BufferUsage.WriteOnly);
            _hexGridVertexBuffer.SetData(gridVerts);
            _hexGridVertexCount = gridVerts.Length;

            SetStressLevel(_options.BenchMode ? _options.BenchBeasts : (_options.ScreenshotMode ? _options.ScreenshotBeasts : 1));
            if (!_options.ScreenshotCrestOn)
                foreach (var inst in _instances)
                    inst.CrestOn = false;
            _tintIndex = Math.Clamp(_options.ScreenshotTint, 0, Tints.Length - 1);

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

        private void RebuildCamera()
        {
            // Lead-review fix: the first camera sat on the Z axis looking straight at the beast's
            // front (its glTF-space forward, +Z, points directly at a camera offset in +Z), reading as
            // a flat head-on view with wings straight up. Beasts in the game are seen in a 3/4 side
            // view (content/art/beasts/griffin/griffin.png) -- fixed by rotating every beast
            // FacingYaw=-90 degrees (see SetStressLevel) so its forward axis points world +X instead of
            // +Z, and moving the camera to the *side* (offset mostly along Z, a little along X for a
            // slight 3/4 turn rather than a flat profile), elevated and tilted down for the hex-board
            // angle the earlier Blender passes also used.
            NumVector3 boardCenterNum = NumVector3.Zero;
            foreach (var inst in _instances)
                boardCenterNum += new NumVector3(inst.World.M41, inst.World.M42, inst.World.M43);
            if (_instances.Count > 0)
                boardCenterNum /= _instances.Count;
            var target = new XnaVector3(boardCenterNum.X, 0.85f, boardCenterNum.Z);

            // A hex board's rows are separated along world Z; an orthographic camera looking *straight*
            // down Z would collapse all rows onto the same screen position no matter how many world
            // units apart they are (that axis is exactly what gets projected away) -- confirmed: the
            // first value here (24 degrees, chosen for a single beast's close-up 3/4 read) made a
            // 3-row, 24-beast formation collapse into one overlapping clump on screen even though
            // RebuildCamera's own view-space bounding math (below) correctly measured the beasts as
            // spread across ~20 world units. 48 degrees gives the rows real screen-Y separation while
            // still reading as a 3/4, not top-down, view for the close-up single-beast case.
            const float tiltDeg = 48f;
            float tilt = MathHelper.ToRadians(tiltDeg);
            // Side axis (+Z, since beasts now face +X) dominates; a small +X component gives the 3/4
            // turn instead of a flat 90-degree profile; +Y from the tilt for the elevated board look.
            var camDir = XnaVector3.Normalize(new XnaVector3(0.22f, (float)Math.Sin(tilt), (float)Math.Cos(tilt)));
            float camDistance = 8f;
            var camPos = target + camDir * camDistance;
            _view = XnaMatrix.CreateLookAt(camPos, target, XnaVector3.Up);

            // Fit the ortho projection to the actual on-screen (view-space) extent of every instance,
            // not a world-axis-aligned guess -- robust to the camera direction above, and what actually
            // fixes "oversized/clumped" framing rather than just widening a fixed-axis span. Samples
            // each beast's ground point and a point near its head height/wingtip so the vertical extent
            // (and wing spread) are both accounted for.
            float minVX = float.MaxValue, maxVX = float.MinValue, minVY = float.MaxValue, maxVY = float.MinValue;
            if (_instances.Count == 0)
            {
                minVX = maxVX = minVY = maxVY = 0f;
            }
            foreach (var inst in _instances)
            {
                var basePt = new XnaVector3(inst.World.M41, inst.World.M42, inst.World.M43);
                var topPt = basePt + new XnaVector3(0f, 2.2f, 0f);
                var vpBase = XnaVector3.Transform(basePt, _view);
                var vpTop = XnaVector3.Transform(topPt, _view);
                minVX = Math.Min(minVX, Math.Min(vpBase.X, vpTop.X));
                maxVX = Math.Max(maxVX, Math.Max(vpBase.X, vpTop.X));
                minVY = Math.Min(minVY, Math.Min(vpBase.Y, vpTop.Y));
                maxVY = Math.Max(maxVY, Math.Max(vpBase.Y, vpTop.Y));
            }
            const float margin = 1.4f; // wing spread + outline thickness clearance around each beast
            float viewSpanX = Math.Max(1.6f, (maxVX - minVX) + margin * 2f);
            float viewSpanY = Math.Max(1.6f, (maxVY - minVY) + margin * 2f);

            float aspect = (float)_graphics.PreferredBackBufferHeight / _graphics.PreferredBackBufferWidth;
            float orthoWidth = Math.Max(viewSpanX, viewSpanY / aspect);
            float orthoHeight = orthoWidth * aspect;
            _projection = XnaMatrix.CreateOrthographic(orthoWidth, orthoHeight, 0.05f, 50f);
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

            if (!_options.BenchMode && !_options.ScreenshotMode)
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
            }
            _prevKeyboard = kb;

            _elapsedSeconds += (float)gameTime.ElapsedGameTime.TotalSeconds;

            _skinStopwatch.Restart();
            foreach (var inst in _instances)
                UpdateInstancePose(inst);
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

        protected override void Draw(GameTime gameTime)
        {
            _stats.DrawCallsThisFrame = 0;
            _stats.TrianglesThisFrame = 0;

            GraphicsDevice.Clear(new XnaColor(10, 10, 14));

            DrawBackdrop();
            DrawHexGrid();
            DrawBeasts();

            bool showStats = !_options.BenchMode && !(_options.ScreenshotMode && _options.ScreenshotHideStats);
            if (showStats)
                DrawStatsOverlay();

            base.Draw(gameTime);

            if (_options.ScreenshotMode && _screenshotElapsedSeconds >= ScreenshotWarmupSeconds)
            {
                SaveScreenshot(_options.ScreenshotPath);
                Exit();
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
            // under glTF's right-handed CCW convention is CullClockwise's kept set here.
            GraphicsDevice.RasterizerState = RasterizerState.CullClockwise;
            var toonTechnique = _toonEffect.Techniques["Toon"];
            foreach (var inst in _instances)
                DrawInstance(inst, toonTechnique);

            // Inverted-hull outline pass: reversed culling, so only the expanded shell's silhouette shows.
            GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
            var outlineTechnique = _toonEffect.Techniques["Outline"];
            foreach (var inst in _instances)
                DrawInstance(inst, outlineTechnique);
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

        private void DrawStatsOverlay()
        {
            _spriteBatch.Begin();
            long managedBytes = StatsTracker.EstimateManagedBytes();
            long gpuEstimateBytes = EstimateGpuBytes();
            string text =
                $"beasts: {_instances.Count}  clip: {_clip}  crest: {(_instances.Count > 0 && _instances[0].CrestOn ? "on" : "off")}  tint: {_tintIndex}\n" +
                $"fps avg: {_stats.AverageFps():0.0}  fps 1% low: {_stats.OnePercentLowFps():0.0}\n" +
                $"draw calls: {_stats.DrawCallsThisFrame}  tris: {_stats.TrianglesThisFrame:N0}\n" +
                $"skin: {_stats.SkinningMsEma:0.00} ms  managed: {managedBytes / (1024.0 * 1024.0):0.0} MB  gpu(est): {gpuEstimateBytes / (1024.0 * 1024.0):0.0} MB\n" +
                "Tab: stress 1/3/12/24   C: crest   T: tint   M: idle/move   Esc: quit";
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
            return vb + ib + tex + backdrop;
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
