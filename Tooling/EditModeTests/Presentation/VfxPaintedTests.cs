using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Save;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Painted VFX frames (<see cref="VfxPaintedData"/>): a single painted frame animated by scale,
    /// rotation, alpha and tint curves, as an alternative to a flipbook on every layer type, on a
    /// particle burst and on an aura. The curve maths (<see cref="VfxPainted"/>), each layer type's
    /// painted sprites, determinism, the effects-settings rules (unchanged: a painted layer plays or
    /// drops exactly as its flipbook would), the flipbook path untouched, the shipped library's
    /// painted slots, and the validator's rules.
    /// </summary>
    public class VfxPaintedTests
    {
        private const float Tolerance = 0.0001f;

        private static VfxCurveKey[] Curve(params float[] tv)
        {
            VfxCurveKey[] keys = new VfxCurveKey[tv.Length / 2];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new VfxCurveKey { T = tv[2 * i], V = tv[2 * i + 1] };
            }

            return keys;
        }

        [Test]
        public void Curve_IsPiecewiseLinear_ClampedAtItsEnds()
        {
            VfxCurveKey[] keys = Curve(0.2f, 1f, 0.6f, 3f, 1f, 2f);

            Assert.AreEqual(1f, VfxPainted.Evaluate(keys, 0f, 9f), Tolerance, "before the first key: its value");
            Assert.AreEqual(1f, VfxPainted.Evaluate(keys, 0.2f, 9f), Tolerance);
            Assert.AreEqual(2f, VfxPainted.Evaluate(keys, 0.4f, 9f), Tolerance, "half way from 1 to 3");
            Assert.AreEqual(2.5f, VfxPainted.Evaluate(keys, 0.8f, 9f), Tolerance);
            Assert.AreEqual(2f, VfxPainted.Evaluate(keys, 1f, 9f), Tolerance);
            Assert.AreEqual(2f, VfxPainted.Evaluate(keys, 5f, 9f), Tolerance, "after the last key: its value");
            Assert.AreEqual(9f, VfxPainted.Evaluate(new VfxCurveKey[0], 0.5f, 9f), "an empty curve: the fallback");
            Assert.AreEqual(9f, VfxPainted.Evaluate(null, 0.5f, 9f));
            Assert.AreEqual(4f, VfxPainted.Evaluate(Curve(0.5f, 4f), 0.1f, 9f), "one key: constant");
        }

        [Test]
        public void TintCurve_BlendsBetweenNeighbouringKeys()
        {
            VfxTintKey[] keys = { new VfxTintKey { T = 0f, Color = "Y" }, new VfxTintKey { T = 0.5f, Color = "o" }, new VfxTintKey { T = 1f, Color = "r" } };

            VfxPainted.Tint(keys, 0.25f, "w", out string from, out string to, out float mix);
            Assert.AreEqual("Y", from);
            Assert.AreEqual("o", to);
            Assert.AreEqual(0.5f, mix, Tolerance);

            VfxPainted.Tint(keys, 1.5f, "w", out from, out to, out mix);
            Assert.AreEqual("r", from);
            Assert.AreEqual("r", to);

            VfxPainted.Tint(null, 0.3f, "w", out from, out to, out mix);
            Assert.AreEqual("w", from, "no tint curve: the part's own tint");
            Assert.AreEqual(0f, mix);
        }

        [Test]
        public void EveryLayerType_CanDrawAPaintedFrame_WithItsCurves()
        {
            VfxPaintedData painted = new VfxPaintedData
            {
                Sheet = "hero",
                Scale = Curve(0f, 0.5f, 1f, 1.5f),
                Rotation = Curve(0f, 0f, 1f, 0.5f),
                Alpha = Curve(0f, 1f, 1f, 0f),
                Tint = new[] { new VfxTintKey { T = 0f, Color = "Y" }, new VfxTintKey { T = 1f, Color = "r" } }
            };
            foreach (string type in new[] { VfxLayerType.Flipbook, VfxLayerType.GroundDecal, VfxLayerType.Shockwave, VfxLayerType.RadialBurst, VfxLayerType.Glyphs })
            {
                VfxEffectData effect = new VfxEffectData
                {
                    Layers = new[]
                    {
                        new VfxLayerData
                        {
                            Type = type, DurationMs = 400, Sheet = "old", Tint = "4", Scale = 2f, StartRadius = 0.5f, EndRadius = 1f, Count = 3, Frames = 0, Painted = painted
                        }
                    }
                };
                VfxTimeline timeline = new VfxTimeline(effect, Vec2.Zero, new[] { new VfxTarget("t", new Vec2(10f, 0f), 5, false) }, 3);
                VfxTimeline plain = new VfxTimeline(new VfxEffectData { Layers = new[] { Unpainted(effect.Layers[0]) } }, Vec2.Zero,
                                                    new[] { new VfxTarget("t", new Vec2(10f, 0f), 5, false) }, 3);

                List<VfxSprite> sprites = timeline.Sample(200).Sprites;
                List<VfxSprite> flip = plain.Sample(200).Sprites;
                Assert.IsNotEmpty(sprites, type);
                Assert.AreEqual(flip.Count, sprites.Count, type + ": as many sprites as the type places");
                for (int i = 0; i < sprites.Count; i++)
                {
                    VfxSprite s = sprites[i];
                    Assert.IsTrue(s.Painted, type);
                    Assert.AreEqual("hero", s.Sheet, type);
                    Assert.AreEqual(0, s.Frame, type + ": one frame");
                    Assert.AreEqual(flip[i].Position, s.Position, type + ": placed as the type places it");
                    Assert.AreEqual(flip[i].Scale * 1f, s.Scale, Tolerance, type + ": the scale curve at t 0.5 is 1");
                    Assert.AreEqual(flip[i].SizePx * 1f, s.SizePx, Tolerance, type);
                    Assert.AreEqual(flip[i].Rotation + 0.25f * Math.PI * 2, s.Rotation, 0.001, type + ": a quarter turn at t 0.5");
                    Assert.AreEqual(0.5f, s.Alpha, Tolerance, type + ": the alpha curve replaces the type's own fade");
                    Assert.AreEqual("Y", s.Tint, type);
                    Assert.AreEqual("r", s.TintTo, type);
                    Assert.AreEqual(0.5f, s.TintMix, Tolerance, type);
                }
            }
        }

        [Test]
        public void PaintedParticles_FollowEachParticlesLife_AndSpinFromTheSeed()
        {
            VfxParticleData burst = new VfxParticleData
            {
                Count = 6,
                SpeedMin = 20f,
                SpeedMax = 40f,
                LifetimeMs = 500,
                Colors = new[] { "o" },
                Painted = new VfxPaintedData { Sheet = "ember", Scale = Curve(0f, 1f, 1f, 0f), RandomSpin = true }
            };
            VfxEffectData effect = new VfxEffectData { Layers = new[] { new VfxLayerData { Type = VfxLayerType.Particles, DurationMs = 600, Particles = burst } } };
            VfxTimeline timeline = new VfxTimeline(effect, Vec2.Zero, new[] { new VfxTarget("t", Vec2.Zero, 0, false) }, 11);

            List<VfxSprite> early = timeline.Sample(50).Sprites;
            Assert.AreEqual(6, early.Count);
            HashSet<float> spins = new HashSet<float>();
            foreach (VfxSprite s in early)
            {
                Assert.IsTrue(s.Painted);
                Assert.AreEqual("ember", s.Sheet);
                Assert.AreEqual("o", s.Tint, "no tint curve: the particle's own colour");
                Assert.Greater(s.Scale, 0.7f, "young particles are near full size");
                spins.Add(s.Rotation);
            }

            Assert.Greater(spins.Count, 3, "each particle has its own spin");
            foreach (VfxSprite s in timeline.Sample(250).Sprites)
            {
                Assert.Less(s.Scale, 0.7f, "older particles shrink along their own life");
            }

            Assert.AreEqual(Positions(timeline.Sample(250).Sprites), Positions(new VfxTimeline(effect, Vec2.Zero, new[] { new VfxTarget("t", Vec2.Zero, 0, false) }, 11).Sample(250).Sprites));
        }

        [Test]
        public void PaintedLayers_AreDeterministic_AndKeepTheEffectsSettingsRules()
        {
            VfxEffectData effect = VfxLibraryTests.Content.Vfx.Resolve("boulder_slam", Element.Earth);
            Assert.IsTrue(Array.Exists(effect.Layers, l => l.Painted != null), "the Earth default draws painted frames");
            VfxTarget[] targets = { new VfxTarget("t", new Vec2(40f, -30f), 22, false) };

            VfxTimeline a = new VfxTimeline(effect, Vec2.Zero, targets, 5);
            VfxTimeline b = new VfxTimeline(effect, Vec2.Zero, targets, 5);
            for (int ms = 0; ms < a.DurationMs; ms += 37)
            {
                Assert.AreEqual(Describe(a.Sample(ms).Sprites), Describe(b.Sample(ms).Sprites), "at " + ms);
            }

            int impact = a.HitStopEndMs + 100;
            List<VfxSprite> full = a.Sample(impact).Sprites;
            Assert.IsTrue(full.Exists(s => s.Painted && s.Sheet == "fx_painted_earth_burst"), Describe(full));
            Assert.IsTrue(full.Exists(s => s.Painted && s.Sheet == "fx_painted_earth_ring"));
            Assert.IsTrue(full.Exists(s => s.Painted && s.Sheet == "fx_painted_earth_decal"));

            VfxTimeline reduced = new VfxTimeline(effect, Vec2.Zero, targets, 5, null, new VfxSettings(EffectsIntensity.Reduced, true, true));
            List<VfxSprite> fewer = reduced.Sample(impact).Sprites;
            Assert.IsFalse(fewer.Exists(s => s.Sheet == "fx_painted_earth_glyph"), "Reduced drops glyph layers, painted or not");
            foreach (VfxSprite s in fewer)
            {
                Assert.IsTrue(full.Exists(f => f.Sheet == s.Sheet && f.Position.Equals(s.Position) && f.Scale == s.Scale), "what still plays is drawn as at Full: " + s.Sheet);
            }

            Assert.IsEmpty(new VfxTimeline(effect, Vec2.Zero, targets, 5, null, new VfxSettings(EffectsIntensity.Minimal, true, true)).Sample(impact).Sprites,
                           "Minimal drops every layer");
            List<VfxSprite> noFlash = new VfxTimeline(effect, Vec2.Zero, targets, 5, null, new VfxSettings(EffectsIntensity.Full, true, false)).Sample(impact).Sprites;
            Assert.IsFalse(noFlash.Exists(s => s.Sheet == "fx_painted_earth_burst"), "flashes off drops the bright additive burst, painted too");
            Assert.IsTrue(noFlash.Exists(s => s.Sheet == "fx_painted_earth_ring"), "a shockwave stays");
        }

        [Test]
        public void TheFlipbookPath_IsUntouched_WithoutPainted()
        {
            VfxEffectData ember = VfxLibraryTests.Content.Vfx.Resolve("ember_shot", Element.Fire);
            VfxTimeline timeline = new VfxTimeline(ember, Vec2.Zero, new[] { new VfxTarget("t", new Vec2(30f, 0f), 50, false) }, 9);
            List<VfxSprite> sprites = timeline.Sample(timeline.HitStopEndMs + 120).Sprites;

            Assert.IsNotEmpty(sprites);
            Assert.IsFalse(sprites.Exists(s => s.Painted), "Phoenix's Ember Shot keeps its pixel flipbooks");
            Assert.IsTrue(sprites.Exists(s => s.Sheet == "fx_fire_burst" && s.Frame > 0), "a flipbook still steps its frames");
            foreach (VfxSprite s in sprites)
            {
                Assert.AreEqual(s.Tint, s.TintTo);
                Assert.AreEqual(0f, s.TintMix);
            }
        }

        [Test]
        public void PaintedAura_LoopsItsCurvesOverThePulse_AndTheEnemyAuraRulesHold()
        {
            VfxAuraData aura = new VfxAuraData
            {
                Scale = 1f,
                PulseMs = 1000,
                Alpha = 0.8f,
                Blend = VfxBlend.Additive,
                Painted = new VfxPaintedData { Sheet = "halo", Scale = Curve(0f, 1f, 0.5f, 2f, 1f, 1f), Rotation = Curve(0f, 0f, 1f, 1f), Alpha = Curve(0f, 0.5f, 1f, 0.5f) }
            };

            Assert.IsTrue(VfxAuraSampler.TrySample(aura, new Vec2(5f, 5f), 1f, 250, out VfxSprite quarter));
            Assert.IsTrue(quarter.Painted);
            Assert.AreEqual("halo", quarter.Sheet);
            Assert.AreEqual(1.5f, quarter.Scale, Tolerance, "the scale curve replaces the breathing");
            Assert.AreEqual(Math.PI / 2, quarter.Rotation, 0.001, "a quarter of the pulse, a quarter turn");
            Assert.AreEqual(0.4f, quarter.Alpha, Tolerance, "the alpha curve times the aura's Alpha");

            Assert.IsTrue(VfxAuraSampler.TrySample(aura, new Vec2(5f, 5f), 1f, 1250, out VfxSprite again));
            Assert.AreEqual(quarter.Scale, again.Scale, Tolerance, "it loops every pulse");

            Assert.IsTrue(VfxAuraSampler.TrySampleEnemyAura(aura, Vec2.Zero, 1f, 250, new VfxSettings(EffectsIntensity.Reduced, true, true), out VfxSprite reduced));
            Assert.AreEqual(0.2f, reduced.Alpha, Tolerance, "Reduced halves a painted haze too");
            Assert.IsTrue(reduced.Painted);
            Assert.IsFalse(VfxAuraSampler.TrySampleEnemyAura(aura, Vec2.Zero, 1f, 250, new VfxSettings(EffectsIntensity.Minimal, true, true), out _));
        }

        [Test]
        public void ShippedLibrary_DrawsThePaintedSlots_PerElementAndStatus()
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;
            foreach (string element in new[] { "fire", "water", "earth", "air", "lightning", "ice", "nature", "metal", "light", "dark" })
            {
                foreach (string kind in new[] { "burst", "ring", "decal", "glyph", "ember" })
                {
                    ArtSpriteData frame = art.Find("fx_painted_" + element + "_" + kind);
                    Assert.IsNotNull(frame, element + " " + kind);
                    Assert.AreEqual(ArtFilter.Linear, frame.Filter, "painted frames are linear-filtered and mipmapped");
                    Assert.AreEqual(1, frame.Frames);
                    Assert.AreEqual("../vfx/" + element + "/" + kind + ".png", frame.File);
                }
            }

            foreach (string status in new[] { "stun", "shield", "burn", "poison", "taunt", "cleanse", "heal", "buff", "debuff" })
            {
                Assert.IsNotNull(art.Find("fx_painted_status_" + status), status);
            }

            VfxLibrary vfx = VfxLibraryTests.Content.Vfx;
            Assert.AreEqual("fx_painted_status_shield", vfx.Aura(VfxEffectKey.Shield).Painted.Sheet);
            Assert.AreEqual("fx_painted_status_stun", vfx.Aura(VfxEffectKey.Stun).Painted.Sheet);
            Assert.IsTrue(Array.Exists(vfx.OnApply(VfxEffectKey.Heal).Layers, l => l.Painted != null && l.Painted.Sheet == "fx_painted_status_heal"));
            Assert.IsTrue(Array.Exists(vfx.OnApply(VfxEffectKey.Cleanse).Layers, l => l.Painted != null && l.Painted.Sheet == "fx_painted_status_cleanse"));
            Assert.AreEqual("fx_painted_fire_ember", vfx.Resolve("unknown_fire_skill", Element.Fire).Particles.Painted.Sheet, "the effect's own burst can be painted too");
        }

        [Test]
        public void Validator_ChecksPaintedFrames()
        {
            ArtManifestData art = new ArtManifestData
            {
                Palette = new Dictionary<string, string> { { "o", "#ff8800" } },
                Sprites = new[]
                {
                    new ArtSpriteData { Name = "hero", File = "h.png", Kind = ArtSpriteKind.Sprite, Frames = 1, Filter = ArtFilter.Linear },
                    new ArtSpriteData { Name = "strip", File = "s.png", Kind = ArtSpriteKind.Sprite, Frames = 4, Filter = ArtFilter.Point }
                }
            };
            List<string> errors = new List<string>();
            VfxLibraryValidator.ValidatePainted(new VfxPaintedData { Sheet = "hero", Scale = Curve(0f, 1f, 1f, 2f) }, "ok", art, errors);
            Assert.IsEmpty(errors, string.Join("\n", errors));

            VfxLibraryValidator.ValidatePainted(new VfxPaintedData
            {
                Sheet = "strip",
                Scale = Curve(0.5f, 1f, 0.2f, 9f),
                Rotation = Curve(0f, 12f),
                Alpha = Curve(1.5f, 0.5f),
                Tint = new[] { new VfxTintKey { T = 0f, Color = "q" } }
            }, "bad", art, errors);
            string text = string.Join("\n", errors);
            StringAssert.Contains("has 4 frames; a painted frame is one", text);
            StringAssert.Contains("a painted frame is linear", text);
            StringAssert.Contains("ascending T", text);
            StringAssert.Contains("bad Scale[1] V 9", text);
            StringAssert.Contains("bad Rotation[0] V 12", text);
            StringAssert.Contains("bad Alpha[0] T 1.5", text);
            StringAssert.Contains("'q' is not in the palette", text);

            errors.Clear();
            VfxLibraryValidator.ValidateLayer(new VfxLayerData { Type = VfxLayerType.Flipbook, DurationMs = 300, Frames = 0, Painted = new VfxPaintedData { Sheet = "hero" } },
                                              "layer", art, errors);
            Assert.IsEmpty(errors, "a painted flipbook layer needs no Sheet or Frames: " + string.Join("\n", errors));
            VfxLibraryValidator.ValidateLayer(new VfxLayerData
            {
                Type = VfxLayerType.Particles,
                DurationMs = 300,
                Painted = new VfxPaintedData { Sheet = "hero" },
                Particles = new VfxParticleData { Count = 2, LifetimeMs = 200, Colors = new[] { "o" }, Painted = new VfxPaintedData { Sheet = "hero" } }
            }, "particles", art, errors);
            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("its Particles' Painted", errors[0]);

            errors.Clear();
            VfxLibraryValidator.ValidateAura(new VfxAuraData { Painted = new VfxPaintedData { Sheet = "hero" } }, "aura", art, errors);
            Assert.IsEmpty(errors, "a painted aura needs no Sheet: " + string.Join("\n", errors));
        }

        private static VfxLayerData Unpainted(VfxLayerData layer)
        {
            return new VfxLayerData
            {
                Type = layer.Type,
                DurationMs = layer.DurationMs,
                Sheet = layer.Sheet,
                Tint = layer.Tint,
                Scale = layer.Scale,
                StartRadius = layer.StartRadius,
                EndRadius = layer.EndRadius,
                Count = layer.Count,
                Frames = 1
            };
        }

        private static string Positions(List<VfxSprite> sprites)
        {
            List<string> parts = new List<string>();
            foreach (VfxSprite s in sprites)
            {
                parts.Add(s.Position + "@" + s.Rotation);
            }

            return string.Join(";", parts);
        }

        private static string Describe(List<VfxSprite> sprites)
        {
            List<string> parts = new List<string>();
            foreach (VfxSprite s in sprites)
            {
                parts.Add(s.Sheet + "#" + s.Frame + " " + s.Position + " x" + s.Scale + "/" + s.SizePx + " r" + s.Rotation + " a" + s.Alpha + " " + s.Tint + ">" + s.TintTo);
            }

            return string.Join("\n", parts);
        }
    }
}
