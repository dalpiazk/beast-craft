using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// A full screen in the <see cref="ScreenStack"/> (title, map, encounter, battle, results).
    /// Engine-neutral: the game's screens implement it with their drawing and input; the tests with
    /// fakes.
    /// </summary>
    public interface IScreen
    {
        /// <summary>A stable name (debug flags, tests, logs), e.g. <c>"map"</c>.</summary>
        string Name { get; }

        /// <summary>Called when the screen becomes the top one (pushed, or uncovered by a pop).</summary>
        void Enter();

        /// <summary>Called when the screen stops being the top one (popped, covered or replaced).</summary>
        void Exit();

        /// <summary>
        /// The back button (Android Back, desktop Esc) with no modal open: true when the screen dealt
        /// with it itself (closed a panel, asked to confirm, switched tab); false lets the stack pop it
        /// (or, at the root, ask the host to quit).
        /// </summary>
        bool HandleBack();
    }

    /// <summary>A modal over the top screen (a confirm, the settings, a picker): lighter than a screen, drawn over it.</summary>
    public interface IModal
    {
        string Name { get; }

        /// <summary>The back button while this modal is on top: true if it dealt with it; false closes it.</summary>
        bool HandleBack();
    }

    /// <summary>What <see cref="ScreenStack.Back"/> did.</summary>
    public enum BackOutcome
    {
        /// <summary>The top modal handled it or was closed.</summary>
        Modal,

        /// <summary>The top screen handled it (e.g. asked to confirm).</summary>
        Screen,

        /// <summary>The top screen was popped.</summary>
        Popped,

        /// <summary>Nothing left to go back to: the host should quit.</summary>
        Quit
    }

    /// <summary>How a screen change animates.</summary>
    public enum TransitionKind
    {
        None,

        /// <summary>A screen pushed on (slides in from the right).</summary>
        Forward,

        /// <summary>A screen popped off (slides in from the left).</summary>
        Backward,

        /// <summary>A screen replaced or the stack reset (cross-fade through the house colour).</summary>
        Fade
    }

    /// <summary>
    /// The screen stack and, over it, the modal stack. Screens are pushed and popped (map →
    /// encounter → battle → results, then back to the map); modals sit over the top screen. The
    /// back button closes the top modal first, then asks the top screen, then pops it; with only the
    /// root left, <see cref="Back"/> says <see cref="BackOutcome.Quit"/> (the title screen asks
    /// before that). Each change starts a short <see cref="Transition"/> the host draws.
    /// </summary>
    public sealed class ScreenStack
    {
        /// <summary>How long a transition runs, ms.</summary>
        public const float TransitionMs = 220f;

        private readonly List<IScreen> _screens = new List<IScreen>();
        private readonly List<IModal> _modals = new List<IModal>();

        /// <summary>Transitions are skipped (screenshots, tests) when false.</summary>
        public bool Animate = true;

        /// <summary>
        /// Settings &gt; Visuals' Effects intensity, read fresh by the host each frame (#52 step 4):
        /// <see cref="EffectsIntensity.Reduced"/> always cross-fades a screen change instead of
        /// sliding it (<see cref="TransitionKind.Fade"/> regardless of the kind <see cref="Begin"/>
        /// was asked for); <see cref="EffectsIntensity.Minimal"/> skips the transition's motion
        /// entirely, the same as <see cref="Animate"/> false. Engine-neutral (no renderer needed) so
        /// the fallback itself is unit-testable.
        /// </summary>
        public EffectsIntensity Effects = EffectsIntensity.Full;

        public IReadOnlyList<IScreen> Screens
        {
            get { return _screens; }
        }

        public IReadOnlyList<IModal> Modals
        {
            get { return _modals; }
        }

        public IScreen Top
        {
            get { return _screens.Count == 0 ? null : _screens[_screens.Count - 1]; }
        }

        public IModal TopModal
        {
            get { return _modals.Count == 0 ? null : _modals[_modals.Count - 1]; }
        }

        public TransitionKind Transition { get; private set; }

        /// <summary>
        /// The screen a <see cref="TransitionKind"/> change is animating away from (drawn alongside
        /// the new <see cref="Top"/> while it runs); null once the transition ends, or when there was
        /// nothing to animate from (the first screen ever pushed), or <see cref="Animate"/>/
        /// <see cref="Effects"/> skipped it.
        /// </summary>
        public IScreen TransitionFrom { get; private set; }

        public float TransitionElapsedMs { get; private set; }

        /// <summary>0 at the start of the current transition, 1 when it is over (or there is none).</summary>
        public float TransitionProgress
        {
            get { return Transition == TransitionKind.None ? 1f : Math.Min(1f, TransitionElapsedMs / TransitionMs); }
        }

        /// <summary>Raised after every change of the top screen.</summary>
        public event Action<IScreen> TopChanged;

        public void Push(IScreen screen)
        {
            if (screen == null)
            {
                throw new ArgumentNullException(nameof(screen));
            }

            IScreen from = Top;
            Top?.Exit();
            _modals.Clear();
            _screens.Add(screen);
            Begin(_screens.Count == 1 ? TransitionKind.Fade : TransitionKind.Forward, from);
            screen.Enter();
            TopChanged?.Invoke(screen);
        }

        /// <summary>Pops the top screen (never the last one); false when only the root is left.</summary>
        public bool Pop()
        {
            if (_screens.Count <= 1)
            {
                return false;
            }

            IScreen top = Top;
            _modals.Clear();
            _screens.RemoveAt(_screens.Count - 1);
            top.Exit();
            Begin(TransitionKind.Backward, top);
            Top.Enter();
            TopChanged?.Invoke(Top);
            return true;
        }

        /// <summary>Replaces the top screen (or pushes the first).</summary>
        public void Replace(IScreen screen)
        {
            if (screen == null)
            {
                throw new ArgumentNullException(nameof(screen));
            }

            IScreen top = Top;
            _modals.Clear();
            if (top != null)
            {
                _screens.RemoveAt(_screens.Count - 1);
                top.Exit();
            }

            _screens.Add(screen);
            Begin(TransitionKind.Fade, top);
            screen.Enter();
            TopChanged?.Invoke(screen);
        }

        /// <summary>Pops down to the topmost screen named <paramref name="name"/>; false (nothing changes) when there is none.</summary>
        public bool PopTo(string name)
        {
            int index = _screens.FindLastIndex(s => s.Name == name);
            if (index < 0)
            {
                return false;
            }

            if (index == _screens.Count - 1)
            {
                _modals.Clear();
                return true;
            }

            IScreen top = Top;
            _modals.Clear();
            _screens.RemoveRange(index + 1, _screens.Count - index - 1);
            top.Exit();
            Begin(TransitionKind.Backward, top);
            Top.Enter();
            TopChanged?.Invoke(Top);
            return true;
        }

        /// <summary>Clears everything and makes <paramref name="screen"/> the root.</summary>
        public void ResetTo(IScreen screen)
        {
            if (screen == null)
            {
                throw new ArgumentNullException(nameof(screen));
            }

            IScreen from = Top;
            Top?.Exit();
            _modals.Clear();
            _screens.Clear();
            _screens.Add(screen);
            Begin(TransitionKind.Fade, from);
            screen.Enter();
            TopChanged?.Invoke(screen);
        }

        /// <summary>How long a modal's spring-pop plays, ms.</summary>
        public const float ModalPopMs = 180f;

        private float _modalElapsedMs;

        /// <summary>
        /// 0 at the top modal's own first frame open, 1 once its spring-pop has settled (or there is
        /// no modal open) — a <see cref="GameModal"/> eases its open scale/fade with it. Skipped
        /// outright (reads 1) the same way <see cref="Begin"/> skips a screen transition:
        /// <see cref="Animate"/> false or <see cref="EffectsIntensity.Minimal"/>.
        /// </summary>
        public float ModalProgress
        {
            get { return _modals.Count == 0 || !Animate || Effects == EffectsIntensity.Minimal ? 1f : Math.Min(1f, _modalElapsedMs / ModalPopMs); }
        }

        public void PushModal(IModal modal)
        {
            if (modal == null)
            {
                throw new ArgumentNullException(nameof(modal));
            }

            _modals.Add(modal);
            _modalElapsedMs = 0f;
        }

        /// <summary>Closes <paramref name="modal"/> (null: the top one); false when it is not open.</summary>
        public bool CloseModal(IModal modal = null)
        {
            if (_modals.Count == 0)
            {
                return false;
            }

            return _modals.Remove(modal ?? TopModal);
        }

        public bool IsOpen(string modalName)
        {
            return _modals.Exists(m => m.Name == modalName);
        }

        /// <summary>The back button: see the class remarks.</summary>
        public BackOutcome Back()
        {
            IModal modal = TopModal;
            if (modal != null)
            {
                if (!modal.HandleBack())
                {
                    _modals.Remove(modal);
                }

                return BackOutcome.Modal;
            }

            IScreen top = Top;
            if (top == null)
            {
                return BackOutcome.Quit;
            }

            if (top.HandleBack())
            {
                return BackOutcome.Screen;
            }

            return Pop() ? BackOutcome.Popped : BackOutcome.Quit;
        }

        public void Update(float elapsedMs)
        {
            if (_modals.Count > 0 && _modalElapsedMs < ModalPopMs)
            {
                _modalElapsedMs += Math.Max(0f, elapsedMs);
            }

            if (Transition == TransitionKind.None)
            {
                return;
            }

            TransitionElapsedMs += Math.Max(0f, elapsedMs);
            if (TransitionElapsedMs >= TransitionMs)
            {
                Transition = TransitionKind.None;
                TransitionElapsedMs = 0f;
                TransitionFrom = null;
            }
        }

        /// <summary>
        /// Starts a transition out of <paramref name="from"/> (the screen just left) into the new
        /// <see cref="Top"/>, folding in <see cref="Animate"/> and <see cref="Effects"/>'s reduced-
        /// motion fallback: off entirely (<see cref="Animate"/> false, or <see cref="EffectsIntensity.Minimal"/>)
        /// skips the transition outright (<see cref="TransitionKind.None"/>, <see cref="TransitionFrom"/>
        /// null — the new screen is simply there, as if drawn with no transition system at all);
        /// <see cref="EffectsIntensity.Reduced"/> always cross-fades, whatever <paramref name="kind"/>
        /// asked for.
        /// </summary>
        private void Begin(TransitionKind kind, IScreen from)
        {
            if (!Animate || Effects == EffectsIntensity.Minimal)
            {
                Transition = TransitionKind.None;
                TransitionFrom = null;
                TransitionElapsedMs = 0f;
                return;
            }

            Transition = Effects == EffectsIntensity.Reduced && kind != TransitionKind.None ? TransitionKind.Fade : kind;
            TransitionFrom = from;
            TransitionElapsedMs = 0f;
        }
    }
}
