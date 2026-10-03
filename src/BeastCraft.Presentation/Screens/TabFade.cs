using System;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// A gentle fade for an internal tab switch (#52 step 4) — every screen with its own sub-tabs
    /// (Settings' four, the Grove's four, the Avatar's four, Inventory's and the Shop's two, Home's
    /// Map/Roster) rebuilds its content instantly on <c>Tabs.Changed</c> (a new <c>CardList</c>, a new
    /// scroll position); this is the paint-time half, a translucent page-colour veil the new content
    /// is under that clears over <see cref="Ms"/>. Unlike a screen push/pop (<see cref="ScreenStack"/>,
    /// also engine-neutral so its own timing is unit-testable without a renderer), a tab switch never
    /// leaves its own screen, so there is no second frame to slide against — a fade reads as "new
    /// content settling in" without one. Call <see cref="Reset"/> from the tab's <c>Changed</c>
    /// handler, <see cref="Update"/> once a frame, and fill the content area with <see cref="Alpha"/>
    /// of the page's own background colour last, after everything else that area draws.
    /// </summary>
    public sealed class TabFade
    {
        public const float Ms = 150f;

        private float _elapsedMs = Ms;

        public void Reset()
        {
            _elapsedMs = 0f;
        }

        public void Update(float elapsedMs)
        {
            _elapsedMs = Math.Min(Ms, _elapsedMs + Math.Max(0f, elapsedMs));
        }

        /// <summary>The veil's alpha right now: 1 just after <see cref="Reset"/>, 0 once settled. <paramref name="animate"/> false (Animate off, or Minimal effects) reads 0 — no veil at all.</summary>
        public float Alpha(bool animate)
        {
            return animate ? 1f - _elapsedMs / Ms : 0f;
        }
    }
}
