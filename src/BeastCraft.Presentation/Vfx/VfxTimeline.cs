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
    /// </summary>
    public sealed class VfxTimeline
    {
        private readonly VfxEffectData _effect;
        private readonly Vec2 _caster;
        private readonly List<VfxTarget> _targets;
        private readonly List<ParticleBurst> _bursts = new List<ParticleBurst>();
        private readonly int _seed;

        /// <param name="effect">The spec (from the VFX library). Null plays nothing.</param>
        /// <param name="caster">Where the effect starts.</param>
        /// <param name="targets">Where it lands (empty: it lands on the caster).</param>
        /// <param name="seed">Seeds the particles and the shake.</param>
        public VfxTimeline(VfxEffectData effect, Vec2 caster, IReadOnlyList<VfxTarget> targets, int seed)
        {
            _effect = effect ?? new VfxEffectData();
            _caster = caster;
            _seed = seed;
            _targets = new List<VfxTarget>(targets ?? new VfxTarget[0]);
            if (_targets.Count == 0)
            {
                _targets.Add(new VfxTarget(null, caster, 0, false));
            }

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

        /// <summary>A value in [-1, 1) from the seed, a time bucket and an axis.</summary>
        private float Noise(int bucket, int axis)
        {
            uint bits = DeterministicRandom.Hash(unchecked(_seed * 2 + axis), bucket);
            return (bits >> 8) * (2f / 16777216f) - 1f;
        }
    }
}
