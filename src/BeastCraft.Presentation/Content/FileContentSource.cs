using System.IO;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// Content as plain files under a folder: the desktop host's <c>Content/</c> beside the
    /// executable, the repo's <c>content/</c> folder, or a <c>--content</c> folder.
    /// </summary>
    public sealed class FileContentSource : IContentSource
    {
        public FileContentSource(string root)
        {
            Location = root;
        }

        public string Location { get; }

        public string Describe(string path)
        {
            return Path.Combine(Location, path.Replace('/', Path.DirectorySeparatorChar));
        }

        public bool Exists(string path)
        {
            return File.Exists(Describe(path));
        }

        public Stream Open(string path)
        {
            return File.OpenRead(Describe(path));
        }
    }
}
