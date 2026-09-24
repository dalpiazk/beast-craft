using System.Collections.Generic;
using System.IO;
using BeastCraft.Creatures.Roster;
using BeastCraft.Presentation.Content;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="GameContent"/> through an <see cref="IContentSource"/> (the seam the Android host
    /// reads its APK assets through): the same content as the folder overload, sprites opened
    /// through the source, and a missing file reported by the source's own description.
    /// </summary>
    public class ContentSourceTests
    {
        [Test]
        public void RelativeOf_StripsTheProjectPrefix_AndKeepsForwardSlashes()
        {
            Assert.AreEqual("Art/Pixel/pixel-art-manifest.json", GameContent.RelativeOf(PixelArtManifestData.ProjectRelativePath));
            Assert.AreEqual("Data/x.json", GameContent.RelativeOf("Data/x.json"));
        }

        [Test]
        public void LoadThroughASource_MatchesLoadFromTheFolder()
        {
            string root = GameContent.FindRoot();
            List<string> errors = new List<string>();
            GameContent viaSource = GameContent.Load(new RecordingSource(new FileContentSource(root), null), errors);

            Assert.IsNotNull(viaSource, string.Join("\n", errors));
            Assert.AreEqual(root, viaSource.Root);
            Assert.IsInstanceOf<RecordingSource>(viaSource.Source);
            CollectionAssert.AreEquivalent(VfxLibraryTests.Content.KnownSkillIds, viaSource.KnownSkillIds);
            Assert.AreEqual(VfxLibraryTests.Content.Art.Sprites.Length, viaSource.Art.Sprites.Length);

            string folder = "Art/Pixel/";
            using (Stream png = viaSource.Source.Open(folder + viaSource.Art.Sprites[0].File))
            {
                Assert.Greater(png.Length, 8);
            }
        }

        [Test]
        public void MissingFile_IsReportedWithTheSourcesDescription()
        {
            string hidden = GameContent.RelativeOf(BeastRosterData.ProjectRelativePath);
            RecordingSource source = new RecordingSource(new FileContentSource(GameContent.FindRoot()), hidden);
            List<string> errors = new List<string>();

            Assert.IsNull(GameContent.Load(source, errors));
            Assert.Contains("Missing " + source.Describe(hidden) + ".", errors);
        }

        /// <summary>A source that forwards to another, optionally hiding one file.</summary>
        private sealed class RecordingSource : IContentSource
        {
            private readonly IContentSource _inner;
            private readonly string _hidden;

            public RecordingSource(IContentSource inner, string hidden)
            {
                _inner = inner;
                _hidden = hidden;
            }

            public string Location
            {
                get { return _inner.Location; }
            }

            public string Describe(string path)
            {
                return _inner.Describe(path);
            }

            public bool Exists(string path)
            {
                return path != _hidden && _inner.Exists(path);
            }

            public Stream Open(string path)
            {
                return _inner.Open(path);
            }
        }
    }
}
