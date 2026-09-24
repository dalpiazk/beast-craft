using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="VfxTimeline"/> and <see cref="ParticleBurst"/>: a pure, seeded function of time —
    /// projectile, impact, hit-stop freeze, flipbook, particles, shake, flash, damage numbers.
    /// </summary>
    public class VfxTimelineTests
    {
        private static readonly Vec2 Caster = new Vec2(0f, 0f);
        private static readonly Vec2 Target = new Vec2(100f, 0f);

        private static VfxTimeline Timeline(VfxEffectData effect, int seed = 7)
        {
            return new VfxTimeline(effect, Caster, new[] { new VfxTarget("t", Target, 42, true) }, seed);
        }

        [Test]
        public void Projectile_FliesFromCasterToTarget_ThenImpacts()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            Assert.AreEqual(200, timeline.ImpactMs);
            Assert.AreEqual(250, timeline.HitStopEndMs);

            VfxFrame halfway = timeline.Sample(100);
            Assert.IsFalse(halfway.Impacted);
            Assert.AreEqual(1, halfway.Projectiles.Count);
            Assert.AreEqual(50f, halfway.Projectiles[0].X, 1e-4);
            Assert.AreEqual(-1, halfway.FlipbookFrame);
            Assert.IsEmpty(halfway.Particles);

            VfxFrame impact = timeline.Sample(200);
            Assert.IsTrue(impact.Impacted);
            Assert.IsEmpty(impact.Projectiles);
        }

        [Test]
        public void HitStop_HoldsTheImpactPose()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            VfxFrame early = timeline.Sample(205);
            VfxFrame late = timeline.Sample(245);

            Assert.IsTrue(early.HitStop);
            Assert.IsTrue(late.HitStop);
            Assert.AreEqual(0, early.FlipbookFrame);
            Assert.AreEqual(0, late.FlipbookFrame);
            Assert.AreEqual(early.Particles.Count, late.Particles.Count);
            for (int i = 0; i < early.Particles.Count; i++)
            {
                Assert.AreEqual(early.Particles[i].Position, late.Particles[i].Position, "particles are frozen during the hit-stop");
                Assert.AreEqual(Target, early.Particles[i].Position, "and sit at the impact point");
            }

            Assert.Greater(early.FlashAlpha, late.FlashAlpha, "the flash already fades during the hit-stop");
            Assert.IsFalse(timeline.Sample(250).HitStop);
        }

        [Test]
        public void Flipbook_AdvancesAtItsFps_ThenEnds()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            Assert.AreEqual(400, timeline.FlipbookDurationMs);
            Assert.AreEqual(0, timeline.Sample(250).FlipbookFrame);
            Assert.AreEqual(1, timeline.Sample(350).FlipbookFrame);
            Assert.AreEqual(3, timeline.Sample(649).FlipbookFrame);
            Assert.AreEqual(-1, timeline.Sample(650).FlipbookFrame);
        }

        [Test]
        public void EverythingEnds_AtDuration()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            Assert.AreEqual(800, timeline.DurationMs, "the 600 ms damage number from impact outlasts the flipbook (hit-stop end + 400), shake, flash and particles");
            VfxFrame done = timeline.Sample(timeline.DurationMs);
            Assert.IsTrue(done.Finished);
            Assert.IsEmpty(done.Particles);
            Assert.AreEqual(Vec2.Zero, done.Shake);
            Assert.IsFalse(timeline.Sample(timeline.DurationMs - 1).Finished);
        }

        [Test]
        public void Shake_DecaysToNothing_AndIsSeeded()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            Vec2 early = timeline.Sample(201).Shake;
            Assert.LessOrEqual(System.Math.Abs(early.X), 2f);
            Assert.AreNotEqual(Vec2.Zero, early);
            Assert.AreEqual(Vec2.Zero, timeline.Sample(200 + 200).Shake, "shake lasts DurationMs after impact");

            Assert.AreEqual(early, Timeline(VfxLibraryTests.Effect(), 7).Sample(201).Shake);
            Assert.AreNotEqual(early, Timeline(VfxLibraryTests.Effect(), 8).Sample(201).Shake);
        }

        [Test]
        public void DamageNumber_RisesAndFades()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());

            DamageNumberState start = timeline.Sample(200).DamageNumbers[0];
            DamageNumberState later = timeline.Sample(200 + 540).DamageNumbers[0];

            Assert.AreEqual(42, start.Value);
            Assert.IsTrue(start.Crit);
            Assert.AreEqual(Target.Y, start.Position.Y, 1e-4);
            Assert.Less(later.Position.Y, start.Position.Y, "it rises (up the screen)");
            Assert.AreEqual(1f, start.Alpha);
            Assert.Less(later.Alpha, 1f);
            Assert.IsEmpty(timeline.Sample(200 + 600).DamageNumbers);
        }

        [Test]
        public void NoDamage_NoNumber_AndInstantEffectsImpactAtOnce()
        {
            VfxEffectData effect = VfxLibraryTests.Effect();
            effect.Motion = VfxMotion.Instant;
            effect.TravelMs = 0;
            effect.Projectile = null;
            VfxTimeline timeline = new VfxTimeline(effect, Caster, new[] { new VfxTarget("t", Target, 0, false) }, 1);

            Assert.AreEqual(0, timeline.ImpactMs);
            Assert.IsTrue(timeline.Sample(0).Impacted);
            Assert.IsEmpty(timeline.Sample(0).DamageNumbers);
        }

        [Test]
        public void Sample_IsPure_TheSameTimeGivesTheSameFrame()
        {
            VfxTimeline timeline = Timeline(VfxLibraryTests.Effect());
            VfxFrame a = timeline.Sample(333);
            timeline.Sample(10);
            timeline.Sample(600);
            VfxFrame b = timeline.Sample(333);

            Assert.AreEqual(a.FlipbookFrame, b.FlipbookFrame);
            Assert.AreEqual(a.Shake, b.Shake);
            Assert.AreEqual(a.Particles.Count, b.Particles.Count);
            for (int i = 0; i < a.Particles.Count; i++)
            {
                Assert.AreEqual(a.Particles[i].Position, b.Particles[i].Position);
            }
        }

        [Test]
        public void MultipleTargets_OneProjectileAndBurstEach()
        {
            VfxTimeline timeline = new VfxTimeline(VfxLibraryTests.Effect(), Caster,
                                                   new[] { new VfxTarget("a", Target, 1, false), new VfxTarget("b", new Vec2(0f, 100f), 2, false) }, 3);

            Assert.AreEqual(2, timeline.Sample(50).Projectiles.Count);
            Assert.AreEqual(12, timeline.Sample(250).Particles.Count);
            Assert.AreEqual(2, timeline.Sample(250).DamageNumbers.Count);
        }

        [Test]
        public void ParticleBurst_IsSeeded_AndGravityPullsDown()
        {
            VfxParticleData spec = new VfxParticleData { Count = 32, SpeedMin = 20f, SpeedMax = 60f, LifetimeMs = 1000, Colors = new[] { "a", "b", "c" }, Gravity = 0f };
            ParticleBurst flat = new ParticleBurst(spec, Vec2.Zero, 11);
            ParticleBurst same = new ParticleBurst(spec, Vec2.Zero, 11);
            ParticleBurst other = new ParticleBurst(spec, Vec2.Zero, 12);

            List<ParticleState> a = flat.Sample(300);
            List<ParticleState> b = same.Sample(300);
            Assert.AreEqual(32, a.Count, "the shortest life is 60% of LifetimeMs");
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Position, b[i].Position);
                Assert.AreEqual(a[i].ColorIndex, b[i].ColorIndex);
                Assert.That(a[i].ColorIndex, Is.InRange(0, 2));
            }

            Assert.AreNotEqual(a[0].Position, other.Sample(300)[0].Position);

            spec.Gravity = 500f;
            List<ParticleState> heavy = new ParticleBurst(spec, Vec2.Zero, 11).Sample(300);
            for (int i = 0; i < heavy.Count; i++)
            {
                Assert.AreEqual(a[i].Position.X, heavy[i].Position.X, 1e-4);
                Assert.AreEqual(a[i].Position.Y + 0.5f * 500f * 0.09f, heavy[i].Position.Y, 1e-3, "g t^2 / 2 further down");
            }

            Assert.IsEmpty(flat.Sample(1000), "all dead by LifetimeMs");
            Assert.IsEmpty(flat.Sample(-1));
            Assert.LessOrEqual(flat.DurationMs, 1000);
        }

        [Test]
        public void DeterministicRandom_IsStableAcrossInstances()
        {
            DeterministicRandom a = new DeterministicRandom(123);
            DeterministicRandom b = new DeterministicRandom(123);
            for (int i = 0; i < 100; i++)
            {
                float x = a.NextFloat();
                Assert.AreEqual(x, b.NextFloat());
                Assert.That(x, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }

            Assert.AreEqual(DeterministicRandom.Hash(4, 5), DeterministicRandom.Hash(4, 5));
            Assert.AreNotEqual(DeterministicRandom.Hash(4, 5), DeterministicRandom.Hash(5, 4));
        }
    }
}
