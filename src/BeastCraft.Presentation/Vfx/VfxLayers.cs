using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Vfx
{
    /// <summary>
    /// One sprite a layer (or an aura) draws at one moment: which sheet and frame, where (board
    /// pixels), how big, turned how far, how opaque, tinted how, blended how, and whether it lies on
    /// the ground under the units. The renderer draws it and nothing else; it is never fed back.
    /// </summary>
    public readonly struct VfxSprite
    {
        public VfxSprite(string sheet, int frame, Vec2 position, float scale, float sizePx, float rotation, float alpha, string tint, bool additive, bool ground)
        {
            Sheet = sheet;
            Frame = frame;
            Position = position;
            Scale = scale;
            SizePx = sizePx;
            Rotation = rotation;
            Alpha = alpha;
            Tint = tint;
            Additive = additive;
            Ground = ground;
        }

        /// <summary>A sprite Name in the art manifest.</summary>
        public string Sheet { get; }

        /// <summary>The frame of the sheet (the renderer wraps it to the sheet's frame count).</summary>
        public int Frame { get; }

        /// <summary>Where its pivot goes, in board pixels.</summary>
        public Vec2 Position { get; }

        /// <summary>Draw scale (world units, as the manifest sizes the sheet), when <see cref="SizePx"/> is 0.</summary>
        public float Scale { get; }

        /// <summary>When above 0: draw it this many board pixels across instead (a ring or decal sized to an area).</summary>
        public float SizePx { get; }

        /// <summary>Radians, clockwise on screen.</summary>
        public float Rotation { get; }

        /// <summary>0-1.</summary>
        public float Alpha { get; }

        /// <summary>A palette char, or null/empty for the sheet's own colours.</summary>
        public string Tint { get; }

        public bool Additive { get; }

        /// <summary>Drawn under the units (a decal, a ground aura) rather than over them.</summary>
        public bool Ground { get; }
    }

    /// <summary>
    /// The hexes a skill affected, as one circle on the board: the mean of their centres and the
    /// distance from it to the farthest one plus half a hex, so a ring of that radius just encloses
    /// every affected tile. What an area-anchored layer centres on and a shockwave grows to.
    /// </summary>
    public sealed class VfxArea
    {
        public VfxArea(Vec2 center, float radius)
        {
            Center = center;
            Radius = radius;
        }

        /// <summary>The centre, in board pixels.</summary>
        public Vec2 Center { get; }

        /// <summary>The radius, in board pixels.</summary>
        public float Radius { get; }

        /// <summary>The radius in hexes (hex column steps).</summary>
        public float RadiusHexes
        {
            get { return Radius / HexLayout.ColumnStep; }
        }

        /// <summary>The circle around tile centres <paramref name="centers"/> (a half-hex circle at the origin for none).</summary>
        public static VfxArea Of(IEnumerable<Vec2> centers)
        {
            List<Vec2> points = new List<Vec2>(centers ?? new Vec2[0]);
            if (points.Count == 0)
            {
                return new VfxArea(Vec2.Zero, HexLayout.ColumnStep / 2f);
            }

            float x = 0f;
            float y = 0f;
            foreach (Vec2 point in points)
            {
                x += point.X;
                y += point.Y;
            }

            Vec2 center = new Vec2(x / points.Count, y / points.Count);
            float farthest = 0f;
            foreach (Vec2 point in points)
            {
                float dx = point.X - center.X;
                float dy = point.Y - center.Y;
                farthest = Math.Max(farthest, (float)Math.Sqrt(dx * dx + dy * dy));
            }

            return new VfxArea(center, farthest + HexLayout.ColumnStep / 2f);
        }

        /// <summary>The circle around <paramref name="tiles"/> laid out by <paramref name="layout"/>.</summary>
        public static VfxArea OfTiles(IEnumerable<HexCoordinate> tiles, HexLayout layout)
        {
            List<Vec2> centers = new List<Vec2>();
            HashSet<HexCoordinate> seen = new HashSet<HexCoordinate>();
            foreach (HexCoordinate tile in tiles ?? new HexCoordinate[0])
            {
                if (seen.Add(tile))
                {
                    centers.Add(layout.Center(tile));
                }
            }

            return Of(centers);
        }
    }

    /// <summary>
    /// A lasting status's look on its unit (<see cref="VfxAuraData"/>) at one moment: the aura sprite
    /// breathing on its pulse (scale and opacity), at the unit's feet or over it. Pure: the same
    /// aura, place and time give the same sprite.
    /// </summary>
    public static class VfxAuraSampler
    {
        /// <summary>How far above the feet an <c>Over</c> aura sits, per unit of draw scale (board px).</summary>
        public const float OverLift = 14f;

        /// <summary>
        /// The aura sprite of <paramref name="aura"/> on a unit whose feet are at
        /// <paramref name="feet"/>, drawn at <paramref name="unitScale"/> (2 for a large unit),
        /// <paramref name="ms"/> into the viewer's clock. False when the aura has no sprite.
        /// </summary>
        public static bool TrySample(VfxAuraData aura, Vec2 feet, float unitScale, int ms, out VfxSprite sprite)
        {
            sprite = default;
            if (aura == null || string.IsNullOrEmpty(aura.Sheet))
            {
                return false;
            }

            int pulse = Math.Max(1, aura.PulseMs);
            double phase = (Math.Max(0, ms) % pulse) / (double)pulse;
            float wave = (float)Math.Sin(phase * Math.PI * 2.0);
            float scale = aura.Scale * unitScale * (0.92f + 0.08f * wave);
            float alpha = 0.7f + 0.3f * wave;
            bool ground = aura.Depth != VfxDepth.Over;
            Vec2 at = ground ? feet : new Vec2(feet.X, feet.Y - OverLift * unitScale);
            sprite = new VfxSprite(aura.Sheet, 0, at, scale, 0f, 0f, alpha, aura.Tint, aura.Blend == VfxBlend.Additive, ground);
            return true;
        }
    }
}
