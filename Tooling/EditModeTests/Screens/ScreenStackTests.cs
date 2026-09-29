using System.Collections.Generic;
using BeastCraft.Presentation.Screens;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Navigation: the screen stack's push, pop, replace, pop-to and reset (with enter/exit in
    /// order and a transition each), the modal stack over it, and the back button — modals first,
    /// then the screen's own handling, then a pop, and Quit only at the root; plus the bottom
    /// nav's back (another tab returns to the Map).
    /// </summary>
    public class ScreenStackTests
    {
        private sealed class FakeScreen : IScreen
        {
            private readonly List<string> _log;

            public FakeScreen(string name, List<string> log)
            {
                Name = name;
                _log = log;
            }

            public string Name { get; }

            /// <summary>How many backs this screen swallows itself (e.g. "ask before quitting").</summary>
            public int SwallowBacks;

            public void Enter()
            {
                _log.Add("enter " + Name);
            }

            public void Exit()
            {
                _log.Add("exit " + Name);
            }

            public bool HandleBack()
            {
                if (SwallowBacks <= 0)
                {
                    return false;
                }

                SwallowBacks--;
                _log.Add("back " + Name);
                return true;
            }
        }

        private sealed class FakeModal : IModal
        {
            public FakeModal(string name, bool sticky = false)
            {
                Name = name;
                Sticky = sticky;
            }

            public string Name { get; }

            public bool Sticky { get; }

            public bool HandleBack()
            {
                return Sticky;
            }
        }

        [Test]
        public void PushPopReplace_EnterAndExitInOrder()
        {
            List<string> log = new List<string>();
            ScreenStack stack = new ScreenStack();
            stack.Push(new FakeScreen("title", log));
            stack.Push(new FakeScreen("map", log));
            stack.Push(new FakeScreen("encounter", log));
            Assert.AreEqual(TransitionKind.Forward, stack.Transition);
            stack.Replace(new FakeScreen("battle", log));
            Assert.AreEqual(TransitionKind.Fade, stack.Transition);
            stack.Push(new FakeScreen("results", log));

            Assert.IsTrue(stack.PopTo("map"));
            Assert.AreEqual("map", stack.Top.Name);
            Assert.AreEqual(TransitionKind.Backward, stack.Transition);
            Assert.AreEqual(2, stack.Screens.Count);
            CollectionAssert.AreEqual(new[]
            {
                "enter title", "exit title", "enter map", "exit map", "enter encounter", "exit encounter", "enter battle", "exit battle", "enter results",
                "exit results", "enter map"
            }, log);

            Assert.IsFalse(stack.PopTo("nowhere"));
            Assert.IsTrue(stack.Pop());
            Assert.IsFalse(stack.Pop(), "the root is never popped");
            Assert.AreEqual("title", stack.Top.Name);
        }

        [Test]
        public void Transition_RunsItsCourse_OrIsSkipped()
        {
            ScreenStack stack = new ScreenStack();
            stack.Push(new FakeScreen("title", new List<string>()));
            Assert.AreEqual(0f, stack.TransitionProgress);
            stack.Update(ScreenStack.TransitionMs / 2f);
            Assert.AreEqual(0.5f, stack.TransitionProgress, 0.001f);
            stack.Update(ScreenStack.TransitionMs);
            Assert.AreEqual(TransitionKind.None, stack.Transition);
            Assert.AreEqual(1f, stack.TransitionProgress);

            ScreenStack still = new ScreenStack { Animate = false };
            still.Push(new FakeScreen("title", new List<string>()));
            Assert.AreEqual(TransitionKind.None, still.Transition);
        }

        [Test]
        public void Back_ClosesModalsFirst_ThenAsksTheScreen_ThenPops_ThenQuits()
        {
            List<string> log = new List<string>();
            ScreenStack stack = new ScreenStack();
            FakeScreen title = new FakeScreen("title", log) { SwallowBacks = 1 };
            stack.Push(title);
            stack.Push(new FakeScreen("map", log));
            stack.PushModal(new FakeModal("settings"));
            stack.PushModal(new FakeModal("confirm"));

            Assert.AreEqual(BackOutcome.Modal, stack.Back());
            Assert.AreEqual("settings", stack.TopModal.Name, "the top modal closed first");
            Assert.AreEqual(BackOutcome.Modal, stack.Back());
            Assert.IsNull(stack.TopModal);
            Assert.AreEqual(BackOutcome.Popped, stack.Back(), "map pops back to the title");
            Assert.AreEqual("title", stack.Top.Name);
            Assert.AreEqual(BackOutcome.Screen, stack.Back(), "the title asks before quitting");
            Assert.Contains("back title", log);
            Assert.AreEqual(BackOutcome.Quit, stack.Back(), "then there is nothing left");
        }

        [Test]
        public void Back_OnAStickyModal_KeepsIt_AndPushingAScreenClearsModals()
        {
            ScreenStack stack = new ScreenStack();
            stack.Push(new FakeScreen("battle", new List<string>()));
            stack.PushModal(new FakeModal("busy", sticky: true));
            Assert.AreEqual(BackOutcome.Modal, stack.Back());
            Assert.IsTrue(stack.IsOpen("busy"));
            stack.Push(new FakeScreen("results", new List<string>()));
            Assert.IsEmpty(stack.Modals);
            Assert.IsFalse(stack.CloseModal());
        }

        [Test]
        public void ResetTo_MakesANewRoot()
        {
            List<string> log = new List<string>();
            ScreenStack stack = new ScreenStack();
            stack.Push(new FakeScreen("title", log));
            stack.Push(new FakeScreen("map", log));
            stack.ResetTo(new FakeScreen("title2", log));
            Assert.AreEqual(1, stack.Screens.Count);
            Assert.AreEqual(BackOutcome.Quit, stack.Back());
        }

        [Test]
        public void HomeNav_BackFromAnotherTab_ReturnsToTheMap_AndTheMapRosterAndAvatarWork()
        {
            HomeViewModel home = new HomeViewModel();
            Assert.AreEqual(HomeTab.Map, home.Tab);
            Assert.IsTrue(home.TabAvailable);
            Assert.IsFalse(home.HandleBack(), "on the Map, back is the stack's");

            home.Select(HomeTab.Roster);
            Assert.IsTrue(home.TabAvailable, "the roster is built");
            Assert.IsTrue(home.HandleBack());
            Assert.AreEqual(HomeTab.Map, home.Tab);

            home.Select(HomeTab.Avatar);
            Assert.IsTrue(home.TabAvailable, "the avatar identity card, achievements and look-token shop are built");
            Assert.IsTrue(home.HandleBack());
            Assert.AreEqual(HomeTab.Map, home.Tab);

            home.Select(HomeTab.Grove);
            Assert.IsFalse(home.TabAvailable, "coming soon");
            StringAssert.Contains("idle rewards", home.ComingSoon);
            Assert.IsTrue(home.HandleBack());
            Assert.AreEqual(HomeTab.Map, home.Tab);
            CollectionAssert.AreEqual(new[] { "Map", "Roster", "Grove", "Avatar", "Inventory" }, HomeViewModel.TabNames, "the Camp tab is the Grove (producer rename)");
        }
    }
}
