using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Vfx
{
    /// <summary>One particle at one moment: where it is, its colour (index into the spec's Colors) and opacity.</summary>
    public readonly struct ParticleState
    {
        public ParticleState(Vec2 position, int colorIndex, float alpha)
        {
            Position = position;
            ColorIndex = colorIndex;
            Alpha = alpha;
        }

        public Vec2 Position { get; }

        public int ColorIndex { get; }

        /// <summary>1 at birth, falling linearly to 0 at the end of its life.</summary>
        public float Alpha { get; }
    }

    /// <summary>
    /// A burst of particles from one point (<see cref="VfxParticleData"/>), fully determined by its
    /// spec, origin and seed: every particle's direction, speed, lifetime and colour is drawn once,
    /// up front, from a <see cref="DeterministicRandom"/>, and its position at time t is closed-form
    /// ballistic motion (<c>p = p0 + v t + g t^2 / 2</c>, gravity pulling down the screen). So
    /// <see cref="Sample"/> is a pure function of time: the same burst looks identical at any frame
    /// rate, can be scrubbed backwards, and a screenshot of frame N is reproducible.
    /// </summary>
    public sealed class ParticleBurst
    {
        private readonly Vec2 _origin;
        private readonly float _gravity;
        private readonly float[] _vx;
        private readonly float[] _vy;
        private readonly int[] _lifeMs;
        private readonly int[] _color;

        /// <param name="spec">The burst's spec (null: no particles).</param>
        /// <param name="origin">Where it bursts from.</param>
        /// <param name="seed">Seeds every particle.</param>
        /// <param name="count">
        /// How many particles to play (null: the spec's <c>Count</c>). The first
        /// <paramref name="count"/> are exactly the full burst's first ones (the same draws), so a
        /// reduced burst is a subset of the full one.
        /// </param>
        public ParticleBurst(VfxParticleData spec, Vec2 origin, int seed, int? count = null)
        {
            _origin = origin;
            int full = spec == null ? 0 : Math.Max(0, spec.Count);
            int played = count.HasValue ? Math.Max(0, Math.Min(full, count.Value)) : full;
            int colors = spec == null || spec.Colors == null ? 0 : spec.Colors.Length;
            _gravity = spec == null ? 0f : spec.Gravity;
            _vx = new float[played];
            _vy = new float[played];
            _lifeMs = new int[played];
            _color = new int[played];

            DeterministicRandom random = new DeterministicRandom(seed);
            for (int i = 0; i < played; i++)
            {
                double angle = random.NextFloat() * Math.PI * 2.0;
                float speed = spec.SpeedMin + (spec.SpeedMax - spec.SpeedMin) * random.NextFloat();
                _vx[i] = (float)Math.Cos(angle) * speed;
                _vy[i] = (float)Math.Sin(angle) * speed;
                _lifeMs[i] = Math.Max(1, (int)(spec.LifetimeMs * (0.6f + 0.4f * random.NextFloat())));
                _color[i] = random.Next(colors);
            }
        }

        /// <summary>How many particles the burst has (alive or not).</summary>
        public int Count
        {
            get { return _vx.Length; }
        }

        /// <summary>When the last particle dies, in ms after the burst.</summary>
        public int DurationMs
        {
            get
            {
                int longest = 0;
                foreach (int life in _lifeMs)
                {
                    longest = Math.Max(longest, life);
                }

                return longest;
            }
        }

        /// <summary>The particles alive <paramref name="ms"/> after the burst (none before it or after they all die).</summary>
        public List<ParticleState> Sample(int ms)
        {
            List<ParticleState> alive = new List<ParticleState>();
            if (ms < 0)
            {
                return alive;
            }

            float t = ms / 1000f;
            for (int i = 0; i < _vx.Length; i++)
            {
                if (ms >= _lifeMs[i])
                {
                    continue;
                }

                Vec2 position = new Vec2(_origin.X + _vx[i] * t, _origin.Y + _vy[i] * t + 0.5f * _gravity * t * t);
                alive.Add(new ParticleState(position, _color[i], 1f - ms / (float)_lifeMs[i]));
            }

            return alive;
        }
    }
}
