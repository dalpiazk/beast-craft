using System;

namespace BeastCraft.Presentation.Layout
{
    /// <summary>
    /// Where a popup goes for something it explains (a tapped glossary term): it never covers the
    /// term (<c>anchor</c>) or the block the term sits in (<c>keepClear</c>, e.g. the description).
    /// It goes below both, a <c>gap</c> away, when it fits inside <c>bounds</c>; otherwise it flips
    /// above them; when neither side has room it takes the roomier side and is clamped into the
    /// bounds (<see cref="Fits"/> false: it overlaps the block, never more than it must).
    /// Horizontally it is centred on the anchor and clamped into the bounds. <see cref="PointerX"/>
    /// is where its small pointer toward the anchor goes, on the edge facing the anchor. Pure maths.
    /// </summary>
    public readonly struct PopupPlacement
    {
        /// <summary>The default gap between the popup and what it keeps clear of (room for the pointer).</summary>
        public const float DefaultGap = 16f;

        /// <summary>Half the pointer's width: the pointer stays this far inside the popup's corners.</summary>
        public const float PointerHalfWidth = 12f;

        public PopupPlacement(Rect rect, bool below, bool fits, float pointerX)
        {
            Rect = rect;
            Below = below;
            Fits = fits;
            PointerX = pointerX;
        }

        public Rect Rect { get; }

        /// <summary>True when the popup sits below the anchor (its pointer on its top edge), false above (pointer on its bottom edge).</summary>
        public bool Below { get; }

        /// <summary>Whether it fits without covering the anchor or the kept-clear block.</summary>
        public bool Fits { get; }

        /// <summary>The pointer's x: the anchor's centre, kept within the popup's edge.</summary>
        public float PointerX { get; }

        /// <summary>
        /// Places a <paramref name="width"/> x <paramref name="height"/> popup for
        /// <paramref name="anchor"/>, keeping clear of <paramref name="keepClear"/> (which may be
        /// empty) and inside <paramref name="bounds"/>. A popup larger than the bounds is shrunk to them.
        /// </summary>
        public static PopupPlacement Place(Rect anchor, Rect keepClear, float width, float height, Rect bounds, float gap = DefaultGap)
        {
            width = Math.Max(0f, Math.Min(width, bounds.Width));
            height = Math.Max(0f, Math.Min(height, bounds.Height));

            Rect clear = keepClear.Width > 0f && keepClear.Height > 0f ? Union(anchor, keepClear) : anchor;
            float x = Clamp(anchor.Center.X - width / 2f, bounds.X, bounds.Right - width);

            float belowY = clear.Bottom + gap;
            float aboveY = clear.Y - gap - height;
            bool below;
            bool fits;
            float y;
            if (belowY + height <= bounds.Bottom)
            {
                below = true;
                fits = true;
                y = belowY;
            }
            else if (aboveY >= bounds.Y)
            {
                below = false;
                fits = true;
                y = aboveY;
            }
            else
            {
                // Neither side has room: the roomier one, clamped (overlapping the block, not beyond the bounds).
                below = bounds.Bottom - clear.Bottom >= clear.Y - bounds.Y;
                fits = false;
                y = Clamp(below ? belowY : aboveY, bounds.Y, bounds.Bottom - height);
            }

            Rect rect = new Rect(x, y, width, height);
            float pointer = Clamp(anchor.Center.X, rect.X + PointerHalfWidth + 8f, rect.Right - PointerHalfWidth - 8f);
            return new PopupPlacement(rect, below, fits, pointer);
        }

        /// <summary>The smallest rectangle holding both.</summary>
        public static Rect Union(Rect a, Rect b)
        {
            float x = Math.Min(a.X, b.X);
            float y = Math.Min(a.Y, b.Y);
            return new Rect(x, y, Math.Max(a.Right, b.Right) - x, Math.Max(a.Bottom, b.Bottom) - y);
        }

        /// <summary>Whether the two rectangles overlap (touching edges do not).</summary>
        public static bool Overlaps(Rect a, Rect b)
        {
            return a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
        }

        private static float Clamp(float value, float min, float max)
        {
            return max < min ? min : Math.Max(min, Math.Min(max, value));
        }
    }
}
