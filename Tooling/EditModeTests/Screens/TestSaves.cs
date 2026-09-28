using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Save;
using BeastCraft.Tutorial;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Saves the screen tests start from. <see cref="SixStarters"/> is the roster the core loop's
    /// tests were written against before Hearthglen (the campaign pacing model's six beasts, all at
    /// level 1 with their default loadouts, the avatar's starting kit), past the tutorial and in the
    /// first campaign region.
    /// </summary>
    internal static class TestSaves
    {
        public static readonly string[] SixSpecies = { "griffin", "phoenix", "golem", "kirin", "treant", "tarasque" };

        public static PlayerSave SixStarters(GameContent content, int level = 1)
        {
            PlayerSave save = PlayerSave.CreateNew();
            StarterPicks.GrantAvatarDefaults(save, content.SkillLibrary);
            foreach (string species in SixSpecies)
            {
                StarterPicks.AddBeast(save, content.SkillLibrary, species, level);
            }

            // Past Hearthglen without having played its fights (as the skip): nothing counts toward idle yet.
            save.Tutorial.HearthglenCleared = true;
            save.Tutorial.Skipped = true;
            return save;
        }

        /// <summary>A session playing <see cref="SixStarters"/> in the first campaign region (as New Game did before Hearthglen).</summary>
        public static GameSession Started(GameSession session)
        {
            session.StartWith(SixStarters(session.Content));
            return session;
        }
    }
}
