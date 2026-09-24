using System.IO;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// Where a host's content files come from. Paths are relative to the content root (the folder
    /// holding <c>Data/</c> and <c>Art/Pixel/</c>) and always use forward slashes, e.g.
    /// <c>Data/Creatures/beast-roster.json</c> (<see cref="GameContent.RelativeOf"/> turns a
    /// ProjectRelativePath into one).
    /// <para>
    /// The desktop host reads plain files (<see cref="FileContentSource"/>); the Android host reads
    /// the APK's assets (BeastCraft.Game's <c>TitleContainerContentSource</c>). Everything above
    /// this (<see cref="GameContent.Load(IContentSource, System.Collections.Generic.List{string})"/>,
    /// the sprite atlas) only ever opens streams through it.
    /// </para>
    /// </summary>
    public interface IContentSource
    {
        /// <summary>The content root, for messages (a folder, or e.g. "APK assets: Content").</summary>
        string Location { get; }

        /// <summary>Where <paramref name="path"/> lives, for messages.</summary>
        string Describe(string path);

        /// <summary>True when <paramref name="path"/> exists.</summary>
        bool Exists(string path);

        /// <summary>Opens <paramref name="path"/> for reading; the caller disposes the stream.</summary>
        Stream Open(string path);
    }
}
