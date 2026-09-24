using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Vfx
{
    /// <summary>One target of an effect: where it is, who it is, and what the hit did to it.</summary>
    public readonly struct VfxTarget
    {
        public VfxTarget(string unitId, Vec2 position, int damage, bool crit)
        {
            UnitId = unitId;
            Position = position;
            Damage = damage;
            Crit = crit;
        }

        public string UnitId { get; }

        public Vec2 Position { get; }

        /// <summary>Damage the skill dealt it (0 = no number).</summary>
        public int Damage { get; }

        public bool Crit { get; }
    }

    /// <summary>A floating damage number at one moment.</summary>
    public readonly struct DamageNumberState
    {
        public DamageNumberState(string unitId, Vec2 position, int value, bool crit, float alpha)
        {
            UnitId = unitId;
            Position = position;
            Value = value;
            Crit = crit;
            Alpha = alpha;
        }

        public string UnitId { get; }

        public Vec2 Position { get; }

        public int Value { get; }

        public bool Crit { get; }

        public float Alpha { get; }
    }

    /// <summary>Everything an effect shows at one moment (see <see cref="VfxTimeline.Sample"/>). Draw it; never feed it back.</summary>
    public sealed class VfxFrame
    {
        /// <summary>True once the effect has run its course (nothing left to draw).</summary>
        public bool Finished;

        /// <summary>True once the hit has landed: a viewer shows the targets' post-hit HP from here on.</summary>
        public bool Impacted;

        /// <summary>True inside the hit-stop window: the host may freeze everything else too.</summary>
        public bool HitStop;

        /// <summary>Where each projectile is (one per target), while in flight.</summary>
        public readonly List<Vec2> Projectiles = new List<Vec2>();

        /// <summary>The flipbook frame to draw on every target (-1 = none).</summary>
        public int FlipbookFrame = -1;

        /// <summary>The live particles of every target's burst.</summary>
        public readonly List<ParticleState> Particles = new List<ParticleState>();

        /// <summary>The camera offset to apply (virtual pixels).</summary>
        public Vec2 Shake = Vec2.Zero;

        /// <summary>The hit flash's strength on the targets, 0-1.</summary>
        public float FlashAlpha;

        public readonly List<DamageNumberState> DamageNumbers = new List<DamageNumberState>();

        /// <summary>Every live layer sprite (schema v2 layers), in layer order: ground ones go under the units.</summary>
        public readonly List<VfxSprite> Sprites = new List<VfxSprite>();
    }

    /// <summary>
    /// One skill effect (<see cref="VfxEffectData"/>) played from a caster to its targets, as a pure
    /// function of time: <see cref="Sample"/>(t) says what to draw t ms after the skill fired, and
    /// the same spec, positions and seed always give the same frames (particles and shake come from a
    /// <see cref="DeterministicRandom"/>). No engine types, no clocks, no state between calls.
    /// <para>
    /// Phases: the projectile flies for <c>TravelMs</c> (0 when <c>Instant</c>); at impact
    /// (<see cref="ImpactMs"/>) the hit-stop holds the impact pose for <c>HitStopMs</c> — the
    /// flipbook on frame 0, particles at their origin — while the flash, shake and damage number
    /// start at once; after the hit-stop (<see cref="HitStopEndMs"/>) the flipbook and particles run.
    /// <see cref="DurationMs"/> is when the last part ends.
    /// </para>
    /// <para>
    /// Layers (<see cref="VfxEffectData.Layers"/>) run on their own clocks from the hit-stop's
    /// release (<see cref="VfxLayerData.StartMs"/>, negative for a charge-up before the impact) for
    /// their <see cref="VfxLayerData.DurationMs"/>, anchored on each target, on the caster, or once
    /// on the affected area (<see cref="Area"/>), and come out as <see cref="VfxFrame.Sprites"/>:
    /// flipbooks, ground decals, shockwave rings growing to the area's radius, radial bursts,
    /// particle bursts and circling glyphs.
    /// </para>
    /// </summary>
    public sealed class VfxTimeline
    {
        private readonly VfxEffectData _effect;
        private readonly Vec2 _caster;
        private readonly List<VfxTarget> _targets;
        private readonly List<ParticleBurst> _bursts = new List<ParticleBurst>();
        private readonly Dictionary<int, List<ParticleBurst>> _layerBursts = new Dictionary<int, List<ParticleBurst>>();
        private readonly VfxLayerData[] _layers;
        private readonly VfxArea _area;
        private readonly int _seed;

        /// <param name="effect">The spec (from the VFX library). Null plays nothing.</param>
        /// <param name="caster">Where the effect starts.</param>
        /// <param name="targets">Where it lands (empty: it lands on the caster).</param>
        /// <param name="seed">Seeds the particles and the shake.</param>
        /// <param name="area">The affected hexes as a circle (null: around the targets' positions).</param>
        public VfxTimeline(VfxEffectData effect, Vec2 caster, IReadOnlyList<VfxTarget> targets, int seed, VfxArea area = null)
        {
            _effect = effect ?? new VfxEffectData();
            _caster = caster;
            _seed = seed;
            _targets = new List<VfxTarget>(targets ?? new VfxTarget[0]);
            if (_targets.Count == 0)
            {
                _targets.Add(new VfxTarget(null, caster, 0, false));
            }

            if (area == null)
            {
                List<Vec2> points = new List<Vec2>();
                foreach (VfxTarget target in _targets)
                {
                    points.Add(target.Position);
                }

                area = VfxArea.Of(points);
            }

            _area = area;
            _layers = _effect.Layers ?? new VfxLayerData[0];

            ImpactMs = _effect.Motion == VfxMotion.Projectile ? Math.Max(0, _effect.TravelMs) : 0;
            HitStopEndMs = ImpactMs + Math.Max(0, _effect.HitStopMs);

            int after = FlipbookDurationMs;
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_effect.Particles != null)
                {
                    ParticleBurst burst = new ParticleBurst(_effect.Particles, _targets[i].Position, unchecked(seed * 31 + i));
                    _bursts.Add(burst);
                    after = Math.Max(after, burst.DurationMs);
                }
            }

            int end = HitStopEndMs + after;
            for (int l = 0; l < _layers.Length; l++)
            {
                VfxLayerData layer = _layers[l];
                if (layer == null)
                {
                    continue;
                }

                end = Math.Max(end, LayerStart(layer) + Math.Max(0, layer.DurationMs));
                if (layer.Type == VfxLayerType.Particles && layer.Particles != null)
                {
                    List<ParticleBurst> bursts = new List<ParticleBurst>();
                    List<Vec2> anchors = Anchors(layer);
                    for (int a = 0; a < anchors.Count; a++)
                    {
                        bursts.Add(new ParticleBurst(layer.Particles, anchors[a], unchecked(seed * 131 + l * 17 + a)));
                    }

                    _layerBursts[l] = bursts;
                }
            }

            end = Math.Max(end, ImpactMs + (_effect.ScreenShake == null ? 0 : _effect.ScreenShake.DurationMs));
            end = Math.Max(end, ImpactMs + (_effect.HitFlash == null ? 0 : _effect.HitFlash.DurationMs));
            end = Math.Max(end, ImpactMs + (_effect.DamageNumber == null ? 0 : _effect.DamageNumber.RiseMs));
            DurationMs = Math.Max(end, HitStopEndMs);
        }

        /// <summary>When the hit lands, ms after the skill fired.</summary>
        public int ImpactMs { get; }

        /// <summary>When the hit-stop releases.</summary>
        public int HitStopEndMs { get; }

        /// <summary>When everything has finished.</summary>
        public int DurationMs { get; }

        /// <summary>How long the flipbook plays once released (0 without one).</summary>
        public int FlipbookDurationMs
        {
            get
            {
                VfxFlipbookData flipbook = _effect.Flipbook;
                return flipbook == null || flipbook.Fps < 1 || flipbook.Frames < 1 ? 0 : (flipbook.Frames * 1000 + flipbook.Fps - 1) / flipbook.Fps;
            }
        }

        /// <summary>The targets, in order.</summary>
        public IReadOnlyList<VfxTarget> Targets
        {
            get { return _targets; }
        }

        /// <summary>The effect spec being played.</summary>
        public VfxEffectData Effect
        {
            get { return _effect; }
        }

        /// <summary>The affected area area-anchored layers centre on and shockwaves grow to.</summary>
        public VfxArea Area
        {
            get { return _area; }
        }

        /// <summary>Where the effect starts (the caster).</summary>
        public Vec2 Caster
        {
            get { return _caster; }
        }

        /// <summary>What to draw <paramref name="ms"/> after the skill fired.</summary>
        public VfxFrame Sample(int ms)
        {
            VfxFrame frame = new VfxFrame();
            if (ms < 0)
            {
                return frame;
            }

            if (ms >= DurationMs)
            {
                frame.Finished = true;
                frame.Impacted = true;
                return frame;
            }

            SampleLayers(ms, frame.Sprites);

            if (ms < ImpactMs)
            {
                float t = ms / (float)ImpactMs;
                foreach (VfxTarget target in _targets)
                {
                    frame.Projectiles.Add(Vec2.Lerp(_caster, target.Position, t));
                }

                return frame;
            }

            frame.Impacted = true;
            frame.HitStop = ms < HitStopEndMs;
            int sinceImpact = ms - ImpactMs;
            int released = Math.Max(0, ms - HitStopEndMs);

            VfxFlipbookData flipbook = _effect.Flipbook;
            if (flipbook != null && flipbook.Fps > 0)
            {
                int index = (int)((long)released * flipbook.Fps / 1000);
                frame.FlipbookFrame = index < flipbook.Frames ? index : -1;
            }

            foreach (ParticleBurst burst in _bursts)
            {
                frame.Particles.AddRange(burst.Sample(released));
            }

            VfxShakeData shake = _effect.ScreenShake;
            if (shake != null && shake.DurationMs > 0 && sinceImpact < shake.DurationMs)
            {
                float strength = shake.Amplitude * (1f - sinceImpact / (float)shake.DurationMs);
                int bucket = sinceImpact / 16;      // a new offset every ~frame, the same one for the whole bucket
                frame.Shake = new Vec2(Noise(bucket, 0) * strength, Noise(bucket, 1) * strength);
            }

            VfxFlashData flash = _effect.HitFlash;
            if (flash != null && flash.DurationMs > 0 && sinceImpact < flash.DurationMs)
            {
                frame.FlashAlpha = 1f - sinceImpact / (float)flash.DurationMs;
            }

            VfxDamageNumberData number = _effect.DamageNumber;
            if (number != null && number.RiseMs > 0 && sinceImpact < number.RiseMs)
            {
                float p = sinceImpact / (float)number.RiseMs;
                float alpha = p < 0.7f ? 1f : 1f - (p - 0.7f) / 0.3f;
                foreach (VfxTarget target in _targets)
                {
                    if (target.Damage > 0)
                    {
                        Vec2 at = new Vec2(target.Position.X, target.Position.Y - number.RisePx * p);
                        frame.DamageNumbers.Add(new DamageNumberState(target.UnitId, at, target.Damage, target.Crit, alpha));
                    }
                }
            }

            return frame;
        }

        /// <summary>When a layer starts, ms after the skill fired (never before 0).</summary>
        public int LayerStart(VfxLayerData layer)
        {
            return Math.Max(0, HitStopEndMs + layer.StartMs);
        }

        /// <summary>Every live layer's sprites at <paramref name="ms"/>, in layer order.</summary>
        private void SampleLayers(int ms, List<VfxSprite> sprites)
        {
            for (int l = 0; l < _layers.Length; l++)
            {
                VfxLayerData layer = _layers[l];
                if (layer == null || layer.DurationMs <= 0)
                {
                    continue;
                }

                int t = ms - LayerStart(layer);
                if (t < 0 || t >= layer.DurationMs)
                {
                    continue;
                }

                float p = t / (float)layer.DurationMs;
                float envelope = Envelope(layer, t);
                bool additive = layer.Blend == VfxBlend.Additive;
                bool ground = string.IsNullOrEmpty(layer.Depth) ? layer.Type == VfxLayerType.GroundDecal : layer.Depth == VfxDepth.Ground;
                float scale = Lerp(layer.Scale, layer.EndScale > 0f ? layer.EndScale : layer.Scale, p);

                if (layer.Type == VfxLayerType.Particles)
                {
                    if (!_layerBursts.TryGetValue(l, out List<ParticleBurst> bursts))
                    {
                        continue;
                    }

                    string[] colors = layer.Particles.Colors ?? new string[0];
                    foreach (ParticleBurst burst in bursts)
                    {
                        foreach (ParticleState particle in burst.Sample(t))
                        {
                            string tint = colors.Length == 0 ? null : colors[particle.ColorIndex];
                            sprites.Add(new VfxSprite(layer.Particles.Sheet, 0, particle.Position, scale, 0f, 0f, particle.Alpha * envelope, tint, additive, ground));
                        }
                    }

                    continue;
                }

                List<Vec2> anchors = Anchors(layer);
                for (int a = 0; a < anchors.Count; a++)
                {
                    Vec2 at = anchors[a];
                    switch (layer.Type)
                    {
                        case VfxLayerType.Flipbook:
                            {
                                int index = Math.Min(Math.Max(1, layer.Frames) - 1, (int)((long)t * Math.Max(1, layer.Fps) / 1000));
                                sprites.Add(new VfxSprite(layer.Sheet, index, at, scale, 0f, 0f, envelope, layer.Tint, additive, ground));
                                break;
                            }

                        case VfxLayerType.GroundDecal:
                            {
                                float radius = RadiusPx(layer, 1f);
                                sprites.Add(new VfxSprite(layer.Sheet, 0, at, scale, 2f * radius, 0f, envelope, layer.Tint, additive, ground));
                                break;
                            }

                        case VfxLayerType.Shockwave:
                            {
                                float radius = RadiusPx(layer, EaseOut(p));
                                sprites.Add(new VfxSprite(layer.Sheet, 0, at, scale, 2f * radius, 0f, envelope * (1f - p), layer.Tint, additive, ground));
                                break;
                            }

                        case VfxLayerType.RadialBurst:
                            {
                                float distance = RadiusPx(layer, EaseOut(p));
                                double offset = (DeterministicRandom.Hash(_seed, l * 7 + a) >> 8) / 16777216.0 * Math.PI * 2.0;
                                int count = Math.Max(1, layer.Count);
                                for (int i = 0; i < count; i++)
                                {
                                    double angle = offset + Math.PI * 2.0 * i / count;
                                    Vec2 ray = new Vec2(at.X + (float)Math.Cos(angle) * distance, at.Y + (float)Math.Sin(angle) * distance);
                                    sprites.Add(new VfxSprite(layer.Sheet, 0, ray, scale, 0f, (float)angle, envelope * (1f - p), layer.Tint, additive, ground));
                                }

                                break;
                            }

                        case VfxLayerType.Glyphs:
                            {
                                float radius = RadiusPx(layer, p);
                                int count = Math.Max(1, layer.Count);
                                for (int i = 0; i < count; i++)
                                {
                                    double angle = Math.PI * 2.0 * (i / (double)count + layer.Spin * p);
                                    // The ring lies on the ground, seen from above at an angle: half as tall as wide.
                                    Vec2 glyph = new Vec2(at.X + (float)Math.Cos(angle) * radius, at.Y + (float)Math.Sin(angle) * radius * 0.5f - layer.RisePx * p);
                                    sprites.Add(new VfxSprite(layer.Sheet, i, glyph, scale, 0f, 0f, envelope, layer.Tint, additive, ground));
                                }

                                break;
                            }
                    }
                }
            }
        }

        /// <summary>Where a layer is drawn: each target, the caster, or the area's centre.</summary>
        private List<Vec2> Anchors(VfxLayerData layer)
        {
            List<Vec2> anchors = new List<Vec2>();
            if (layer.Anchor == VfxAnchor.Area)
            {
                anchors.Add(_area.Center);
            }
            else if (layer.Anchor == VfxAnchor.Caster)
            {
                anchors.Add(_caster);
            }
            else
            {
                foreach (VfxTarget target in _targets)
                {
                    anchors.Add(target.Position);
                }
            }

            return anchors;
        }

        /// <summary>
        /// A layer's radius at progress <paramref name="k"/> (0-1), in board pixels: from
        /// StartRadius to EndRadius hexes, where an EndRadius of 0 or less is the affected area's.
        /// </summary>
        public float RadiusPx(VfxLayerData layer, float k)
        {
            float end = layer.EndRadius > 0f ? layer.EndRadius * HexLayout.ColumnStep : _area.Radius;
            float start = layer.StartRadius * HexLayout.ColumnStep;
            return Lerp(start, end, k);
        }

        /// <summary>Fade in over FadeInMs and out over FadeOutMs: 0-1.</summary>
        private static float Envelope(VfxLayerData layer, int t)
        {
            float alpha = 1f;
            if (layer.FadeInMs > 0 && t < layer.FadeInMs)
            {
                alpha = Math.Min(alpha, t / (float)layer.FadeInMs);
            }

            int left = layer.DurationMs - t;
            if (layer.FadeOutMs > 0 && left < layer.FadeOutMs)
            {
                alpha = Math.Min(alpha, left / (float)layer.FadeOutMs);
            }

            return Math.Max(0f, Math.Min(1f, alpha));
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private static float EaseOut(float p)
        {
            return 1f - (1f - p) * (1f - p);
        }

        /// <summary>A value in [-1, 1) from the seed, a time bucket and an axis.</summary>
        private float Noise(int bucket, int axis)
        {
            uint bits = DeterministicRandom.Hash(unchecked(_seed * 2 + axis), bucket);
            return (bits >> 8) * (2f / 16777216f) - 1f;
        }
    }
}
