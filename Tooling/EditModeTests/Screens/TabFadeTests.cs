using BeastCraft.Presentation.Screens;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="TabFade"/> (#52 step 4): the veil timing a screen's own internal tab switch (not a
    /// <see cref="BeastCraft.Presentation.Screens.ScreenStack"/> push/pop) fades out over, and its
    /// Animate/Minimal skip.
    /// </summary>
    public class TabFadeTests
    {
        [Test]
        public void StartsSettled_PlaysOnceOnReset_AndSkipsWhenNotAnimating()
        {
            TabFade fade = new TabFade();
            Assert.AreEqual(0f, fade.Alpha(true), "nothing to fade before the first switch");

            fade.Reset();
            Assert.AreEqual(1f, fade.Alpha(true), "fully veiled right after a switch");
            fade.Update(TabFade.Ms / 2f);
            Assert.AreEqual(0.5f, fade.Alpha(true), 0.001f);
            fade.Update(TabFade.Ms);
            Assert.AreEqual(0f, fade.Alpha(true), "settled, and stays there");

            fade.Reset();
            Assert.AreEqual(0f, fade.Alpha(false), "animate = false (screenshot mode, or Minimal effects): no veil at all");
        }

        [Test]
        public void Update_NeverOvershootsPastSettled()
        {
            TabFade fade = new TabFade();
            fade.Reset();
            fade.Update(TabFade.Ms * 10f);
            Assert.AreEqual(0f, fade.Alpha(true));
            fade.Update(-50f);
            Assert.AreEqual(0f, fade.Alpha(true), "a negative elapsed (should never happen) never un-settles it");
        }
    }
}
