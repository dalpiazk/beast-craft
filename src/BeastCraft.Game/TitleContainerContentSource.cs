using System;
using System.IO;
using BeastCraft.Presentation.Content;
using Microsoft.Xna.Framework;

namespace BeastCraft.Game
{
    /// <summary>
    /// Content through MonoGame's <see cref="TitleContainer"/>: on Android, the APK's assets (read
    /// with the activity's AssetManager), under a folder (<c>Content</c>). Each file is copied into
    /// memory on open, because asset streams cannot seek and <c>Texture2D.FromStream</c> and the
    /// JSON reader should see the same seekable stream on every platform.
    /// </summary>
    public sealed class TitleContainerContentSource : IContentSource
    {
        private readonly string _folder;

        public TitleContainerContentSource(string folder)
        {
            _folder = folder.TrimEnd('/');
        }

        public string Location
        {
            get { return "title container: " + _folder; }
        }

        public string Describe(string path)
        {
            return _folder + "/" + path;
        }

        public bool Exists(string path)
        {
            try
            {
                using (TitleContainer.OpenStream(Describe(path)))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public Stream Open(string path)
        {
            MemoryStream copy = new MemoryStream();
            using (Stream stream = TitleContainer.OpenStream(Describe(path)))
            {
                stream.CopyTo(copy);
            }

            copy.Position = 0;
            return copy;
        }
    }
}
