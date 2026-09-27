using System;
using System.Collections.Generic;
using System.IO;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Art;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The painted battle backdrops (<see cref="BattleArtData"/>, <c>battle-art.json</c>): the shipped
    /// data is valid and dresses every Verdant Hollow arena; a region or arena without a backdrop
    /// falls back (no entry: the pixel-tile board); the board rect maps onto the arena's tiles box
    /// exactly and independently of the image's resolution (<see cref="BackdropPlacement"/>); every
    /// camera view the rig can take stays inside the image, so the margins reach the canvas edges;
    /// the grid overlay draws each tile edge once (<see cref="HexGridLines"/>); and
    /// <see cref="BattleArtValidator"/>'s rules one by one.
    /// </summary>
    public class BattleArtTests
    {
        private const float Tolerance = 0.01f;

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [Test]
        public void ShippedBattleArt_IsValid_AndDressesEveryHollowArena()
        {
            string root = GameContent.FindRoot();
            BattleArtData data = FieldJson.FromJson<BattleArtData>(File.ReadAllText(GameContent.PathOf(root, BattleArtData.ProjectRelativePath)));
            List<string> errors = BattleArtValidator.Validate(data, Content.Regions, Content.Art);

            Assert.IsEmpty(errors, string.Join("\n", errors));
            Assert.IsEmpty(BattleArtValidator.ValidateLayouts(data, Content.Layouts), string.Join("\n", BattleArtValidator.ValidateLayouts(data, Content.Layouts)));
            foreach (ArenaSize arena in new[] { ArenaSize.Small, ArenaSize.Medium, ArenaSize.Large })
            {
                Assert.IsNotNull(Content.BattleArt.Backdrop("r01", arena), arena.ToString());
                foreach (string id in new[] { "sun0", "sun1", "sun3", "ruin0", "ruin2", "dusk2" })
                {
                    string key = "backdrop/r01/" + id + "/" + arena.ToString().ToLowerInvariant();
                    BattleBackdropData backdrop = Content.BattleArt.BackdropByArtKey(key);
                    Assert.IsNotNull(backdrop, key);
                    Assert.AreEqual(arena.ToString(), backdrop.Arena);
                    ArtSpriteData image = Content.Art.FindByArtKey(backdrop.ArtKey);
                    Assert.IsNotNull(image, backdrop.ArtKey);
                    Assert.AreEqual(ArtFilter.Linear, image.Filter, "painted art is linear-filtered (and mipmapped)");
                    Assert.AreEqual("backdrop", image.Category, "backdrops load on first use (SpriteAtlas.DeferredCategory)");
                    Assert.IsNotNull(Array.Find(Content.Layouts.Layouts, l => l.ArtKey == key), key + " has its obstacle layout");
                }
            }

            Assert.AreEqual(18, Content.BattleArt.Backdrops.Length, "six Hollow paintings, each on the three arenas");
        }

        [Test]
        public void Backdrops_AndLayouts_AreHeldToEachOther()
        {
            BattleArtData art = new BattleArtData
            {
                Backdrops = new[]
                {
                    new BattleBackdropData { RegionId = "r01", Arena = "Small", ArtKey = "backdrop/r01/a/small" },
                    new BattleBackdropData { RegionId = "r01", Arena = "Small", ArtKey = "backdrop/r01/b/small" },
                    new BattleBackdropData { RegionId = "r02", Arena = "Small", ArtKey = "backdrop/r02/a/small" }
                }
            };
            BattleLayoutData layouts = new BattleLayoutData
            {
                Layouts = new[]
                {
                    new BattleLayoutEntryData { RegionId = "r01", Arena = "Small", ArtKey = "backdrop/r01/a/small" },
                    new BattleLayoutEntryData { RegionId = "r01", Arena = "Medium", ArtKey = "backdrop/r01/b/small" },
                    new BattleLayoutEntryData { RegionId = "r01", Arena = "Large", ArtKey = "backdrop/r01/c/large" }
                }
            };

            string text = string.Join("\n", BattleArtValidator.ValidateLayouts(art, layouts));
            StringAssert.Contains("Layout 'backdrop/r01/b/small': its backdrop is r01 Small, the layout r01 Medium", text);
            StringAssert.Contains("Layout 'backdrop/r01/c/large': no backdrop has that ArtKey", text);
            Assert.IsFalse(text.Contains("backdrop/r02/a/small"), "a region without layouts may have backdrops without them");
            Assert.AreEqual(art.Backdrops[1], art.BackdropByArtKey("backdrop/r01/b/small"));
            Assert.IsNull(art.BackdropByArtKey("backdrop/r01/z/small"));

            layouts.Layouts = new[] { layouts.Layouts[0] };
            StringAssert.Contains("Backdrop 'backdrop/r01/b/small': region r01 has battle layouts, but none for this painting",
                                  string.Join("\n", BattleArtValidator.ValidateLayouts(art, layouts)));
        }

        [Test]
        public void NoBackdrop_FallsBackToThePixelBoard()
        {
            Assert.IsNull(Content.BattleArt.Backdrop("r02", ArenaSize.Medium), "a region without a backdrop draws the pixel tiles");
            Assert.IsNull(Content.BattleArt.Backdrop(null, ArenaSize.Medium));
            Assert.IsNull(Content.BattleArt.Backdrop(string.Empty, ArenaSize.Small));

            BattleArtData onlyMedium = new BattleArtData
            {
                SchemaVersion = 1,
                Backdrops = new[] { Backdrop("r01", "Medium", 0.1f, 0.2f, 0.8f, 0.5f) }
            };
            Assert.IsNotNull(onlyMedium.Backdrop("r01", ArenaSize.Medium));
            Assert.IsNull(onlyMedium.Backdrop("r01", ArenaSize.Large), "an arena size without one falls back too");
            Assert.IsNull(new BattleArtData { Backdrops = null }.Backdrop("r01", ArenaSize.Medium));
        }

        [TestCase(ArenaSize.Small)]
        [TestCase(ArenaSize.Medium)]
        [TestCase(ArenaSize.Large)]
        public void BoardRect_LandsExactlyOnTheTilesBox(ArenaSize arena)
        {
            HexGrid.DimensionsFor(arena, out int width, out int height);
            BattleBackdropData backdrop = Backdrop("r01", arena.ToString(), 0.125f, 0.25f, 0.75f, 0.5f);
            Rect tiles = HexLayout.BoardBounds(width, height);

            foreach ((int w, int h) in new[] { (720, 1280), (1440, 2560), (100, 100) })
            {
                Vec2 topLeft = BackdropPlacement.ImageToBoard(backdrop, width, height, w, h, 0.125f * w, 0.25f * h);
                Vec2 bottomRight = BackdropPlacement.ImageToBoard(backdrop, width, height, w, h, 0.875f * w, 0.75f * h);
                Assert.AreEqual(tiles.X, topLeft.X, Tolerance, w + "x" + h);
                Assert.AreEqual(tiles.Y, topLeft.Y, Tolerance);
                Assert.AreEqual(tiles.Right, bottomRight.X, Tolerance);
                Assert.AreEqual(tiles.Bottom, bottomRight.Y, Tolerance);
            }

            // The whole image: the tiles box over three quarters of its width, half its height.
            Rect image = BackdropPlacement.ImageRect(backdrop, width, height);
            Assert.AreEqual(tiles.Width / 0.75f, image.Width, Tolerance);
            Assert.AreEqual(tiles.Height / 0.5f, image.Height, Tolerance);
            Assert.AreEqual(tiles.X - 0.125f * image.Width, image.X, Tolerance);
            Assert.AreEqual(tiles.Y - 0.25f * image.Height, image.Y, Tolerance);
        }

        [Test]
        public void ImageRect_WorkedExample_AndDegenerateRect()
        {
            // Small: tiles box (-80, -99, 176x198). A rect of (0.25, 0.25, 0.5x0.5) doubles it around itself.
            BattleBackdropData backdrop = Backdrop("r01", "Small", 0.25f, 0.25f, 0.5f, 0.5f);
            Rect image = BackdropPlacement.ImageRect(backdrop, 5, 7);
            Assert.AreEqual(-168f, image.X, Tolerance);
            Assert.AreEqual(-198f, image.Y, Tolerance);
            Assert.AreEqual(352f, image.Width, Tolerance);
            Assert.AreEqual(396f, image.Height, Tolerance);

            Assert.AreEqual(1f, BackdropPlacement.PixelAspect(backdrop, 5, 7, 176, 198), 0.0001f, "a 176x198 image keeps square pixels");
            Assert.AreEqual(2f, BackdropPlacement.PixelAspect(backdrop, 5, 7, 88, 198), 0.0001f, "half as wide: stretched twice across");

            Rect tiles = HexLayout.BoardBounds(5, 7);
            Rect fallback = BackdropPlacement.ImageRect(Backdrop("r01", "Small", 0f, 0f, 0f, 0f), 5, 7);
            Assert.AreEqual(tiles.X, fallback.X);
            Assert.AreEqual(tiles.Width, fallback.Width);
        }

        [TestCase(ArenaSize.Small)]
        [TestCase(ArenaSize.Medium)]
        [TestCase(ArenaSize.Large)]
        public void EveryCameraView_ShowsOnlyBackdrop_ToTheCanvasEdges(ArenaSize arena)
        {
            PortraitLayout screen = new PortraitLayout();
            HexGrid.DimensionsFor(arena, out int width, out int height);
            BattleBackdropData backdrop = Content.BattleArt.Backdrop("r01", arena);
            Rect image = BackdropPlacement.ImageRect(backdrop, width, height);
            CameraRig rig = new CameraRig(width, height, screen.Board);
            Rect bounds = rig.Bounds;

            // Fit-all is the fixed board fit: the tiles land where the portrait layout puts them.
            Rect canvas = BackdropPlacement.CanvasAtFitAll(width, height, screen);
            Assert.IsTrue(BackdropPlacement.Covers(image, canvas), arena + ": " + image + " vs " + canvas);
            Assert.AreEqual((float)PortraitLayout.CanvasWidth / PortraitLayout.CanvasHeight, canvas.Width / canvas.Height, 0.001f);

            // Every framing the camera can take: the corners and centre at every zoom, clamped as the rig clamps.
            foreach (float zoom in new[] { 1f, 1.5f, rig.MaxZoomFor })
            {
                foreach (Vec2 at in new[]
                         {
                             new Vec2(bounds.X, bounds.Y), new Vec2(bounds.Right, bounds.Y), new Vec2(bounds.X, bounds.Bottom), new Vec2(bounds.Right, bounds.Bottom),
                             bounds.Center
                         })
                {
                    BoardFit fit = rig.Fit(rig.Clamp(new CameraView(at, zoom)));
                    Vec2 topLeft = fit.ToBoard(0f, 0f);
                    Vec2 bottomRight = fit.ToBoard(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
                    Rect seen = new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
                    Assert.IsTrue(BackdropPlacement.Covers(image, seen), arena + " zoom " + zoom + " at " + at + ": " + seen + " outside " + image);
                }
            }
        }

        [Test]
        public void GridLines_DrawEveryEdgeOnce()
        {
            HexLayout layout = new HexLayout(0, 0);
            Assert.AreEqual(6, HexGridLines.Of(new[] { HexCoordinate.Zero }, layout).Count);
            Assert.AreEqual(11, HexGridLines.Of(new[] { HexCoordinate.Zero, new HexCoordinate(1, 0) }, layout).Count, "a shared edge once");
            Assert.AreEqual(11, HexGridLines.Of(new[] { HexCoordinate.Zero, new HexCoordinate(0, 1) }, layout).Count, "diagonal neighbours too");

            foreach (ArenaSize arena in new[] { ArenaSize.Small, ArenaSize.Medium, ArenaSize.Large })
            {
                HexGrid grid = new HexGrid(arena);
                HashSet<HexCoordinate> tiles = new HashSet<HexCoordinate>(grid.Tiles);
                int pairs = 0;
                foreach (HexCoordinate tile in tiles)
                {
                    foreach (HexCoordinate next in new[] { new HexCoordinate(tile.Q + 1, tile.R), new HexCoordinate(tile.Q, tile.R + 1), new HexCoordinate(tile.Q - 1, tile.R + 1) })
                    {
                        if (tiles.Contains(next))
                        {
                            pairs++;
                        }
                    }
                }

                List<Segment> segments = HexGridLines.Of(grid.Tiles, layout);
                Assert.AreEqual(6 * tiles.Count - pairs, segments.Count, arena.ToString());
                foreach (Segment segment in segments)
                {
                    Assert.That(segment.Length, Is.InRange(17.9f, 18.4f), "every edge a hex side (18 or sqrt(16^2 + 9^2))");
                }
            }
        }

        [Test]
        public void GridLines_CornersMeetTheNeighboursCorners()
        {
            HexLayout layout = new HexLayout(0, 0);
            Vec2[] a = HexGridLines.Corners(layout, HexCoordinate.Zero);
            Vec2[] right = HexGridLines.Corners(layout, new HexCoordinate(1, 0));
            Vec2[] below = HexGridLines.Corners(layout, new HexCoordinate(0, 1));
            Assert.AreEqual(a[1], right[5]);
            Assert.AreEqual(a[2], right[4]);
            Assert.AreEqual(a[2], below[0]);
            Assert.AreEqual(a[3], below[5]);
        }

        [Test]
        public void Validator_ChecksEveryRule()
        {
            RegionLibraryData regions = Content.Regions;
            ArtManifestData art = Content.Art;
            BattleBackdropData good = Content.BattleArt.Backdrop("r01", ArenaSize.Medium);
            BattleBackdropData Copy(string region, string arenaName, string key, BattleArtRect rect)
            {
                return new BattleBackdropData { RegionId = region, Arena = arenaName, ArtKey = key, BoardRect = rect };
            }

            List<string> Errors(params BattleBackdropData[] backdrops)
            {
                return BattleArtValidator.Validate(new BattleArtData { SchemaVersion = 1, Backdrops = backdrops }, regions, art);
            }

            Assert.IsEmpty(Errors(good));
            StringAssert.Contains("SchemaVersion", string.Join("\n", BattleArtValidator.Validate(new BattleArtData { SchemaVersion = 7 }, regions, art)));
            StringAssert.Contains("not a region", string.Join("\n", Errors(Copy("r99", "Medium", good.ArtKey, good.BoardRect))));
            StringAssert.Contains("not Small, Medium or Large", string.Join("\n", Errors(Copy("r01", "medium", good.ArtKey, good.BoardRect))));
            StringAssert.Contains("listed twice", string.Join("\n", Errors(good, good)));
            StringAssert.Contains("not lowercase", string.Join("\n", Errors(Copy("r01", "Medium", "Backdrop/R01", good.BoardRect))));
            StringAssert.Contains("not a sprite in the art manifest", string.Join("\n", Errors(Copy("r01", "Medium", "backdrop/r01/huge", good.BoardRect))));
            StringAssert.Contains("inside the image", string.Join("\n", Errors(Copy("r01", "Medium", good.ArtKey, Rect(0.5f, 0.2f, 0.7f, 0.4f)))));
            StringAssert.Contains("inside the image", string.Join("\n", Errors(Copy("r01", "Medium", good.ArtKey, Rect(0.1f, 0.2f, 0f, 0.4f)))));
            StringAssert.Contains("no BoardRect", string.Join("\n", Errors(Copy("r01", "Medium", good.ArtKey, null))));
            StringAssert.Contains("stretched",
                                  string.Join("\n", Errors(Copy("r01", "Medium", good.ArtKey, Rect(good.BoardRect.X, good.BoardRect.Y, good.BoardRect.Width * 0.9f, good.BoardRect.Height)))));
            StringAssert.Contains("canvas edges", string.Join("\n", Errors(Copy("r01", "Medium", good.ArtKey, Rect(0f, 0f, 1f, 1f)))), "a board rect over the whole image leaves no margin");

            BattleArtData overlay = new BattleArtData
            {
                SchemaVersion = 1,
                Board = new BoardOverlayData { GridColor = "#ffffff", PlayerZoneColor = "~", GridAlpha = 1.5f, GridWidth = 0.1f, EnemyZoneAlpha = -0.1f, HudScrimAlpha = 2f }
            };
            string text = string.Join("\n", BattleArtValidator.Validate(overlay, regions, art));
            StringAssert.Contains("GridColor", text);
            StringAssert.Contains("PlayerZoneColor", text);
            StringAssert.Contains("GridAlpha", text);
            StringAssert.Contains("GridWidth", text);
            StringAssert.Contains("EnemyZoneAlpha", text);
            StringAssert.Contains("HudScrimAlpha", text);
            Assert.IsNotEmpty(BattleArtValidator.Validate(null, regions, art));
        }

        [Test]
        public void SkillIcons_ShippedStyle_FramesEveryIcon_WithPaintableSprites()
        {
            SkillIconStyleData style = Content.BattleArt.SkillIcons;
            Assert.IsNotNull(style);
            ArtSpriteData frame = Content.Art.FindByArtKey(style.Frame);
            Assert.IsNotNull(frame, style.Frame);
            Assert.AreEqual(ArtFilter.Linear, frame.Filter, "painted UI is linear-filtered and mipmapped");
            Assert.AreEqual("ui", frame.Category);
            Assert.That(style.IconScale, Is.LessThan(style.FrameScale), "the frame's rim covers the icon's edge");
            Assert.That(style.FrameScale, Is.LessThanOrEqualTo(style.RingScale), "the ring shows around the frame");

            CollectionAssert.AreEqual(new[] { "common", "rare", "epic", "legendary", "gloam" }, Array.ConvertAll(style.Rarities, r => r.Rarity));
            foreach (SkillRarityData rarity in style.Rarities)
            {
                ArtSpriteData ring = Content.Art.FindByArtKey(rarity.Ring);
                Assert.IsNotNull(ring, rarity.Ring);
                Assert.AreEqual(ArtFilter.Linear, ring.Filter);
                Assert.AreEqual(frame.FrameWidth, ring.FrameWidth, "frame and rings share one square canvas");
                Assert.AreEqual(frame.FrameHeight, ring.FrameHeight);
            }
        }

        [Test]
        public void SkillIcons_Rarity_BySource_UnlessOverridden()
        {
            SkillIconStyleData style = Content.BattleArt.SkillIcons;
            Assert.AreEqual("common", style.RarityFor("skill/ember_shot", SkillIconSource.Beast).Rarity);
            Assert.AreEqual("rare", style.RarityFor("skill/ember_shot", SkillIconSource.Avatar).Rarity);
            Assert.AreEqual("gloam", style.RarityFor("skill/enemy/giant/quake", SkillIconSource.Enemy).Rarity);

            SkillIconStyleData custom = new SkillIconStyleData
            {
                Rarities = style.Rarities,
                BeastRarity = "common",
                Overrides = new[] { new SkillRarityOverrideData { ArtKey = "skill/rebirth_flame", Rarity = "legendary" } }
            };
            Assert.AreEqual("legendary", custom.RarityFor("skill/rebirth_flame", SkillIconSource.Beast).Rarity, "an override wins");
            Assert.AreEqual("common", custom.RarityFor("skill/ember_shot", SkillIconSource.Beast).Rarity);
            Assert.IsNull(custom.RarityFor("skill/ember_shot", SkillIconSource.Enemy), "no enemy default: no ring");
            Assert.IsNull(custom.Rarity("mythic"));
        }

        [Test]
        public void SkillIcons_Validator_ChecksEveryRule()
        {
            SkillRarityData common = new SkillRarityData { Rarity = "common", Ring = "ui/skill_icon/ring/common" };
            SkillIconStyleData style = new SkillIconStyleData
            {
                Frame = "ui/skill_icon/missing",
                FrameScale = 2f,
                IconScale = 0.1f,
                Rarities = new[] { common, common, new SkillRarityData { Rarity = "Rare", Ring = "ui/nope", Tint = "~" } },
                BeastRarity = "common",
                EnemyRarity = "mythic",
                Overrides = new[]
                {
                    new SkillRarityOverrideData { ArtKey = "skill/ember_shot", Rarity = "epic" }, new SkillRarityOverrideData { ArtKey = "skill/ember_shot", Rarity = "common" },
                    new SkillRarityOverrideData { ArtKey = "skill/nothing_here", Rarity = "common" }
                }
            };
            string text = string.Join("\n", BattleArtValidator.Validate(new BattleArtData { SchemaVersion = 1, SkillIcons = style }, Content.Regions, Content.Art));

            StringAssert.Contains("SkillIcons Frame: ArtKey 'ui/skill_icon/missing' is not a sprite", text);
            StringAssert.Contains("FrameScale", text);
            StringAssert.Contains("IconScale", text);
            StringAssert.Contains("rarity 'common' is listed twice", text);
            StringAssert.Contains("rarity 'Rare': Rarity must be lowercase", text);
            StringAssert.Contains("rarity 'Rare' Ring: ArtKey 'ui/nope'", text);
            StringAssert.Contains("rarity 'Rare' Tint", text);
            StringAssert.Contains("EnemyRarity 'mythic' is not a rarity", text);
            StringAssert.Contains("override 'skill/ember_shot': Rarity 'epic' is not a rarity", text);
            StringAssert.Contains("override 'skill/ember_shot' is listed twice", text);
            StringAssert.Contains("override 'skill/nothing_here': no skill icon", text);
            Assert.IsFalse(text.Contains("BeastRarity"), text);
            Assert.IsEmpty(BattleArtValidator.Validate(new BattleArtData { SchemaVersion = 1, SkillIcons = Content.BattleArt.SkillIcons }, Content.Regions, Content.Art));
        }

        [Test]
        public void ArtKeys_AreUniqueInTheManifest_SoAPaintedIconReplacesItsPlaceholder()
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ArtSpriteData sprite in Content.Art.Sprites)
            {
                if (!string.IsNullOrEmpty(sprite.ArtKey))
                {
                    Assert.IsTrue(keys.Add(sprite.ArtKey), sprite.ArtKey + " is listed twice");
                }
            }
        }

        [Test]
        public void ArtSlotChecklist_ListsEveryPaintedSlot_AndEveryListedPlaceholderExists()
        {
            string content = GameContent.FindRoot();
            string checklist = File.ReadAllText(Path.Combine(Path.GetDirectoryName(content), "docs", "art", "hollow-art-slots.md"));
            HashSet<string> listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(checklist, @"`content/(art/[a-z0-9_/]+\.png)`"))
            {
                listed.Add(match.Groups[1].Value);
            }

            int painted = 0;
            foreach (ArtSpriteData sprite in Content.Art.Sprites)
            {
                string file = ArtManifestData.ResolveFile(sprite.File);
                if (file.StartsWith("art/backdrops/", StringComparison.Ordinal) || file.StartsWith("art/ui/", StringComparison.Ordinal) ||
                    file.StartsWith("art/vfx/", StringComparison.Ordinal))
                {
                    Assert.IsTrue(listed.Contains(file), file + " (" + sprite.Name + ") is a painted slot missing from docs/art/hollow-art-slots.md");
                    painted++;
                }
            }

            Assert.AreEqual(18 + 6 + 59, painted, "backdrops, icon frame and rings, VFX hero frames");
            int icons = 0;
            foreach (string file in listed)
            {
                if (file.StartsWith("art/icons/skills/", StringComparison.Ordinal))
                {
                    icons++;
                    continue;
                }

                Assert.IsTrue(File.Exists(Path.Combine(content, file.Replace('/', Path.DirectorySeparatorChar))), file + " is listed but has no placeholder");
            }

            Assert.AreEqual(60 + 6 + 10 + 15, icons, "a slot per beast skill, avatar active, avatar passive and enemy skill");
        }

        private static BattleBackdropData Backdrop(string region, string arena, float x, float y, float width, float height)
        {
            return new BattleBackdropData { RegionId = region, Arena = arena, ArtKey = "backdrop/" + region + "/" + arena.ToLowerInvariant(), BoardRect = Rect(x, y, width, height) };
        }

        private static BattleArtRect Rect(float x, float y, float width, float height)
        {
            return new BattleArtRect { X = x, Y = y, Width = width, Height = height };
        }
    }
}
