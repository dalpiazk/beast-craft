using System.Collections.Generic;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The art manifest's schema v2 (<see cref="ArtManifestData"/>): the shipped manifest (pixel
    /// placeholders plus the ten illustrated beasts), file paths resolved against the manifest's
    /// folder, reading a v1 manifest, <see cref="ArtManifestValidator"/>'s rules, the reserved
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
        public void ShippedManifest_IsV2_Valid_PixelPlaceholders_PlusIllustratedBeasts()
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;

            Assert.AreEqual(ArtManifestData.CurrentSchemaVersion, art.SchemaVersion);
            Assert.IsEmpty(ArtManifestValidator.Validate(art), string.Join("\n", ArtManifestValidator.Validate(art)));
            foreach (ArtSpriteData sprite in art.Sprites)
            {
                Assert.AreEqual(ArtSpriteKind.Sprite, sprite.Kind, sprite.Name);
                Assert.IsTrue(!sprite.Premultiplied || sprite.Filter == ArtFilter.Linear,
                              sprite.Name + ": pixel PNGs are straight alpha, premultiplied on load (a painted slot may ship premultiplied)");
                Assert.IsFalse(string.IsNullOrEmpty(sprite.Category), sprite.Name);
                if (sprite.Filter == ArtFilter.Linear)
                {
                    // Illustrated characters, and the painted slots (docs/art/hollow-art-slots.md), each in its own folder.
                    string folder = sprite.File.Split('/')[1];
                    Dictionary<string, string[]> categories = new Dictionary<string, string[]>
                    {
                        { "beasts", new[] { "beast" } },
                        { "enemies", new[] { "enemy", "accent" } },
                        { "backdrops", new[] { "backdrop" } },
                        { "ui", new[] { "ui", "ui_kit" } },
                        { "icons", new[] { "skill" } },
                        { "vfx", new[] { "fx" } }
                    };
                    Assert.IsTrue(sprite.File.StartsWith("../", System.StringComparison.Ordinal) && categories.ContainsKey(folder), sprite.Name + ": " + sprite.File);
                    CollectionAssert.Contains(categories[folder], sprite.Category, sprite.Name);
                    continue;
                }

                Assert.AreEqual(ArtFilter.Point, sprite.Filter, sprite.Name);
                Assert.AreEqual(ArtManifestData.ReferencePixelsPerUnit, sprite.PixelsPerUnit, sprite.Name);
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

        [TestCase("phoenix")]
        [TestCase("leviathan")]
        [TestCase("golem")]
        [TestCase("griffin")]
        [TestCase("thunderbird")]
        [TestCase("frost_wyrm")]
        [TestCase("treant")]
        [TestCase("tarasque")]
        [TestCase("kirin")]
        [TestCase("basilisk")]
        public void EveryBeast_DrawsItsIllustratedSprite_LinearFeetPivot_AboutAHexTall(string species)
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;
            string key = VfxLibraryTests.Content.Battle.GetSpecies(species).ArtKey;
            ArtSpriteData sprite = art.FindByArtKey(key);

            Assert.AreEqual("beast/" + species + "/illustrated", key);
            Assert.IsNotNull(sprite, key);
            Assert.AreEqual(ArtFilter.Linear, sprite.Filter);
            Assert.AreEqual("../beasts/" + species + "/" + species + ".png", sprite.File);
            Assert.AreEqual("art/beasts/" + species + "/" + species + ".png", ArtManifestData.ResolveFile(sprite.File));
            Assert.AreEqual(sprite.FrameWidth / 2f, sprite.PivotX, "the feet are the frame's horizontal centre");
            Assert.Greater(sprite.PivotY, sprite.FrameHeight * 0.9f, "the feet are at the bottom of the frame");
            Assert.GreaterOrEqual(sprite.FrameHeight, 512, "about 512 px tall: twice its largest on-screen size");
            float height = HeightUnits(sprite);
            // World units from the feet to the top of the square frame: a tall beast fills it, so this is
            // about its height; the low, long basilisk fills it only in width (its art is ~0.65 tall).
            Assert.That(height, Is.InRange(0.75f, 1.75f), "world units from the feet to the top of the frame");
            Assert.IsNotNull(art.FindByArtKey("beast/" + species), "the pixel placeholder stays in the manifest");
        }

        [TestCase("giant", 2.37f)]
        [TestCase("champion", 1.7f)]
        [TestCase("shaman", 1.32f)]
        [TestCase("caster", 1.25f)]
        [TestCase("stalker", 1.03f)]
        [TestCase("archer", 0.94f)]
        [TestCase("brute", 0.88f)]
        [TestCase("stingling", 0.73f)]
        [TestCase("swarmling", 0.39f)]
        public void EveryEnemy_DrawsItsHollowSprite_WithAnAccentOverlayOfItsSize(string enemy, float worldHeight)
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;
            string key = VfxLibraryTests.Content.Enemies.Get(enemy).ArtKey;
            ArtSpriteData sprite = art.FindByArtKey(key);

            Assert.AreEqual("enemy/" + enemy + "/hollow", key);
            Assert.IsNotNull(sprite, key);
            Assert.AreEqual(ArtFilter.Linear, sprite.Filter);
            Assert.AreEqual("../enemies/" + enemy + "/" + enemy + "_hollow.png", sprite.File);
            Assert.AreEqual(sprite.FrameWidth / 2f, sprite.PivotX, "the feet are the frame's horizontal centre");
            Assert.AreEqual(sprite.FrameHeight - 4f, sprite.PivotY, "the feet are at the bottom of the frame");

            ArtSpriteData accent = art.Find(sprite.Accent);
            Assert.IsNotNull(accent, sprite.Accent);
            Assert.AreEqual("accent", accent.Category);
            Assert.AreEqual("../enemies/" + enemy + "/" + enemy + "_hollow_accent.png", accent.File);
            Assert.AreEqual(sprite.FrameWidth, accent.FrameWidth);
            Assert.AreEqual(sprite.FrameHeight, accent.FrameHeight);
            Assert.AreEqual(sprite.PixelsPerUnit, accent.PixelsPerUnit);
            Assert.AreEqual(sprite.Frames, accent.Frames, "the overlay draws the base's frame, so it has as many");
            Assert.AreEqual("Nature", sprite.AccentElement, "the Verdant Hollow art is drawn in Nature");
            Assert.IsTrue(ArtManifestValidator.IsHexColor(sprite.AccentNative), sprite.AccentNative);
            Assert.IsNotNull(art.FindByArtKey("enemy/" + enemy), "the pixel placeholder stays in the manifest");

            // WorldHeight (illustrated.json, the lineup's relative sizes) times PixelsPerUnit is the art's
            // height in source px: it fills most of the 504 px content height or width, inside the frame.
            Assert.That(worldHeight * sprite.PixelsPerUnit, Is.InRange(sprite.PivotY * 0.6f, sprite.PivotY), "the art fits its frame");
        }

        [Test]
        public void AccentTint_ComesFromData_TheArtsOwnColourForItsElement()
        {
            ArtManifestData art = VfxLibraryTests.Content.Art;
            ArtSpriteData brute = art.FindByArtKey("enemy/brute/hollow");

            Assert.AreEqual(brute.AccentNative, art.AccentTint(brute, "Nature"), "Nature draws the approved art as is");
            Assert.AreEqual(brute.AccentNative, art.AccentTint(brute, "None"));
            Assert.AreEqual(brute.AccentNative, art.AccentTint(brute, null));
            Assert.AreEqual(art.Palette[art.ElementAccents["Fire"]], art.AccentTint(brute, "Fire"));
            Assert.AreEqual(art.Palette[art.ElementAccents["Dark"]], art.AccentTint(brute, "Dark"));
            Assert.AreEqual(10, art.ElementAccents.Count, "every element but None has an accent colour");
            Assert.IsNull(art.AccentTint(art.FindByArtKey("beast/golem/illustrated"), "Fire"), "a beast has no accent overlay");

            // The colour is data: change the palette char and the tint follows.
            ArtManifestData edited = Valid();
            edited.Palette["o"] = "#ff0000";
            edited.Palette["q"] = "#00ff00";
            edited.ElementAccents = new Dictionary<string, string> { { "Fire", "o" } };
            ArtSpriteData big = edited.Find("big");
            big.Accent = "big_accent";
            big.AccentElement = "Nature";
            big.AccentNative = "#123456";
            Assert.AreEqual("#ff0000", edited.AccentTint(big, "Fire"));
            edited.ElementAccents["Fire"] = "q";
            Assert.AreEqual("#00ff00", edited.AccentTint(big, "Fire"));
            Assert.AreEqual("#123456", edited.AccentTint(big, "Water"), "an element the data does not colour keeps the art's own");
        }

        [Test]
        public void Validator_RequiresTheAccentOverlay_ToMatchItsBase()
        {
            ArtManifestData art = WithAccent();
            Assert.IsEmpty(ArtManifestValidator.Validate(art), string.Join("\n", ArtManifestValidator.Validate(art)));

            art.Find("big_accent").FrameWidth = 256;
            List<string> errors = ArtManifestValidator.Validate(art);
            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("must match its base's size", errors[0]);

            art = WithAccent();
            art.Find("big_accent").Frames = 2;
            StringAssert.Contains("must match its base's size", string.Join("\n", ArtManifestValidator.Validate(art)), "the frame counts must match too");

            art = WithAccent();
            art.Find("big_accent").PivotY = 400;
            StringAssert.Contains("pivot", string.Join("\n", ArtManifestValidator.Validate(art)));

            art = WithAccent();
            art.Find("big").Accent = "nope";
            art.Find("big").AccentElement = "Moss";
            art.Find("big").AccentNative = "green";
            art.ElementAccents = new Dictionary<string, string> { { "None", "K" }, { "Fire", "?" } };
            string all = string.Join("\n", ArtManifestValidator.Validate(art));
            StringAssert.Contains("not another sprite", all);
            StringAssert.Contains("AccentElement 'Moss'", all);
            StringAssert.Contains("AccentNative 'green'", all);
            StringAssert.Contains("ElementAccents 'None': not an element", all);
            StringAssert.Contains("ElementAccents 'Fire': '?' is not a palette char", all);
        }

        [TestCase("beast_a.png", "art/pixel/beast_a.png")]
        [TestCase("../beasts/golem/golem.png", "art/beasts/golem/golem.png")]
        [TestCase("./sub/../x.png", "art/pixel/x.png")]
        [TestCase("../../fonts/OFL.txt", "fonts/OFL.txt")]
        [TestCase("../../../outside.png", null)]
        [TestCase("/rooted.png", null)]
        [TestCase("..\\beasts\\golem.png", null)]
        [TestCase("C:/x.png", null)]
        [TestCase("", null)]
        public void ResolveFile_IsRelativeToTheManifestFolder_AndStaysInTheContentRoot(string file, string expected)
        {
            Assert.AreEqual(expected, ArtManifestData.ResolveFile(file));
        }

        [Test]
        public void Validator_RefusesAFileOutsideTheContentRoot()
        {
            ArtManifestData art = Valid();
            art.Sprites[0].File = "../../../beast_a.png";

            List<string> errors = ArtManifestValidator.Validate(art);

            Assert.AreEqual(1, errors.Count, string.Join("\n", errors));
            StringAssert.Contains("inside the content root", errors[0]);
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

        /// <summary>World units from the pivot (feet) to the frame's top.</summary>
        private static float HeightUnits(ArtSpriteData sprite)
        {
            return sprite.PivotY / sprite.PixelsPerUnit;
        }

        /// <summary><see cref="Valid"/> plus an accent overlay for its <c>big</c> sprite, and an element palette.</summary>
        private static ArtManifestData WithAccent()
        {
            ArtManifestData art = Valid();
            ArtSpriteData big = art.Find("big");
            big.Accent = "big_accent";
            big.AccentElement = "Nature";
            big.AccentNative = "#8fb85a";
            ArtSpriteData accent = new ArtSpriteData
            {
                Name = "big_accent",
                File = "big_accent.png",
                Kind = ArtSpriteKind.Sprite,
                Category = "accent",
                FrameWidth = big.FrameWidth,
                FrameHeight = big.FrameHeight,
                Frames = 1,
                PivotX = big.PivotX,
                PivotY = big.PivotY,
                PixelsPerUnit = big.PixelsPerUnit,
                Filter = ArtFilter.Linear
            };
            art.Sprites = new List<ArtSpriteData>(art.Sprites) { accent }.ToArray();
            art.ElementAccents = new Dictionary<string, string> { { "Fire", "K" } };
            return art;
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
