using BeastCraft.Localization;
using BeastCraft.Presentation.Content;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Gives Core's rules their text table before any test runs (<see cref="RulesText"/>): the game sets
    /// it when the content loads, and many tests call the rules without loading the content, so their
    /// refusals and fallback names read the English from <c>en.json</c> as they do in the game.
    /// </summary>
    [SetUpFixture]
    public sealed class RulesTextSetUp
    {
        [OneTimeSetUp]
        public void UseTheEnglishTable()
        {
            RulesText.Table = ContentText.TableFor(GameContent.FindRoot());
        }
    }
}
