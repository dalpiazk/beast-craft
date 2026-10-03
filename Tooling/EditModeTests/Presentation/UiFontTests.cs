using System.IO;
using System.Text;
using BeastCraft.Presentation.Content;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The UI's two typefaces ship correctly: each TTF and its SIL OFL licence are in the content
    /// root, both hosts package them (desktop output and Android APK assets), and the third-party
    /// notice names them. (Each font's bytes are checked only when Git LFS has pulled them; CI
    /// checks out without LFS.)
    /// </summary>
    public class UiFontTests
    {
        private static string Root
        {
            get { return GameContent.FindRoot(); }
        }

        private static string Repo
        {
            get { return Path.GetDirectoryName(Root); }
        }

        [TestCase(GameContent.UiFontPath, GameContent.UiFontLicensePath, "Fredoka")]
        [TestCase(GameContent.UiBodyFontPath, GameContent.UiBodyFontLicensePath, "Braille Institute")]
        public void TheFontAndItsLicence_AreInTheContentRoot(string fontPath, string licensePath, string nameInLicence)
        {
            string font = Path.Combine(Root, fontPath.Replace('/', Path.DirectorySeparatorChar));
            string licence = Path.Combine(Root, licensePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.IsTrue(File.Exists(font), font);
            Assert.IsTrue(File.Exists(licence), licence);
            StringAssert.Contains("SIL Open Font License, Version 1.1", File.ReadAllText(licence));
            StringAssert.Contains(nameInLicence, File.ReadAllText(licence));

            byte[] head = new byte[4];
            using (FileStream stream = File.OpenRead(font))
            {
                Assert.AreEqual(4, stream.Read(head, 0, 4));
            }

            if (Encoding.ASCII.GetString(head) == "vers")
            {
                Assert.Ignore("The font is a Git LFS pointer here (not pulled); only its presence is checked.");
            }

            CollectionAssert.AreEqual(new byte[] { 0, 1, 0, 0 }, head, "a TrueType font (sfnt version 1.0)");
        }

        [TestCase("src/BeastCraft.Desktop/BeastCraft.Desktop.csproj", "Content\\fonts\\")]
        [TestCase("src/BeastCraft.Android/BeastCraft.Android.csproj", "Assets\\Content\\fonts\\")]
        public void BothHosts_PackTheFontAndItsLicence(string project, string destination)
        {
            string text = File.ReadAllText(Path.Combine(Repo, project.Replace('/', Path.DirectorySeparatorChar)));

            // *.ttf is a glob: it packs both Fredoka and Atkinson Hyperlegible without naming either
            // file; each licence is named explicitly (and OFL.txt is also Fredoka's exact filename).
            StringAssert.Contains("content\\fonts\\*.ttf", text);
            StringAssert.Contains("content\\fonts\\OFL.txt", text);
            StringAssert.Contains("content\\fonts\\AtkinsonHyperlegible-OFL.txt", text);
            StringAssert.Contains(destination, text);
        }

        [Test]
        public void TheThirdPartyNotice_NamesBothFontsAndTheRasteriser()
        {
            string notice = File.ReadAllText(Path.Combine(Repo, "THIRD-PARTY-NOTICES.md"));

            StringAssert.Contains("Fredoka", notice);
            StringAssert.Contains("Atkinson Hyperlegible", notice);
            StringAssert.Contains("SIL Open Font License", notice);
            StringAssert.Contains("FontStashSharp", notice);
        }
    }
}
