using System.Collections.Generic;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The art manifest's schema v2 (<see cref="ArtManifestData"/>): the shipped placeholder
    /// manifest, reading a v1 manifest, <see cref="ArtManifestValidator"/>'s rules, the reserved
    /// <c>spine</c> kind and clip timing.
    /// </summary>
    public class ArtManifestTests
    {
        private const string V1 = @"{
  ""SchemaVersion"": 1,
  ""Palette"": { ""K"": ""#1c1428"" },
  ""Sprites"": [
    { ""Name"": ""beast_a"", ""File"": ""beast_a.png"", ""FrameWidth"": 32, ""FrameHeight"": 32, ""Frames"": 1, ""FrameMs"": 0,
      ""Kind"": ""beast"", ""Label"": ""A"", ""ArtKey"": ""beast/a"" },
    { ""Name"": ""fx_b"", ""File"": ""fx_b.png"", ""FrameWidth"": 16, ""FrameHeight"": 20, ""Frames"": 4, ""FrameMs"": 70,
      ""Kind"": ""fx"", ""Label"": ""B"", ""ArtKey"": """" }
  ]
}";

        [Test]
        public void ShippedManifest_IsV2_Valid_AndPixelPlaceholders()
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;

            Assert.AreEqual(ArtManifestData.CurrentSchemaVersion, art.SchemaVersion);
            Assert.IsEmpty(ArtManifestValidator.Validate(art), string.Join("\n", ArtManifestValidator.Validate(art)));
            foreach (ArtSpriteData sprite in art.Sprites)
            {
                Assert.AreEqual(ArtSpriteKind.Sprite, sprite.Kind, sprite.Name);
                Assert.AreEqual(ArtFilter.Point, sprite.Filter, sprite.Name);
                Assert.AreEqual(ArtManifestData.ReferencePixelsPerUnit, sprite.PixelsPerUnit, sprite.Name);
                Assert.IsFalse(sprite.Premultiplied, sprite.Name);
                Assert.IsFalse(string.IsNullOrEmpty(sprite.Category), sprite.Name);
            }

            ArtSpriteData phoenix = art.FindByArtKey("beast/phoenix");
            Assert.AreEqual(16f, phoenix.PivotX);
            Assert.AreEqual(24f, phoenix.PivotY, "a beast's pivot is its feet, three quarters down");
            ArtAnimationData idle = phoenix.Animation("idle");
            Assert.IsNotNull(idle);
            Assert.AreEqual("beast_phoenix_idle", idle.Sheet);
            CollectionAssert.AreEqual(new[] { 0, 1 }, idle.Frames);
            Assert.AreEqual(18f, art.Find("hex_grass").PivotY, "a tile's pivot is its centre");
        }

        [Test]
        public void V1_StillReads_AsWhatItMeant()
        {
            ArtManifestData art = ArtManifestData.Normalize(FieldJson.FromJson<ArtManifestData>(V1));

            Assert.IsEmpty(ArtManifestValidator.Validate(art), string.Join("\n", ArtManifestValidator.Validate(art)));
            ArtSpriteData a = art.Find("beast_a");
            Assert.AreEqual(ArtSpriteKind.Sprite, a.Kind);
            Assert.AreEqual("beast", a.Category);
            Assert.AreEqual(16f, a.PivotX);
            Assert.AreEqual(16f, a.PivotY);
            Assert.AreEqual(32f, a.PixelsPerUnit);
            Assert.AreEqual(ArtFilter.Point, a.Filter);
            Assert.IsFalse(a.Premultiplied);
            Assert.AreEqual(8f, art.Find("fx_b").PivotX);
            Assert.AreEqual(10f, art.Find("fx_b").PivotY);
            Assert.AreEqual("fx", art.Find("fx_b").Category);
            Assert.AreSame(art.Find("beast_a"), art.FindByArtKey("beast/a"));
        }

        [Test]
        public void Validator_ReportsEachBrokenField()
        {
            ArtManifestData art = Valid();
            art.Sprites[0].Filter = "bilinear";
            art.Sprites[0].PixelsPerUnit = 0f;
            art.Sprites[0].PivotY = 40f;
            art.Sprites[0].Tint = "red";
            art.Sprites[1].Name = art.Sprites[0].Name;
            art.Sprites[2].Animations = new[]
            {
                new ArtAnimationData { Name = "idle", Sheet = "nope", Frames = new[] { 0 }, Fps = 8 },
                new ArtAnimationData { Name = "walk", Frames = new[] { 0, 5 }, Fps = 0 }
            };
            art.SchemaVersion = 3;

            List<string> errors = ArtManifestValidator.Validate(art);

            string all = string.Join("\n", errors);
            StringAssert.Contains("SchemaVersion", all);
            StringAssert.Contains("Filter 'bilinear'", all);
            StringAssert.Contains("PixelsPerUnit", all);
            StringAssert.Contains("pivot", all);
            StringAssert.Contains("Tint 'red'", all);
            StringAssert.Contains("listed twice", all);
            StringAssert.Contains("sheet 'nope'", all);
            StringAssert.Contains("Fps 0", all);
            StringAssert.Contains("frame 5", all);
        }

        [Test]
        public void SpineKind_IsReserved_ValidButNeverAVfxSheet()
        {
            ArtManifestData art = Valid();
            art.Sprites[0] = new ArtSpriteData { Name = "beast_x_spine", File = "beast_x.json", Kind = ArtSpriteKind.Spine, ArtKey = "beast/x" };

            Assert.IsEmpty(ArtManifestValidator.Validate(art));

            List<string> errors = new List<string>();
            VfxLibraryValidator.ValidateEffect(new VfxEffectData
            {
                Particles = new VfxParticleData { Sheet = "beast_x_spine", Count = 1, LifetimeMs = 100, Colors = new[] { "K" } }
            }, "t", art, errors);
            Assert.IsTrue(errors.Exists(e => e.Contains("spine")), string.Join("\n", errors));
        }

        [TestCase(0, 0)]
        [TestCase(124, 0)]
        [TestCase(125, 1)]
        [TestCase(250, 2)]
        [TestCase(375, 0)]
        public void LoopingClip_Wraps(int ms, int frame)
        {
            ArtAnimationData clip = new ArtAnimationData { Frames = new[] { 0, 1, 2 }, Fps = 8, Loop = true };
            Assert.AreEqual(frame, clip.FrameAt(ms));
        }

        [Test]
        public void OneShotClip_HoldsItsLastFrame()
        {
            ArtAnimationData clip = new ArtAnimationData { Frames = new[] { 3, 4 }, Fps = 10, Loop = false };
            Assert.AreEqual(3, clip.FrameAt(-50));
            Assert.AreEqual(4, clip.FrameAt(100));
            Assert.AreEqual(4, clip.FrameAt(10000));
        }

        private static ArtManifestData Valid()
        {
            ArtManifestData art = ArtManifestData.Normalize(FieldJson.FromJson<ArtManifestData>(V1));
            art.SchemaVersion = 2;
            List<ArtSpriteData> sprites = new List<ArtSpriteData>(art.Sprites)
            {
                new ArtSpriteData
                {
                    Name = "big", File = "big.png", Kind = ArtSpriteKind.Sprite, Category = "beast", FrameWidth = 512, FrameHeight = 512,
                    Frames = 1, PivotX = 256, PivotY = 470, PixelsPerUnit = 512, Filter = ArtFilter.Linear, Premultiplied = true
                }
            };
            art.Sprites = sprites.ToArray();
            Assert.IsEmpty(ArtManifestValidator.Validate(art), string.Join("\n", ArtManifestValidator.Validate(art)));
            return art;
        }
    }
}
