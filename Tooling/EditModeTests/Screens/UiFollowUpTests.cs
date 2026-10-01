using System;
using System.Collections.Generic;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Economy;
using BeastCraft.Expeditions;
using BeastCraft.Grove;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The #46 follow-ups: "new" markers over the schema-12 seen list, the toolkit's draggable (over a
    /// scroll view) and slider bar, free decor placement in the Glade, the HSV colour picker's arithmetic
    /// and storage, and the Grove-ready notification's time and setting.
    /// </summary>
    public class UiFollowUpTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GameSession NewSession(ManualGameClock clock = null)
        {
            return TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => 424242, clock ?? new ManualGameClock(T0, TimeSpan.FromHours(1000))));
        }

        // ------------------------------------------------------------------ "New" markers

        [Test]
        public void TheSchema12Migration_MarksEverythingOwnedAsSeen()
        {
            PlayerSave save = PlayerSave.CreateNew();
            string gear = save.Gear.AddBeastGear("any_gear");
            string avatarGear = save.Gear.AddAvatarGear("any_avatar_gear");
            save.Cosmetics.Unlock(CosmeticCollection.Key("hat", "crown"));
            SaveSerializer serializer = new SaveSerializer(new JsonSaveSerializer());
            string v11 = serializer.Serialize(save).Replace("\"SchemaVersion\":" + PlayerSave.CurrentSchemaVersion, "\"SchemaVersion\":11");
            v11 = System.Text.RegularExpressions.Regex.Replace(v11, ",\\s*\"Seen\":\\s*\\{[^}]*\\}", string.Empty);

            SaveLoadResult loaded = serializer.Deserialize(v11);

            Assert.IsTrue(loaded.Success, loaded.Error);
            Assert.IsTrue(loaded.Migrated);
            Assert.AreEqual(13, loaded.Save.SchemaVersion);
            Assert.IsFalse(SeenRules.IsNewGear(loaded.Save, gear));
            Assert.IsFalse(SeenRules.IsNewGear(loaded.Save, avatarGear));
            Assert.IsFalse(SeenRules.IsNewLook(loaded.Save, CosmeticCollection.Key("hat", "crown")), "an updated save shows nothing as new");

            string later = loaded.Save.Gear.AddBeastGear("any_gear");
            Assert.IsTrue(SeenRules.IsNewGear(loaded.Save, later), "what comes after the update is new");
            Assert.IsTrue(SeenRules.MarkGearSeen(loaded.Save, later));
            Assert.IsFalse(SeenRules.MarkGearSeen(loaded.Save, later), "each key once");
            Assert.IsFalse(SeenRules.IsNewGear(loaded.Save, later));
            Assert.IsFalse(SeenRules.IsNewGear(loaded.Save, "not_owned"), "only owned gear is new");
            Assert.IsFalse(SeenRules.IsNewLook(loaded.Save, CosmeticCollection.Key("hat", "never_unlocked")), "only unlocked looks are new");
        }

        [Test]
        public void InventoryGearRows_ShowNew_UntilSeen_AndTheTabKnows()
        {
            GameSession session = NewSession();
            GearSO piece = Content.Economy.Gear.BeastGearAssets.First(g => g.MinimumLevel <= 1);
            string instanceId = session.Save.Gear.AddBeastGear(piece.GearId);
            InventoryGearViewModel model = new InventoryGearViewModel(session);

            Assert.IsTrue(model.Gear.Single(g => g.InstanceId == instanceId).IsNew);
            Assert.IsTrue(model.AnyNew);
            int saves = session.AutosaveCount;

            model.MarkSeen(instanceId);
            Assert.IsFalse(model.AnyNew, "the tab's dot clears once the row was on screen");
            Assert.IsTrue(model.Gear.Single(g => g.InstanceId == instanceId).IsNew, "the row keeps its dot for this visit");
            model.SaveSeen();
            Assert.AreEqual(saves + 1, session.AutosaveCount);
            model.SaveSeen();
            Assert.AreEqual(saves + 1, session.AutosaveCount, "nothing new seen: no second save");

            model.Refresh();
            Assert.IsFalse(model.Gear.Single(g => g.InstanceId == instanceId).IsNew, "next visit: no dot");
        }

        [Test]
        public void WardrobeRows_ShowNewUnlockedLooks_AndTheirCategory()
        {
            GameSession session = NewSession();
            CosmeticCategory category = Content.Economy.Cosmetics.Categories.First(c => c != null && c.IsAvatar && !c.IsColor && c.Options.Count > 1);
            CosmeticOption option = category.Options.Last();
            session.Save.Cosmetics.Unlock(option.Key);
            AvatarWardrobeViewModel model = new AvatarWardrobeViewModel(session);

            WardrobeCategoryRow row = model.Categories.Single(c => c.CategoryId == category.CategoryId);
            Assert.IsTrue(row.Options.Single(o => o.Key == option.Key).IsNew);
            Assert.IsTrue(row.HasNew);
            Assert.IsTrue(model.AnyNew);

            model.MarkSeen(option.Key);
            model.SaveSeen();
            model.Refresh();
            row = model.Categories.Single(c => c.CategoryId == category.CategoryId);
            Assert.IsFalse(row.HasNew);
            Assert.IsFalse(model.AnyNew);
        }

        // ------------------------------------------------------------------ Draggable, slider

        [Test]
        public void ADraggable_InAScrollView_DragsInsteadOfScrolling_AndATapStaysATap()
        {
            UiRoot root = new UiRoot();
            ScrollView scroll = root.Add(new ScrollView { Bounds = new Rect(0f, 0f, 1080f, 800f), ContentHeight = 3000f });
            Draggable piece = scroll.Add(new Draggable { Bounds = new Rect(100f, 100f, 200f, 100f) });
            List<Vec2> moves = new List<Vec2>();
            int drops = 0;
            int taps = 0;
            piece.Moved += (dragged, delta) =>
            {
                moves.Add(delta);
                dragged.Bounds = new Rect(dragged.StartBounds.X + delta.X, dragged.StartBounds.Y + delta.Y, 200f, 100f);
            };
            piece.Dropped += _ => drops++;
            piece.Clicked += () => taps++;

            root.OnPointerDown(new Vec2(150f, 150f));
            root.OnPointerMove(new Vec2(150f, 300f));
            root.OnPointerMove(new Vec2(250f, 400f));
            Assert.IsNull(root.OnPointerUp(new Vec2(250f, 400f)), "a drag is not a click");

            Assert.AreEqual(0f, scroll.ScrollY, "the scroll view never moved");
            Assert.AreEqual(new Rect(200f, 350f, 200f, 100f), piece.Bounds);
            Assert.AreEqual(1, drops);
            Assert.AreEqual(0, taps);
            Assert.IsFalse(piece.Dragging);

            root.OnPointerDown(new Vec2(250f, 400f));
            root.OnPointerMove(new Vec2(253f, 402f));
            Assert.AreSame(piece, root.OnPointerUp(new Vec2(253f, 402f)), "a small wobble is still a tap");
            Assert.AreEqual(1, taps);
            Assert.AreEqual(1, drops);
        }

        [Test]
        public void ASliderBar_TakesTheValueUnderATapOrADrag()
        {
            UiRoot root = new UiRoot();
            SliderBar bar = root.Add(new SliderBar { Bounds = new Rect(100f, 500f, 800f, 70f) });
            List<float> values = new List<float>();
            bar.Changed += values.Add;

            root.Tap(new Vec2(300f, 530f));
            Assert.AreEqual(0.25f, bar.Value, 1e-5);
            root.OnPointerDown(new Vec2(300f, 530f));
            root.OnPointerMove(new Vec2(1200f, 530f));
            root.OnPointerUp(new Vec2(1200f, 530f));
            Assert.AreEqual(1f, bar.Value, "clamped to the bar");
            Assert.AreEqual(0f, bar.ValueAt(0f));
        }

        // ------------------------------------------------------------------ Decor

        [Test]
        public void MoveDecor_ClampsToTheCanvas_AndLoadingClampsToo()
        {
            GameSession session = NewSession();
            session.Save.Grove.HabitatsUnlocked.Add("mossy_glade");
            session.Save.Grove.UnlockedDecorIds.Add("firefly_lantern");
            GladeViewModel model = new GladeViewModel(session);
            model.SelectHabitat("mossy_glade");
            Assert.IsTrue(model.PlaceDecor("firefly_lantern").Success);
            DecorSlotRow placed = model.Slots.Single(s => s.DecorId == "firefly_lantern");
            Assert.AreEqual(0.5f, placed.Y, "a new piece starts halfway down");
            Assert.That(placed.X, Is.InRange(0f, 1f));

            int saves = session.AutosaveCount;
            Assert.IsTrue(model.MoveDecor("firefly_lantern", 0.25f, 1.7f));
            Assert.Greater(session.AutosaveCount, saves);
            placed = model.Slots.Single(s => s.DecorId == "firefly_lantern");
            Assert.AreEqual((0.25f, 1f), (placed.X, placed.Y));
            Assert.IsFalse(GroveRules.MoveDecor(session.Save, Content.GroveLibrary, "not_placed", 0f, 0f).Success);
            Assert.IsTrue(GroveRules.MoveDecor(session.Save, Content.GroveLibrary, "firefly_lantern", float.NaN, -3f).Success);
            Assert.AreEqual((0f, 0f), (session.Save.Grove.PlacedDecor[0].X, session.Save.Grove.PlacedDecor[0].Y));

            session.Save.Grove.PlacedDecor[0].X = 12f;
            session.Save.EnsureInitialized();
            Assert.AreEqual(1f, session.Save.Grove.PlacedDecor[0].X, "loading clamps an out-of-range position");
            Assert.AreEqual(0, session.Save.Grove.PlacedDecor[0].Rotation, "rotation stays 0");
        }

        // ------------------------------------------------------------------ Colour picker

        [Test]
        public void Hsv_RoundTripsThroughRgb_AndThePickerStoresAnOpaqueColour()
        {
            Assert.AreEqual("#FF0000", new Hsv(0f, 1f, 1f).ToHex());
            Assert.AreEqual("#00FF00", new Hsv(1f / 3f, 1f, 1f).ToHex());
            Assert.AreEqual("#0000FF", new Hsv(2f / 3f, 1f, 1f).ToHex());
            Assert.AreEqual("#808080", new Hsv(0.4f, 0f, 128f / 255f).ToHex(), "no saturation: a grey");
            Hsv back = Hsv.FromRgb(0.2f, 0.6f, 0.4f);
            back.ToRgb(out float r, out float g, out float b);
            Assert.AreEqual((0.2f, 0.6f, 0.4f), ((float)Math.Round(r, 4), (float)Math.Round(g, 4), (float)Math.Round(b, 4)));

            GameSession session = NewSession();
            AvatarWardrobeViewModel model = new AvatarWardrobeViewModel(session);
            WardrobeCategoryRow colour = model.Categories.First(c => c.IsColor);
            Assert.AreEqual(CosmeticResult.Set, model.SetColorHsv(colour.CategoryId, new Hsv(0f, 1f, 1f)));
            Assert.AreEqual("#FF0000", model.Categories.First(c => c.CategoryId == colour.CategoryId).WornColorHex);
            Hsv current = model.CurrentHsv(colour.CategoryId);
            Assert.AreEqual((0f, 1f, 1f), (current.H, current.S, current.V));
            Assert.IsTrue(CosmeticRules.AppearanceOf(session.Save, null).TryGetColor(colour.CategoryId, out BeastCraft.Color stored));
            Assert.AreEqual(1f, stored.a, "alpha is fixed at 1");
        }

        // ------------------------------------------------------------------ Grove notification

        [Test]
        public void GroveReadyUtc_IsTheSoonestPlotOrExpedition_StillOnItsWay()
        {
            ManualGameClock clock = new ManualGameClock(T0, TimeSpan.FromHours(1000));
            GameSession session = NewSession(clock);
            Assert.IsNull(session.GroveReadyUtc(), "nothing planted or away");

            GardenViewModel garden = new GardenViewModel(session);
            string seedId = garden.AvailableSeeds[0].SeedId;
            Assert.IsTrue(garden.Plant(garden.Plots[0].PlotId, seedId).Success);
            int growth = Math.Max(1, Content.GardenLibrary.Seed(seedId).GrowthHours);
            Assert.AreEqual(T0.AddHours(growth), session.GroveReadyUtc());

            DestinationData destination = Content.ExpeditionLibrary.Data.Destinations.OrderBy(d => d.DurationHours).First();
            clock.Advance(TimeSpan.FromMinutes(30));
            session.Save.Expeditions.Active.Add(new ActiveExpedition
            {
                DestinationId = destination.DestinationId,
                StartUtcTicks = clock.UtcNow.Ticks,
                StartMonotonicMs = (long)clock.Monotonic.TotalMilliseconds
            });
            DateTime back = clock.UtcNow.AddHours(destination.DurationHours);
            DateTime grown = T0.AddHours(growth);
            Assert.AreEqual(back < grown ? back : grown, session.GroveReadyUtc(), "the sooner of the two");

            clock.Advance(TimeSpan.FromHours(Math.Max(growth, destination.DurationHours) + 1));
            Assert.IsNull(session.GroveReadyUtc(), "what is ready already is not waited for");
        }

        [Test]
        public void TheGroveAlertSetting_DefaultsOff_AndShowsOnlyWhereNotificationsExist()
        {
            GameSession session = NewSession();
            Assert.IsFalse(session.Settings.GroveNotifications);
            Assert.IsNull(new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings).Row(SettingsViewModel.GroveNotifications));

            SettingsViewModel phone = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings, true);
            List<bool> changed = new List<bool>();
            phone.GroveNotificationsChanged += changed.Add;
            Assert.AreEqual("Off", phone.Row(SettingsViewModel.GroveNotifications).Value);
            phone.Change(SettingsViewModel.GroveNotifications);
            Assert.IsTrue(session.Settings.GroveNotifications);
            Assert.IsFalse(session.Settings.IdleNotifications, "separate from the idle alert");
            CollectionAssert.AreEqual(new[] { true }, changed);
        }
    }
}
