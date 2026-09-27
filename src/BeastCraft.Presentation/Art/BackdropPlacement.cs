using System;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;

namespace BeastCraft.Presentation.Art
{
    /// <summary>
    /// Where a painted backdrop (<see cref="BattleBackdropData"/>) lies in board space (tile (0, 0)'s
    /// centre the origin, <see cref="HexLayout"/> pixels): its <see cref="BattleBackdropData.BoardRect"/>
    /// (fractions of the image) lands exactly on the arena's tiles box
    /// (<see cref="HexLayout.BoardBounds"/>), and the rest of the image extends around it by the same
    /// scale. Pure maths, no engine types; the renderer draws the image into <see cref="ImageRect"/>
    /// through the camera's board transform, so the camera moves the backdrop with the board.
    /// </summary>
    public static class BackdropPlacement
    {
        /// <summary>How far a backdrop's pixels may be from square (image aspect against its board rect's), as a fraction.</summary>
        public const float AspectTolerance = 0.02f;

        /// <summary>
        /// The whole image of <paramref name="backdrop"/> in board space for a
        /// <paramref name="width"/> x <paramref name="height"/> arena: the tiles box
        /// <c>B</c> stretched so the board rect (<c>bx, by, bw, bh</c>) covers it, i.e.
        /// <c>(B.X - bx / bw * B.Width, B.Y - by / bh * B.Height, B.Width / bw, B.Height / bh)</c>.
        /// Independent of the image's resolution. An empty or degenerate board rect gives the tiles box.
        /// </summary>
        public static Rect ImageRect(BattleBackdropData backdrop, int width, int height)
        {
            Rect tiles = HexLayout.BoardBounds(width, height);
            BattleArtRect board = backdrop == null ? null : backdrop.BoardRect;
            if (board == null || !(board.Width > 0f) || !(board.Height > 0f))
            {
                return tiles;
            }

            float w = tiles.Width / board.Width;
            float h = tiles.Height / board.Height;
            return new Rect(tiles.X - board.X * w, tiles.Y - board.Y * h, w, h);
        }

        /// <summary>
        /// Where image pixel (<paramref name="px"/>, <paramref name="py"/>) of a
        /// <paramref name="imageWidth"/> x <paramref name="imageHeight"/> backdrop lands in board space.
        /// </summary>
        public static Vec2 ImageToBoard(BattleBackdropData backdrop, int width, int height, int imageWidth, int imageHeight, float px, float py)
        {
            Rect image = ImageRect(backdrop, width, height);
            return new Vec2(image.X + px / Math.Max(1, imageWidth) * image.Width, image.Y + py / Math.Max(1, imageHeight) * image.Height);
        }

        /// <summary>
        /// How far from square one image pixel is drawn: (board px per image px across) / (down),
        /// for a <paramref name="imageWidth"/> x <paramref name="imageHeight"/> image. 1 = undistorted.
        /// </summary>
        public static float PixelAspect(BattleBackdropData backdrop, int width, int height, int imageWidth, int imageHeight)
        {
            Rect image = ImageRect(backdrop, width, height);
            float across = image.Width / Math.Max(1, imageWidth);
            float down = image.Height / Math.Max(1, imageHeight);
            return down > 0f ? across / down : 0f;
        }

        /// <summary>
        /// What the whole portrait canvas shows of board space at the fit-all view of a
        /// <paramref name="width"/> x <paramref name="height"/> arena (the board band's fit, extended
        /// to the canvas edges). A backdrop that covers this covers the canvas at every camera view:
        /// the camera only zooms in from fit-all and keeps its view inside the arena.
        /// </summary>
        public static Rect CanvasAtFitAll(int width, int height, PortraitLayout layout = null)
        {
            layout = layout ?? new PortraitLayout();
            BoardFit fit = layout.FitBoard(width, height);
            Vec2 topLeft = fit.ToBoard(0f, 0f);
            Vec2 bottomRight = fit.ToBoard(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
            return new Rect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
        }

        /// <summary>Whether <paramref name="outer"/> holds <paramref name="inner"/>, give or take <paramref name="slack"/> on each side.</summary>
        public static bool Covers(Rect outer, Rect inner, float slack = 0.5f)
        {
            return outer.X <= inner.X + slack && outer.Y <= inner.Y + slack && outer.Right >= inner.Right - slack && outer.Bottom >= inner.Bottom - slack;
        }
    }
}
