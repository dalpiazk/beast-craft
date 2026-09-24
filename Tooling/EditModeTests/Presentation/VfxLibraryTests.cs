using System.Collections.Generic;
using System.IO;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Content;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The shipped VFX library (<c>Data/Vfx/vfx-library.json</c>) against the real skills and the
    /// real pixel-art manifest, <see cref="VfxLibraryValidator"/>'s rules one by one, and
    /// <see cref="VfxLibrary"/>'s fallback order.
    /// </summary>
    public class VfxLibraryTests
    {
        private static GameContent _content;

        /// <summary>The repo's content, loaded (and fully validated) once for the presentation tests.</summary>
        internal static GameContent Content
        {
            get
            {
                if (_content == null)
                {
                    List<string> errors = new List<string>();
                    _content = GameContent.Load(GameContent.FindRoot(), errors);
                    Assert.IsNotNull(_content, "content failed to load:\n" + string.Join("\n", errors));
                }

                return _content;
            }
        }

        [Test]
        public void ShippedLibrary_IsValid_AgainstTheRealSkillsAndArt()
        {
            string root = GameContent.FindRoot();
            VfxLibraryData data = FieldJson.FromJson<VfxLibraryData>(File.ReadAllText(GameContent.PathOf(root, VfxLibraryData.ProjectRelativePath)));
            PixelArtManifestData art = FieldJson.FromJson<PixelArtManifestData>(File.ReadAllText(GameContent.PathOf(root, PixelArtManifestData.ProjectRelativePath)));

            List<string> errors = VfxLibraryValidator.Validate(data, Content.KnownSkillIds, art);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.Greater(art.Sprites.Length, 20);
            Assert.IsTrue(art.Palette.ContainsKey("o"));
        }

        [Test]
        public void EverySkill_ResolvesToAnEffect_AndThePhoenixFireSkillsAreAuthored()
        {
            foreach (string skillId in Content.KnownSkillIds)
            {
                SkillSO skill = Content.Battle.GetSkill(skillId);
                Element element = skill == null ? Element.None : skill.Element;
                Assert.IsNotNull(Content.Vfx.Resolve(skillId, element), skillId);
            }

            Assert.IsTrue(Content.Vfx.HasOwnEffect("ember_shot"));
            Assert.IsTrue(Content.Vfx.HasOwnEffect("flame_wave"));
            Assert.AreEqual("fx_fire_burst", Content.Vfx.Resolve("ember_shot", Element.Fire).Flipbook.Sheet);
        }

        [Test]
        public void Resolve_FallsBackToTheElementThenNone()
        {
            VfxEffectData fire = Effect();
            VfxEffectData none = Effect();
            VfxEffectData own = Effect();
            VfxLibraryData data = new VfxLibraryData
            {
                SchemaVersion = 1,
                ElementDefaults = new[]
                {
                    new VfxElementDefaultData { Element = "Fire", Effect = fire },
                    new VfxElementDefaultData { Element = "None", Effect = none }
                },
                Skills = new[] { new VfxSkillEffectData { SkillId = "mine", Effect = own } }
            };

            VfxLibrary library = VfxLibrary.Build(data);

            Assert.AreSame(own, library.Resolve("mine", Element.Water));
            Assert.AreSame(fire, library.Resolve("other", Element.Fire));
            Assert.AreSame(none, library.Resolve("other", Element.Water));
            Assert.IsNull(VfxLibrary.Build(null).Resolve("x", Element.Fire));
        }

        [Test]
        public void Validate_AMinimalLibrary_IsValid()
        {
            Assert.IsEmpty(VfxLibraryValidator.Validate(Minimal(), new[] { "zap" }, Art()));
        }

        [Test]
        public void Validate_RequiresOneDefaultPerElement()
        {
            VfxLibraryData data = Minimal();
            data.ElementDefaults = System.Array.FindAll(data.ElementDefaults, d => d.Element != "Ice");
            AssertError(data, "no entry for Ice");

            data = Minimal();
            List<VfxElementDefaultData> twice = new List<VfxElementDefaultData>(data.ElementDefaults) { new VfxElementDefaultData { Element = "Fire", Effect = Effect() } };
            data.ElementDefaults = twice.ToArray();
            AssertError(data, "'Fire' is listed twice");

            data = Minimal();
            data.ElementDefaults[0].Element = "Plasma";
            AssertError(data, "'Plasma' is not an element");
        }

        [Test]
        public void Validate_SkillIdsMustResolveAndBeUnique()
        {
            VfxLibraryData data = Minimal();
            data.Skills = new[] { new VfxSkillEffectData { SkillId = "nope", Effect = Effect() } };
            AssertError(data, "'nope' is not a skill");

            data.Skills = new[] { new VfxSkillEffectData { SkillId = "zap", Effect = Effect() }, new VfxSkillEffectData { SkillId = "zap", Effect = Effect() } };
            AssertError(data, "'zap' is listed twice");
        }

        [Test]
        public void Validate_SheetsAndColoursMustExistInTheArt()
        {
            VfxLibraryData data = Minimal();
            data.ElementDefaults[0].Effect.Flipbook.Sheet = "missing_sheet";
            AssertError(data, "sheet 'missing_sheet' is not in the pixel-art manifest");

            data = Minimal();
            data.ElementDefaults[0].Effect.Flipbook.FrameWidth = 16;
            AssertError(data, "is not sheet 'burst''s 8x8");

            data = Minimal();
            data.ElementDefaults[0].Effect.Flipbook.Frames = 9;
            AssertError(data, "9 frames, but sheet 'burst' has 4");

            data = Minimal();
            data.ElementDefaults[0].Effect.Particles.Colors = new[] { "Q" };
            AssertError(data, "'Q' is not in the palette");

            data = Minimal();
            data.ElementDefaults[0].Effect.DamageNumber.Color = "ab";
            AssertError(data, "'ab' is not a single palette char");
        }

        [Test]
        public void Validate_MotionAndRanges()
        {
            VfxLibraryData data = Minimal();
            data.ElementDefaults[0].Effect.Motion = "Teleport";
            AssertError(data, "Motion 'Teleport'");

            data = Minimal();
            data.ElementDefaults[0].Effect.TravelMs = 0;
            AssertError(data, "a Projectile needs TravelMs");

            data = Minimal();
            data.ElementDefaults[0].Effect.Motion = VfxMotion.Instant;
            AssertError(data, "an Instant effect has TravelMs 0");

            data = Minimal();
            data.ElementDefaults[0].Effect.HitStopMs = 5000;
            AssertError(data, "HitStopMs 5000 is outside 0-250");

            data = Minimal();
            data.ElementDefaults[0].Effect.ScreenShake.Amplitude = 20f;
            AssertError(data, "ScreenShake Amplitude 20 is outside 0-8");

            data = Minimal();
            data.ElementDefaults[0].Effect.Particles.SpeedMin = 90f;
            AssertError(data, "SpeedMin 90 is above SpeedMax 50");

            data = Minimal();
            data.ElementDefaults[0].Effect.Particles.Count = 0;
            AssertError(data, "Particles Count 0 is outside 1-64");

            data = Minimal();
            data.ElementDefaults[0].Effect.Flipbook.Fps = 0;
            AssertError(data, "Fps 0 is outside 1-60");

            data = Minimal();
            data.ElementDefaults[0].Effect = null;
            AssertError(data, "no Effect");

            data = Minimal();
            data.SchemaVersion = 2;
            AssertError(data, "SchemaVersion is 2");
        }

        private static void AssertError(VfxLibraryData data, string fragment)
        {
            List<string> errors = VfxLibraryValidator.Validate(data, new[] { "zap" }, Art());
            Assert.IsTrue(errors.Exists(e => e.Contains(fragment)), "expected an error containing '" + fragment + "', got:\n" + string.Join("\n", errors));
        }

        /// <summary>A valid library: one full default per element and one skill entry.</summary>
        internal static VfxLibraryData Minimal()
        {
            List<VfxElementDefaultData> defaults = new List<VfxElementDefaultData>();
            foreach (Element element in (Element[])System.Enum.GetValues(typeof(Element)))
            {
                defaults.Add(new VfxElementDefaultData { Element = element.ToString(), Effect = Effect() });
            }

            return new VfxLibraryData
            {
                SchemaVersion = VfxLibraryData.CurrentSchemaVersion,
                ElementDefaults = defaults.ToArray(),
                Skills = new[] { new VfxSkillEffectData { SkillId = "zap", Effect = Effect() } }
            };
        }

        /// <summary>A full projectile effect over the test <see cref="Art"/>.</summary>
        internal static VfxEffectData Effect()
        {
            return new VfxEffectData
            {
                Motion = VfxMotion.Projectile,
                TravelMs = 200,
                Projectile = new VfxSpriteData { Sheet = "dot", Tint = "a", Scale = 1 },
                Flipbook = new VfxFlipbookData { Sheet = "burst", FrameWidth = 8, FrameHeight = 8, Frames = 4, Fps = 10 },
                Particles = new VfxParticleData { Sheet = "dot", Count = 6, SpeedMin = 10f, SpeedMax = 50f, LifetimeMs = 300, Colors = new[] { "a", "b" }, Gravity = 100f },
                ScreenShake = new VfxShakeData { Amplitude = 2f, DurationMs = 200 },
                HitFlash = new VfxFlashData { Tint = "a", DurationMs = 100 },
                HitStopMs = 50,
                DamageNumber = new VfxDamageNumberData { Color = "a", CritColor = "b", RiseMs = 600, RisePx = 10 }
            };
        }

        internal static PixelArtManifestData Art()
        {
            return new PixelArtManifestData
            {
                SchemaVersion = 1,
                Palette = new Dictionary<string, string> { { "a", "#ffffff" }, { "b", "#ff0000" } },
                Sprites = new[]
                {
                    new PixelSpriteData { Name = "burst", FrameWidth = 8, FrameHeight = 8, Frames = 4 },
                    new PixelSpriteData { Name = "dot", FrameWidth = 3, FrameHeight = 3, Frames = 1 }
                }
            };
        }
    }
}
