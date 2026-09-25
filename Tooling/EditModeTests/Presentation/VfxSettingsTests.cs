using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The player's effects settings as the VFX timeline plays them (<see cref="VfxSettings"/>):
    /// Full is exactly the authored effect, Reduced drops glyphs and secondary particle layers and
    /// halves particles, Minimal keeps only the damage number, hit flash and one simple burst,
    /// shake and flashes switch off on their own, the impact never moves, and every setting is
    /// deterministic. Also the repeated on-apply overlays: a status that lands again (on a unit
    /// that already has it, or from a second skill) plays its overlay each time.
    /// </summary>
    public class VfxSettingsTests
    {
        private static readonly Vec2 Caster = new Vec2(0f, 0f);
        private static readonly VfxTarget[] Targets = { new VfxTarget("a", new Vec2(96f, 0f), 42, false), new VfxTarget("b", new Vec2(128f, 27f), 17, true) };

        private static VfxEffectData Effect()
        {
            return new VfxEffectData
            {
                Motion = VfxMotion.Projectile,
                TravelMs = 200,
                HitStopMs = 60,
                Projectile = new VfxSpriteData { Sheet = "projectile" },
                Flipbook = new VfxFlipbookData { Sheet = "flipbook", Frames = 4, Fps = 20, Additive = true },
                Particles = new VfxParticleData { Sheet = "spark", Count = 11, SpeedMin = 20f, SpeedMax = 60f, LifetimeMs = 400, Colors = new[] { "y", "o" } },
                ScreenShake = new VfxShakeData { Amplitude = 4f, DurationMs = 220 },
                HitFlash = new VfxFlashData { Tint = "4", DurationMs = 140 },
                DamageNumber = new VfxDamageNumberData { Color = "4", RiseMs = 600, RisePx = 20 },
                Layers = new[]
                {
                    new VfxLayerData { Type = VfxLayerType.Glyphs, Sheet = "glyphs", DurationMs = 500, Count = 4, StartRadius = 1f, EndRadius = 1f },
                    new VfxLayerData
                    {
                        Type = VfxLayerType.Particles, DurationMs = 500,
                        Particles = new VfxParticleData { Sheet = "ember", Count = 8, SpeedMin = 10f, SpeedMax = 30f, LifetimeMs = 450, Colors = new[] { "y" } }
                    },
                    new VfxLayerData { Type = VfxLayerType.RadialBurst, Sheet = "ray", DurationMs = 300, Blend = VfxBlend.Additive, EndRadius = 1f },
                    new VfxLayerData { Type = VfxLayerType.Flipbook, Sheet = "glow", DurationMs = 300, Blend = VfxBlend.Additive, Frames = 1 },
                    new VfxLayerData { Type = VfxLayerType.Shockwave, Sheet = "ring", DurationMs = 400, Blend = VfxBlend.Additive, EndRadius = 1f },
                    new VfxLayerData { Type = VfxLayerType.GroundDecal, Sheet = "scorch", DurationMs = 900, EndRadius = 1f }
                }
            };
        }

        private static VfxTimeline Timeline(VfxSettings settings)
        {
            return new VfxTimeline(Effect(), Caster, Targets, 1234, null, settings);
        }

        private static HashSet<string> SheetsSeen(VfxTimeline timeline)
        {
            HashSet<string> sheets = new HashSet<string>();
            for (int ms = 0; ms < timeline.DurationMs; ms += 5)
            {
                foreach (VfxSprite sprite in timeline.Sample(ms).Sprites)
                {
                    sheets.Add(sprite.Sheet);
                }
            }

            return sheets;
        }

        private static int MaxParticles(VfxTimeline timeline)
        {
            int most = 0;
            for (int ms = 0; ms < timeline.DurationMs; ms += 5)
            {
                most = System.Math.Max(most, timeline.Sample(ms).Particles.Count);
            }

            return most;
        }

        [Test]
        public void Settings_FromPlayerSettings_AndUnknownIntensityReadsFull()
        {
            Assert.AreSame(VfxSettings.Default, VfxSettings.From(null));
            Assert.AreEqual(EffectsIntensity.Full, VfxSettings.Default.Intensity);
            Assert.IsTrue(VfxSettings.Default.ScreenShake);
            Assert.IsTrue(VfxSettings.Default.Flashes);

            VfxSettings from = VfxSettings.From(new PlayerSettings { EffectsIntensity = EffectsIntensity.Reduced, ScreenShake = false, Flashes = false });
            Assert.AreEqual(EffectsIntensity.Reduced, from.Intensity);
            Assert.IsFalse(from.ScreenShake);
            Assert.IsFalse(from.Flashes);
            Assert.AreEqual(EffectsIntensity.Full, new VfxSettings((EffectsIntensity)7, true, true).Intensity, "a newer build's value");

            Assert.AreEqual(11, VfxSettings.Default.ParticleCount(11));
            Assert.AreEqual(6, from.ParticleCount(11), "half, rounded up");
            Assert.AreEqual(1, from.ParticleCount(1), "a burst never vanishes");
            Assert.AreEqual(0, from.ParticleCount(0));
        }

        [Test]
        public void Full_IsTheAuthoredEffect_FrameForFrame()
        {
            VfxTimeline plain = new VfxTimeline(Effect(), Caster, Targets, 1234);
            VfxTimeline full = Timeline(VfxSettings.Default);

            Assert.AreEqual(plain.DurationMs, full.DurationMs);
            for (int ms = -10; ms <= plain.DurationMs; ms += 7)
            {
                Assert.AreEqual(Describe(plain.Sample(ms)), Describe(full.Sample(ms)), ms + " ms");
            }

            CollectionAssert.IsSupersetOf(SheetsSeen(full), new[] { "glyphs", "ember", "ray", "glow", "ring", "scorch" });
        }

        [Test]
        public void Reduced_DropsGlyphsAndSecondaryParticles_AndHalvesTheBurst()
        {
            VfxTimeline full = Timeline(VfxSettings.Default);
            VfxTimeline reduced = Timeline(new VfxSettings(EffectsIntensity.Reduced, true, true));

            HashSet<string> sheets = SheetsSeen(reduced);
            Assert.IsFalse(sheets.Contains("glyphs"));
            Assert.IsFalse(sheets.Contains("ember"), "no secondary particle layer");
            CollectionAssert.IsSupersetOf(sheets, new[] { "ray", "glow", "ring", "scorch" });
            Assert.AreEqual(2 * 6, MaxParticles(reduced), "11 per target, halved and rounded up");
            Assert.AreEqual(2 * 11, MaxParticles(full));
            Assert.AreEqual(full.ImpactMs, reduced.ImpactMs);

            // A halved burst is the first half of the full one.
            int at = full.HitStopEndMs + 50;
            List<ParticleState> all = full.Sample(at).Particles;
            List<ParticleState> half = reduced.Sample(at).Particles;
            Assert.AreEqual(all[0].Position, half[0].Position);
        }

        [Test]
        public void Minimal_PlaysOnlyTheNumberTheFlashAndASimpleBurst()
        {
            VfxTimeline full = Timeline(VfxSettings.Default);
            VfxTimeline minimal = Timeline(new VfxSettings(EffectsIntensity.Minimal, true, true));

            Assert.AreEqual(full.ImpactMs, minimal.ImpactMs, "the hit lands when it would have");
            Assert.IsEmpty(SheetsSeen(minimal), "no layers");
            Assert.AreEqual(0, minimal.FlipbookDurationMs);
            bool number = false;
            bool flash = false;
            for (int ms = 0; ms < minimal.DurationMs; ms += 5)
            {
                VfxFrame frame = minimal.Sample(ms);
                Assert.IsEmpty(frame.Projectiles);
                Assert.AreEqual(-1, frame.FlipbookFrame);
                Assert.AreEqual(Vec2.Zero, frame.Shake, "no shake at Minimal");
                number |= frame.DamageNumbers.Count > 0;
                flash |= frame.FlashAlpha > 0f;
            }

            Assert.IsTrue(number);
            Assert.IsTrue(flash);
            Assert.AreEqual(2 * 6, MaxParticles(minimal), "one simple burst per target");
            Assert.Less(minimal.DurationMs, full.DurationMs);

            VfxTimeline dark = Timeline(new VfxSettings(EffectsIntensity.Minimal, true, false));
            for (int ms = 0; ms < dark.DurationMs; ms += 5)
            {
                Assert.AreEqual(0f, dark.Sample(ms).FlashAlpha, "no flash with flashes off");
            }
        }

        [Test]
        public void Minimal_WithoutAnOwnBurst_UsesTheFirstParticleLayer()
        {
            VfxEffectData effect = Effect();
            effect.Particles = null;
            VfxTimeline minimal = new VfxTimeline(effect, Caster, Targets, 9, null, new VfxSettings(EffectsIntensity.Minimal, true, true));

            Assert.AreEqual("ember", minimal.BurstSpec.Sheet);
            Assert.AreEqual(2 * 4, MaxParticles(minimal));
            Assert.IsNull(new VfxTimeline(effect, Caster, Targets, 9).BurstSpec, "at Full the layer plays as a layer");
        }

        [Test]
        public void ShakeOff_StopsOnlyTheShake()
        {
            VfxTimeline full = Timeline(VfxSettings.Default);
            VfxTimeline still = Timeline(new VfxSettings(EffectsIntensity.Full, false, true));

            bool shook = false;
            for (int ms = 0; ms < full.DurationMs; ms += 5)
            {
                shook |= !full.Sample(ms).Shake.Equals(Vec2.Zero);
                VfxFrame frame = still.Sample(ms);
                Assert.AreEqual(Vec2.Zero, frame.Shake);
                Assert.AreEqual(full.Sample(ms).Sprites.Count, frame.Sprites.Count);
                Assert.AreEqual(full.Sample(ms).FlashAlpha, frame.FlashAlpha);
            }

            Assert.IsTrue(shook);
        }

        [Test]
        public void FlashesOff_StopsTheHitFlashAndBrightAdditiveBursts()
        {
            VfxTimeline off = Timeline(new VfxSettings(EffectsIntensity.Full, true, false));

            HashSet<string> sheets = SheetsSeen(off);
            Assert.IsFalse(sheets.Contains("ray"), "additive radial burst");
            Assert.IsFalse(sheets.Contains("glow"), "additive flipbook layer");
            CollectionAssert.IsSupersetOf(sheets, new[] { "glyphs", "ember", "ring", "scorch" });
            Assert.AreEqual(0, off.FlipbookDurationMs, "the additive flipbook");
            for (int ms = 0; ms < off.DurationMs; ms += 5)
            {
                Assert.AreEqual(0f, off.Sample(ms).FlashAlpha);
            }

            Assert.IsTrue(VfxTimeline.LayerPlays(new VfxLayerData { Type = VfxLayerType.Flipbook, Blend = VfxBlend.Alpha }, new VfxSettings(EffectsIntensity.Full, true, false)),
                          "an alpha flipbook is not a flash");
        }

        [TestCase(EffectsIntensity.Full, true, true)]
        [TestCase(EffectsIntensity.Reduced, false, true)]
        [TestCase(EffectsIntensity.Minimal, true, false)]
        public void EverySetting_IsDeterministic(EffectsIntensity intensity, bool shake, bool flashes)
        {
            VfxTimeline a = Timeline(new VfxSettings(intensity, shake, flashes));
            VfxTimeline b = Timeline(new VfxSettings(intensity, shake, flashes));

            Assert.AreEqual(a.DurationMs, b.DurationMs);
            for (int ms = 0; ms <= a.DurationMs; ms += 11)
            {
                Assert.AreEqual(Describe(a.Sample(ms)), Describe(b.Sample(ms)));
            }
        }

        [Test]
        public void TurnAnimation_PassesTheSettingsToEveryTimeline()
        {
            GameContent content = VfxLibraryTests.Content;
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out _)));
            VfxSettings minimal = new VfxSettings(EffectsIntensity.Minimal, false, false);
            int beats = 0;
            PlayedTurn turn;
            while ((turn = playback.Advance()) != null && beats < 20)
            {
                TurnAnimation full = new TurnAnimation(turn, new HexLayout(0, 0), content.Vfx, 5);
                TurnAnimation small = new TurnAnimation(turn, new HexLayout(0, 0), content.Vfx, 5, minimal);
                Assert.AreSame(minimal, small.Settings);
                Assert.LessOrEqual(small.DurationMs, full.DurationMs);
                Assert.AreEqual(full.Beats.Count, small.Beats.Count);
                for (int i = 0; i < small.Beats.Count; i++)
                {
                    Assert.AreSame(minimal, small.Beats[i].Timeline.Settings);
                    Assert.AreEqual(full.Beats[i].Timeline.ImpactMs, small.Beats[i].Timeline.ImpactMs);
                    foreach (BeatOverlay overlay in small.Beats[i].Overlays)
                    {
                        Assert.AreSame(minimal, overlay.Timeline.Settings);
                    }

                    beats++;
                }
            }

            Assert.Greater(beats, 5);
        }

        // ------------------------------------------------------------------------------------------
        // Repeated on-apply overlays
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AStatusLandingAgain_PlaysItsOverlayEveryTime()
        {
            GameContent content = VfxLibraryTests.Content;
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out _)));
            PlayedTurn real = null;
            PlayedTurn turn;
            while ((turn = playback.Advance()) != null)
            {
                if (turn.Beats.Count > 0 && turn.Beats[0].Targets.Count > 0 && turn.After.ContainsKey(turn.Beats[0].Targets[0].UnitId) &&
                    !turn.After[turn.Beats[0].Targets[0].UnitId].Defeated)
                {
                    real = turn;
                    break;
                }
            }

            Assert.IsNotNull(real);
            string unitId = real.Beats[0].Targets[0].UnitId;
            BeatTarget[] targets = { new BeatTarget(unitId, 0, false) };
            BeatApplied[] stun = { new BeatApplied(unitId, VfxEffectKey.Stun) };
            string[] keys = { VfxEffectKey.Stun };
            List<SkillBeat> beats = new List<SkillBeat>
            {
                new SkillBeat(real.Turn.Unit.Id, "first", "First", BeastCraft.Creatures.Element.Ice, targets, null, keys, stun),
                new SkillBeat(real.Turn.Unit.Id, "second", "Second", BeastCraft.Creatures.Element.Ice, targets, null, keys, stun)
            };
            PlayedTurn twice = new PlayedTurn(real.Index, real.Turn, beats, new Dictionary<string, UnitSnapshot>(real.Before), new Dictionary<string, UnitSnapshot>(real.After));

            TurnAnimation animation = new TurnAnimation(twice, new HexLayout(0, 0), content.Vfx, 3);
            VfxTimeline alone = new VfxTimeline(content.Vfx.OnApply(VfxEffectKey.Stun), Vec2.Zero, new[] { new VfxTarget(unitId, Vec2.Zero, 0, false) }, 1);

            foreach (ScheduledBeat beat in animation.Beats)
            {
                Assert.AreEqual(1, beat.Overlays.Count, beat.Beat.SkillId + ": the stun plays on this beat too");
                Assert.AreEqual(VfxEffectKey.Stun, beat.Overlays[0].Key);
                Assert.AreEqual(alone.DurationMs, beat.Overlays[0].Timeline.DurationMs, "in full");
                Assert.GreaterOrEqual(beat.DurationMs, beat.Overlays[0].OffsetMs + alone.DurationMs);
            }
        }

        [Test]
        public void DemoBattle_OverlaysFollowWhatLanded_IncludingOnUnitsThatHadItAlready()
        {
            int overlays = 0;
            int again = 0;
            foreach (int seed in new[] { DemoBattle.DefaultSeed, 7, 99 })
            {
                GameContent content = VfxLibraryTests.Content;
                BattlePlayback playback = new BattlePlayback(BattleSession.Begin(DemoBattle.Create(content, seed, out _, out _)));
                PlayedTurn turn;
                while ((turn = playback.Advance()) != null)
                {
                    TurnAnimation animation = new TurnAnimation(turn, new HexLayout(0, 0), content.Vfx, seed);
                    foreach (ScheduledBeat beat in animation.Beats)
                    {
                        Assert.IsNotNull(beat.Beat.Applied, "real beats carry the battle's record");
                        foreach (BeatApplied landed in beat.Beat.Applied)
                        {
                            CollectionAssert.Contains(beat.Beat.EffectKeys, landed.Key, "only what the skill can apply");
                        }

                        foreach (BeatOverlay overlay in beat.Overlays)
                        {
                            Assert.IsTrue(System.Array.Exists(ToArray(beat.Beat.Applied), a => a.UnitId == overlay.UnitId && a.Key == overlay.Key),
                                          "an overlay is something that landed");
                            overlays++;
                            if (overlay.Key != VfxEffectKey.Knockback && turn.Before[overlay.UnitId].StatusKeys().Contains(overlay.Key))
                            {
                                again++;
                            }
                        }
                    }
                }
            }

            Assert.Greater(overlays, 5);
            Assert.Greater(again, 0, "somewhere a status lands on a unit that already had it, and still plays");
        }

        private static BeatApplied[] ToArray(IReadOnlyList<BeatApplied> list)
        {
            BeatApplied[] array = new BeatApplied[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                array[i] = list[i];
            }

            return array;
        }

        private static string Describe(VfxFrame frame)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append(frame.Finished).Append(frame.Impacted).Append(frame.HitStop).Append('|').Append(frame.FlipbookFrame).Append('|').Append(frame.Shake).Append('|')
                .Append(frame.FlashAlpha).Append('|');
            foreach (Vec2 p in frame.Projectiles)
            {
                text.Append(p).Append(';');
            }

            foreach (ParticleState p in frame.Particles)
            {
                text.Append(p.Position).Append(p.ColorIndex).Append(p.Alpha).Append(';');
            }

            foreach (DamageNumberState n in frame.DamageNumbers)
            {
                text.Append(n.Value).Append(n.Position).Append(n.Alpha).Append(';');
            }

            foreach (VfxSprite s in frame.Sprites)
            {
                text.Append(s.Sheet).Append(s.Frame).Append(s.Position).Append(s.Scale).Append(s.Alpha).Append(';');
            }

            return text.ToString();
        }
    }
}
