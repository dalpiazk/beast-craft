using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NumMatrix = System.Numerics.Matrix4x4;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace BeastCraft.Spike55.Live3D
{
    /// <summary>Spike #55, fourth pass: real-time 3D in MonoGame. Loads the Blender-authored, skinned
    /// griffin_live.glb (see Tooling/Spike55/blender_export_live.py), CPU-skins it per instance per
    /// frame, and draws it with a custom toon + inverted-hull-outline effect over a Verdant Hollow
    /// backdrop and a hex-grid board. See Tooling/Spike55/README.md for controls and the bench/screenshot
    /// CLI, and docs/spikes/055-3d-mini-spike.md's fourth-pass section for what this was built to answer.</summary>
    public sealed class Game1 : Game
    {
        private readonly GraphicsDeviceManager _graphics;
        private readonly LaunchOptions _options;
        private SpriteBatch _spriteBatch;
        private SpriteFont _font;

        private GltfSkinnedModel _bodyModel;
        private GltfSkinnedModel _crestModel;
        private Effect _toonEffect;
        private Texture2D _baseColorTexture;
        private Texture2D _crestTexture; // crest_alt.glb ships no base-colour image (a flat Principled
                                         // BSDF colour in Blender) -- a 1x1 solid-colour stand-in texture
                                         // so the shared Toon.fx (which always samples BaseTexture) has
                                         // something to sample instead of an unbound/black sampler (a
                                         // first render with no texture bound at all showed the crest as
                                         // near-black, indistinguishable from its own outline pass).
        private Texture2D _backdropTexture;
        private IndexBuffer _bodyIndices;
        private IndexBuffer _crestIndices;
        private int _headNodeIndex;

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

        // --screenshot state
        private int _screenshotFramesRemaining;

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

            _baseColorTexture = LoadTextureFromBytes(_bodyModel.BaseColorImageBytes);

            // MGFX (MonoGame's effect compiler) does not honour a .fx file's HLSL default-value
            // initialisers at runtime -- every Effect parameter comes back zero until set from C#
            // (confirmed: a first run with only ViewProjection/BaseTexture/TintMultiply set from here
            // rendered the whole beast flat black, because LightDirection defaulted to a zero vector,
            // normalize(0,0,0) is undefined/NaN, every ndotl comparison then fell through to the
            // HighlightBoost band -- which was *also* unset/zero -- multiplying the sampled texture by
            // black regardless of what it actually was). Set the toon material's fixed constants once
            // here, not per frame (only ViewProjection/BaseTexture/TintMultiply change per frame/instance).
            _toonEffect.Parameters["LightDirection"].SetValue(XnaVector3.Normalize(new XnaVector3(0.45f, 0.65f, 0.60f)));
            _toonEffect.Parameters["LightColor"].SetValue(new XnaVector3(1.0f, 0.88f, 0.68f));
            _toonEffect.Parameters["ShadowTint"].SetValue(new XnaVector3(0x7C / 255f, 0x7A / 255f, 0xAE / 255f));
            _toonEffect.Parameters["HighlightBoost"].SetValue(new XnaVector3(1.08f, 1.0f, 0.85f));
            _toonEffect.Parameters["OutlineThickness"].SetValue(0.012f);
            _toonEffect.Parameters["OutlineColor"].SetValue(new XnaVector3(0x2E / 255f, 0x2A / 255f, 0x45 / 255f));
            using (var fs = File.OpenRead(Path.Combine(contentRoot, "backdrop.png")))
                _backdropTexture = Texture2D.FromStream(GraphicsDevice, fs);

            _crestTexture = new Texture2D(GraphicsDevice, 1, 1);
            _crestTexture.SetData(new[] { new XnaColor(64, 191, 179) }); // matches CrestAlt's Blender material (0.25, 0.75, 0.70)

            _bodyIndices = BuildIndexBuffer(_bodyModel.Indices);
            _crestIndices = BuildIndexBuffer(_crestModel.Indices);

            _lineEffect = new BasicEffect(GraphicsDevice) { VertexColorEnabled = true };
            _backdropEffect = new BasicEffect(GraphicsDevice) { TextureEnabled = true, VertexColorEnabled = false, World = XnaMatrix.Identity, View = XnaMatrix.Identity, Projection = XnaMatrix.Identity };

            var gridVerts = HexBoard.BuildGridLines(8, 6, new XnaColor(46, 42, 69, 140));
            _hexGridVertexBuffer = new VertexBuffer(GraphicsDevice, VertexPositionColor.VertexDeclaration, gridVerts.Length, BufferUsage.WriteOnly);
            _hexGridVertexBuffer.SetData(gridVerts);
            _hexGridVertexCount = gridVerts.Length;

            SetStressLevel(_options.BenchMode ? ClampStress(_options.BenchBeasts) : (_options.ScreenshotMode ? ClampStress(_options.ScreenshotBeasts) : 1));
            if (!_options.ScreenshotCrestOn)
                foreach (var inst in _instances)
                    inst.CrestOn = false;
            _tintIndex = Math.Clamp(_options.ScreenshotTint, 0, Tints.Length - 1);

            if (_options.ScreenshotMode)
                _screenshotFramesRemaining = 30; // let one full idle cycle's worth of frames settle first
        }

        private static int ClampStress(int requested)
        {
            foreach (var lvl in StressLevels)
                if (lvl >= requested)
                    return requested; // bench/screenshot honour the exact requested count, not just the UI's steps
            return requested;
        }

        private Texture2D LoadTextureFromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            using (var ms = new MemoryStream(bytes))
                return Texture2D.FromStream(GraphicsDevice, ms);
        }

        private IndexBuffer BuildIndexBuffer(int[] indices)
        {
            var ib = new IndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
            var shorts = new short[indices.Length];
            for (int i = 0; i < indices.Length; i++)
                shorts[i] = (short)indices[i];
            ib.SetData(shorts);
            return ib;
        }

        private void SetStressLevel(int count)
        {
            while (_instances.Count < count)
                _instances.Add(new BeastInstance(GraphicsDevice, _bodyModel, _crestModel));
            while (_instances.Count > count)
                _instances.RemoveAt(_instances.Count - 1);

            var cells = HexBoard.FillOrder(8, 6, count);
            for (int i = 0; i < _instances.Count; i++)
            {
                var (col, row) = cells[i % cells.Count];
                var center = HexBoard.CellCenter(col, row);
                var world = NumMatrix.CreateTranslation(center.X, 0f, center.Z);
                _instances[i].World = world;
                _instances[i].ClockOffset = (float)(_rng.NextDouble() * 4.0);
            }
            RebuildCamera();
        }

        private void RebuildCamera()
        {
            // Fit an orthographic camera to whatever's currently on the board -- a close-up single
            // beast, or all 24 spread across the grid -- same hex-board 3/4 tilt the earlier pre-rendered
            // passes used (blender_lowpoly_render.py's BoardCam), adapted to MonoGame's Y-up glTF space.
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var inst in _instances)
            {
                float x = inst.World.M41, z = inst.World.M43;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minZ = Math.Min(minZ, z);
                maxZ = Math.Max(maxZ, z);
            }
            if (_instances.Count == 0)
            { minX = maxX = minZ = maxZ = 0; }

            var target = new XnaVector3((minX + maxX) * 0.5f, 0.9f, (minZ + maxZ) * 0.5f);
            float spanX = Math.Max(1.6f, maxX - minX + HexBoard.HexSize * 2.5f);
            float spanZ = Math.Max(1.6f, maxZ - minZ + HexBoard.HexSize * 2.5f);

            const float tiltDeg = 32f;
            float tilt = MathHelper.ToRadians(tiltDeg);
            var camDir = XnaVector3.Normalize(new XnaVector3(0f, (float)Math.Sin(tilt), (float)Math.Cos(tilt)));
            float camDistance = 6f;
            var camPos = target + camDir * camDistance;
            _view = XnaMatrix.CreateLookAt(camPos, target, XnaVector3.Up);

            // Portrait viewport (9:16): fit width to the widest span, derive height from the aspect ratio
            // so beasts don't stretch; orthoHeight tracks whichever of spanX/spanZ*aspect is larger.
            float aspect = (float)_graphics.PreferredBackBufferHeight / _graphics.PreferredBackBufferWidth;
            float orthoWidth = Math.Max(spanX, spanZ / aspect) * 1.15f;
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
                SkinInstance(inst);
            _skinStopwatch.Stop();
            _stats.SkinningMsThisFrame = _skinStopwatch.Elapsed.TotalMilliseconds;

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

        private void SkinInstance(BeastInstance inst)
        {
            float t = _elapsedSeconds + inst.ClockOffset;
            AnimatedPose.ComputeWorldMatrices(_bodyModel, _clip, t, inst.NodeWorldScratch);
            AnimatedPose.ComputeSkinMatrices(_bodyModel, inst.NodeWorldScratch, inst.World, inst.SkinScratch);
            Skinner.SkinToBuffer(_bodyModel, inst.SkinScratch, inst.BodyScratch);
            inst.BodyVertexBuffer.SetData(inst.BodyScratch);

            if (inst.CrestOn)
            {
                // The crest's local mesh grows from its own origin along -Y/+Z (see
                // blender_export_live.py's procedural blade loop); parented directly at the head
                // bone's world matrix with no adjustment, it first rendered draped in front of the
                // face like a bib instead of sticking up like a crest -- the head bone's own bind-pose
                // orientation (tilted forward/down to follow the beak) doesn't line up with "up" in
                // world space. A small fixed local re-orientation (tip the blades up and back, scale
                // down a little) fixes that -- this is a stand-in cosmetic mesh for the toggle proof,
                // not a hand-placed art asset, so this one fixed correction (not a full per-bone rig)
                // is deliberately as far as this spike takes cosmetic placement.
                var crestLocal = NumMatrix.CreateScale(0.55f) * NumMatrix.CreateRotationX(-1.65f) * NumMatrix.CreateTranslation(0f, 0.05f, 0.05f);
                var crestTransform = crestLocal * inst.NodeWorldScratch[_headNodeIndex] * inst.World;
                Skinner.TransformToBuffer(_crestModel, crestTransform, inst.CrestScratch);
                inst.CrestVertexBuffer.SetData(inst.CrestScratch);
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

            if (!_options.BenchMode)
                DrawStatsOverlay();

            base.Draw(gameTime);

            if (_options.ScreenshotMode)
            {
                _screenshotFramesRemaining--;
                if (_screenshotFramesRemaining <= 0)
                {
                    SaveScreenshot(_options.ScreenshotPath);
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
            _toonEffect.Parameters["ViewProjection"]?.SetValue(viewProjection);
            _toonEffect.Parameters["TintMultiply"]?.SetValue(Tints[_tintIndex]);

            GraphicsDevice.BlendState = BlendState.Opaque;
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;

            // glTF/OpenGL's winding convention is the opposite of MonoGame/XNA's default
            // (RasterizerState.CullCounterClockwise, which treats *clockwise* as front-facing) -- the
            // griffin_live.glb index data was loaded as-is (no re-winding), so the front-facing set
            // under glTF's right-handed CCW convention is CullClockwise's kept set here. Using the
            // (wrong) default first showed a flat, uniformly dark silhouette with no banding at all: the
            // main pass was lighting/culling the mesh's inside-out backfaces, and the outline pass (also
            // reversed) then fully overdrew it instead of just its fringe -- both symptoms explained by
            // this one winding mismatch, confirmed by swapping the two RasterizerStates below.
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
            _toonEffect.Parameters["BaseTexture"].SetValue(_baseColorTexture);
            GraphicsDevice.SetVertexBuffer(inst.BodyVertexBuffer);
            GraphicsDevice.Indices = _bodyIndices;
            foreach (var pass in technique.Passes)
            {
                pass.Apply();
                GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _bodyModel.TriangleCount);
            }
            _stats.DrawCallsThisFrame++;
            _stats.TrianglesThisFrame += _bodyModel.TriangleCount;

            if (inst.CrestOn)
            {
                _toonEffect.Parameters["BaseTexture"].SetValue(_crestTexture);
                GraphicsDevice.SetVertexBuffer(inst.CrestVertexBuffer);
                GraphicsDevice.Indices = _crestIndices;
                foreach (var pass in technique.Passes)
                {
                    pass.Apply();
                    GraphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _crestModel.TriangleCount);
                }
                _stats.DrawCallsThisFrame++;
                _stats.TrianglesThisFrame += _crestModel.TriangleCount;
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
            long perInstanceVb = (long)(_bodyModel.Positions.Length + _crestModel.Positions.Length) * VertexPositionNormalTexture.VertexDeclaration.VertexStride;
            long vb = perInstanceVb * _instances.Count;
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
                $"  \"gpuMemoryEstimateBytes\": {gpuBytes}\n" +
                "}\n";
            File.WriteAllText(_options.BenchOutPath, json);
            Console.WriteLine($"Bench result written: {_options.BenchOutPath}");
            Console.WriteLine(json);
        }
    }
}
