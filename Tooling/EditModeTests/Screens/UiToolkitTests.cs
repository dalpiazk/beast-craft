using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The retained UI toolkit's engine-neutral half: hit-testing in canvas pixels (nested, clipped,
    /// scrolled), taps and drags through <see cref="UiRoot"/> (a drag in a scroll view cancels the
    /// tap), scroll inertia and its edges, tabs, toasts, and the data-driven house style.
    /// </summary>
    public class UiToolkitTests
    {
        private static Vec2 P(float x, float y)
        {
            return new Vec2(x, y);
        }

        [Test]
        public void HitTest_FindsTheTopmostDeepestWidget_AndMissesOutside()
        {
            UiRoot root = new UiRoot();
            Panel panel = root.Add(new Panel { Bounds = new Rect(100, 100, 400, 400) });
            Button under = panel.Add(new Button { Id = "under", Bounds = new Rect(150, 150, 200, 100) });
            Button over = panel.Add(new Button { Id = "over", Bounds = new Rect(200, 180, 200, 100) });

            Assert.AreSame(over, root.HitTest(P(250, 200)), "the later child is on top");
            Assert.AreSame(under, root.HitTest(P(160, 160)));
            Assert.AreSame(panel, root.HitTest(P(450, 450)), "the panel itself, not interactive");
            Assert.IsNull(panel.HitTest(P(50, 50)));
            Assert.AreSame(root, root.HitTest(P(10, 10)), "the root covers the canvas");
            Assert.IsNull(root.HitTest(P(-1, 10)));
            Assert.AreEqual(PortraitLayout.CanvasWidth, root.Bounds.Width);
        }

        [Test]
        public void Tap_ClicksTheButtonPressedAndReleased_ButNotADisabledOrHiddenOne()
        {
            UiRoot root = new UiRoot();
            int clicks = 0;
            Button button = root.Add(new Button { Bounds = new Rect(0, 0, 100, 100) });
            button.Clicked += () => clicks++;

            Assert.AreSame(button, root.Tap(P(50, 50)));
            Assert.AreEqual(1, clicks);

            root.OnPointerDown(P(50, 50));
            Assert.AreSame(button, root.Pressed);
            Assert.IsNull(root.OnPointerUp(P(150, 150)), "released off the button: no click");
            Assert.AreEqual(1, clicks);

            button.Enabled = false;
            Assert.IsNull(root.Tap(P(50, 50)));
            button.Enabled = true;
            button.Visible = false;
            Assert.IsNull(root.Tap(P(50, 50)));
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void ScrollView_OffsetsItsChildren_AndClipsThem()
        {
            UiRoot root = new UiRoot();
            ScrollView scroll = root.Add(new ScrollView { Bounds = new Rect(0, 200, 1080, 600), ContentHeight = 2000 });
            Button inside = scroll.Add(new Button { Id = "row", Bounds = new Rect(0, 900, 1080, 100) });

            Assert.AreNotSame(inside, root.HitTest(P(10, 1150)), "content y 900 sits below the view until scrolled");
            scroll.ScrollTo(800);
            Assert.AreSame(inside, root.HitTest(P(10, 350)), "content y 900 at view y 100 (canvas 300..400)");
            Assert.AreEqual(350f, inside.ToCanvas(P(10, 950)).Y, 0.001f);
            Assert.IsNull(scroll.HitTest(P(10, 150)), "above the view: clipped");
            scroll.ScrollTo(99999);
            Assert.AreEqual(1400f, scroll.ScrollY, "clamped to content - view");
        }

        [Test]
        public void Drag_ScrollsTheView_CancelsTheTap_AndCarriesOnWithInertia()
        {
            UiRoot root = new UiRoot();
            ScrollView scroll = root.Add(new ScrollView { Bounds = new Rect(0, 0, 1080, 1000), ContentHeight = 5000 });
            int clicks = 0;
            Button row = scroll.Add(new Button { Bounds = new Rect(0, 0, 1080, 5000) });
            row.Clicked += () => clicks++;

            root.OnPointerDown(P(500, 800));
            for (int i = 1; i <= 5; i++)
            {
                root.Tick(16f);
                root.OnPointerMove(P(500, 800 - 60 * i));
            }

            Assert.AreEqual(300f, scroll.ScrollY, 0.001f, "the content follows the finger");
            Assert.IsNull(root.Pressed, "a drag is no longer a press");
            Assert.IsNull(root.OnPointerUp(P(500, 500)));
            Assert.AreEqual(0, clicks, "a drag never clicks");
            Assert.Greater(scroll.Velocity, 1f, "released moving: it keeps sliding");

            float before = scroll.ScrollY;
            root.Tick(16f);
            Assert.Greater(scroll.ScrollY, before);
            for (int i = 0; i < 400; i++)
            {
                root.Tick(16f);
            }

            Assert.AreEqual(0f, scroll.Velocity, "friction brings it to rest");
            Assert.Greater(scroll.ScrollY, 400f);
            Assert.LessOrEqual(scroll.ScrollY, scroll.MaxScroll);
        }

        [Test]
        public void Drag_PastTheTop_Resists_AndSpringsBack()
        {
            UiRoot root = new UiRoot();
            ScrollView scroll = root.Add(new ScrollView { Bounds = new Rect(0, 0, 1080, 1000), ContentHeight = 3000 });
            root.OnPointerDown(P(500, 200));
            root.OnPointerMove(P(500, 400));
            Assert.Less(scroll.ScrollY, 0f, "pulled past the top");
            Assert.Greater(scroll.ScrollY, -200f, "but resisting");
            root.OnPointerUp(P(500, 400));
            for (int i = 0; i < 100; i++)
            {
                root.Tick(16f);
            }

            Assert.AreEqual(0f, scroll.ScrollY, "sprung back to the top");
        }

        [Test]
        public void Tabs_SelectTheTappedItem_AndRaiseChanged()
        {
            UiRoot root = new UiRoot();
            Tabs tabs = root.Add(new Tabs { Bounds = new Rect(0, 1800, 1000, 100) });
            tabs.Items.AddRange(new[] { "Map", "Roster", "Grove", "Avatar", "Inventory" });
            List<int> changes = new List<int>();
            tabs.Changed += changes.Add;

            root.Tap(P(450, 1850));
            Assert.AreEqual(2, tabs.Selected);
            root.Tap(P(450, 1850));
            root.Tap(P(10, 1850));
            CollectionAssert.AreEqual(new[] { 2, 0 }, changes, "re-tapping the selected tab changes nothing");
            Assert.AreEqual(new Rect(800, 1800, 200, 100).ToString(), tabs.ItemBounds(4).ToString());
        }

        [Test]
        public void Toast_FadesInAndOut_AndIsReplacedByTheNext()
        {
            ToastQueue toast = new ToastQueue();
            Assert.IsFalse(toast.Visible);
            toast.Show("Camp opens soon.", 1000f);
            Assert.AreEqual(0f, toast.Alpha);
            toast.Tick(500f);
            Assert.AreEqual(1f, toast.Alpha);
            toast.Show("Another", 1000f);
            Assert.AreEqual("Another", toast.Message);
            toast.Tick(1200f);
            Assert.IsFalse(toast.Visible);
            Assert.IsNull(toast.Message);
        }

        [Test]
        public void ShippedStyle_IsValid_AndResolvesTheHouseColours()
        {
            UiStyle style = VfxLibraryTests.Content.Style;
            Assert.AreEqual("#5B3A5EFF", style.Color("plum").ToString(), "warm plum outlines");
            Assert.AreEqual(style.Color("cream"), style.Panel("panel").Fill, "cream panels");
            Assert.AreEqual(style.Color("plum"), style.Panel("panel").Outline);
            Assert.Greater(style.Panel("panel").Radius, 0f, "rounded");
            Assert.AreEqual(style.Button("primary").Fill, style.Button("no-such-button").Fill, "an unknown look falls back to the first");
            Assert.AreEqual("#FF00FFFF", style.Color("no-such-colour").ToString(), "an unknown colour is loud magenta");
            Assert.AreEqual("#12345678", style.Color("#12345678").ToString(), "a #hex passes through");
        }

        [Test]
        public void StyleValidator_ReportsMissingLooksAndBadColours()
        {
            UiStyleData data = new UiStyleData
            {
                SchemaVersion = 1,
                Colors = new[] { new UiColorData { Key = "plum", Hex = "#5B3A5E" }, new UiColorData { Key = "bad", Hex = "5B3A5E" } },
                Panels = new[] { new UiPanelStyleData { Key = "panel", Fill = "nope", Outline = "plum", Radius = 4 } }
            };

            List<string> errors = UiStyleValidator.Validate(data);
            Assert.That(errors, Has.Some.Contains("'bad' is not #RRGGBB"));
            Assert.That(errors, Has.Some.Contains("Panel 'panel' fill 'nope'"));
            Assert.That(errors, Has.Some.Contains("Button look 'primary' is missing"));
            Assert.That(errors, Has.Some.Contains("Colour 'cream' is missing"));
        }
    }
}
