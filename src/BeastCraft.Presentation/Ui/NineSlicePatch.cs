using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Layout;

namespace BeastCraft.Presentation.Ui
{
    /// <summary>One source/destination rectangle pair of a nine-slice patch (both engine-neutral: the source is texture pixels, the destination is <see cref="Rect"/>'s space).</summary>
    public readonly struct NineSliceCell
    {
        public NineSliceCell(Rect source, Rect dest)
        {
            Source = source;
            Dest = dest;
        }

        public Rect Source { get; }

        public Rect Dest { get; }
    }

    /// <summary>
    /// The nine-slice math (engine-neutral, unit-tested in isolation from the renderer): splits a
    /// <paramref name="frameWidth"/> x <paramref name="frameHeight"/> source frame by its four insets
    /// into nine source/dest cells that stretch a <see cref="Rect"/> without distorting its painted
    /// edges — the four corner cells never scale (drawn, or cropped, at their own native pixel size,
    /// so a rounded or deckled edge stays crisp), the four edge cells stretch along one axis, the
    /// centre both.
    /// <para>
    /// An inset pair that would overlap in <c>box</c> (<c>Left+Right &gt; box.Width</c>, or the same
    /// for Top/Bottom) is scaled down together, proportionally, so the two insets meet exactly at the
    /// box's centre line instead of overlapping — kept equal on the source side too, so a corner is
    /// always drawn 1:1 (less of it is sampled, rather than it being squashed). Left=Right=0 (or
    /// Top=Bottom=0) skips splitting that axis: a 3-slice pill (a slider rail, a toggle track) whose
    /// rounded ends are the frame's own full height, stretched only along its length.
    /// </para>
    /// <para>Every one of the 9 cells is always returned, left-to-right then top-to-bottom (index 4 is the centre); a cell with a zero-size destination draws nothing (the painter's own no-op), so a caller may skip or draw all nine uniformly.</para>
    /// </summary>
    public static class NineSlicePatch
    {
        public static IReadOnlyList<NineSliceCell> Build(float frameWidth, float frameHeight, float left, float top, float right, float bottom, Rect box)
        {
            float l = Math.Max(0f, left);
            float t = Math.Max(0f, top);
            float r = Math.Max(0f, right);
            float b = Math.Max(0f, bottom);

            // Never read past the frame itself (defensive: the manifest is validated, but a caller
            // building insets by hand, as the tests do, should not be able to crash the math).
            ClampPair(ref l, ref r, frameWidth);
            ClampPair(ref t, ref b, frameHeight);

            // Then clamp against the destination box, the same way: proportionally, symmetrically,
            // and used for BOTH the source and dest corner sizes, so a corner is always 1:1 (cropped,
            // never stretched) even when the box is smaller than the art's own corners.
            ClampPair(ref l, ref r, box.Width);
            ClampPair(ref t, ref b, box.Height);

            float[] srcX = { 0f, l, frameWidth - r, frameWidth };
            float[] srcY = { 0f, t, frameHeight - b, frameHeight };
            float[] dstX = { box.X, box.X + l, box.Right - r, box.Right };
            float[] dstY = { box.Y, box.Y + t, box.Bottom - b, box.Bottom };

            List<NineSliceCell> cells = new List<NineSliceCell>(9);
            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    Rect source = new Rect(srcX[col], srcY[row], Math.Max(0f, srcX[col + 1] - srcX[col]), Math.Max(0f, srcY[row + 1] - srcY[row]));
                    Rect dest = new Rect(dstX[col], dstY[row], Math.Max(0f, dstX[col + 1] - dstX[col]), Math.Max(0f, dstY[row + 1] - dstY[row]));
                    cells.Add(new NineSliceCell(source, dest));
                }
            }

            return cells;
        }

        /// <summary>Scales <paramref name="a"/> and <paramref name="b"/> down together (same ratio) so they sum to at most <paramref name="limit"/>; left alone when they already fit.</summary>
        private static void ClampPair(ref float a, ref float b, float limit)
        {
            float sum = a + b;
            if (sum > limit && sum > 0f)
            {
                float k = Math.Max(0f, limit) / sum;
                a *= k;
                b *= k;
            }
        }
    }
}
