using System.IO;
using System.Text;
using BeastCraft.Presentation.Content;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The UI typeface ships correctly: the TTF and its SIL OFL licence are in the content root,
    /// both hosts package them (desktop output and Android APK assets), and the third-party notice
    /// names them. (The font's bytes are checked only when Git LFS has pulled them; CI checks out
    /// without LFS.)
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

        [Test]
        public void TheFontAndItsLicence_AreInTheContentRoot()
        {
            string font = Path.Combine(Root, GameContent.UiFontPath.Replace('/', Path.DirectorySeparatorChar));
            string licence = Path.Combine(Root, GameContent.UiFontLicensePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.IsTrue(File.Exists(font), font);
            Assert.IsTrue(File.Exists(licence), licence);
            StringAssert.Contains("SIL Open Font License, Version 1.1", File.ReadAllText(licence));
            StringAssert.Contains("Fredoka", File.ReadAllText(licence));

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

            StringAssert.Contains("content\\fonts\\*.ttf", text);
            StringAssert.Contains("content\\fonts\\OFL.txt", text);
            StringAssert.Contains(destination, text);
        }

        [Test]
        public void TheThirdPartyNotice_NamesTheFontAndTheRasteriser()
        {
            string notice = File.ReadAllText(Path.Combine(Repo, "THIRD-PARTY-NOTICES.md"));

            StringAssert.Contains("Fredoka", notice);
            StringAssert.Contains("SIL Open Font License", notice);
            StringAssert.Contains("FontStashSharp", notice);
        }
    }
}
