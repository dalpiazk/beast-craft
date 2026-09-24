using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Rendering
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// A <see cref="SpriteBatch"/> that follows the art: it keeps one batch open while the blend
    /// state, sampler and transform stay the same, and restarts it when a draw needs another
    /// (a point-filtered pixel sprite after a linear illustrated one, an additive glow after an
    /// alpha sprite). Sprites are placed by their manifest pivot and sized in world units
    /// (<see cref="UnitSize"/>, the pixels of one unit in the current transform's space) from
    /// their PixelsPerUnit, so a 32 px placeholder and a 512 px illustration of the same beast
    /// draw the same size.
    /// </summary>
    public sealed class SpriteRenderer
    {
        private readonly SpriteBatch _batch;
        private bool _open;
        private BlendState _blend = BlendState.AlphaBlend;
        private SamplerState _sampler = SamplerState.PointClamp;
        private Matrix _transform = Matrix.Identity;
        private BlendState _openBlend;
        private SamplerState _openSampler;
        private Matrix _openTransform;

        public SpriteRenderer(SpriteBatch batch)
        {
            _batch = batch;
        }

        /// <summary>Pixels of one world unit (one hex column step) in the current transform's space.</summary>
        public float UnitSize { get; set; } = 32f;

        /// <summary>How many batches were begun since <see cref="ResetStats"/> (a draw-call proxy).</summary>
        public int Batches { get; private set; }

        public void ResetStats()
        {
            Batches = 0;
        }

        /// <summary>The transform every later draw uses (until the next call).</summary>
        public void SetTransform(Matrix transform)
        {
            _transform = transform;
        }

        /// <summary>The blend every later draw uses: alpha (the default) or additive.</summary>
        public void SetBlend(BlendState blend)
        {
            _blend = blend ?? BlendState.AlphaBlend;
        }

        /// <summary>The open batch for <paramref name="sampler"/> under the current blend and transform, for callers that draw themselves.</summary>
        public SpriteBatch Batch(SamplerState sampler)
        {
            Ensure(sampler);
            return _batch;
        }

        /// <summary>Ends the open batch, if any (call before switching render targets or presenting).</summary>
        public void Flush()
        {
            if (_open)
            {
                _batch.End();
                _open = false;
            }
        }

        /// <summary>
        /// Draws frame <paramref name="frame"/> of <paramref name="sprite"/> with its pivot at
        /// <paramref name="at"/>, <paramref name="scale"/> world units per unit of its natural size
        /// (1 = as the manifest sizes it), mirrored when <paramref name="flipX"/>, multiplied by the
        /// sprite's own tint and <paramref name="color"/>.
        /// </summary>
        public void DrawSprite(ArtSprite sprite, int frame, Vector2 at, float scale, Color color, bool flipX = false, float rotation = 0f)
        {
            if (sprite == null)
            {
                return;
            }

            DrawFrame(sprite, sprite.Texture, sprite.Frame(frame), at, scale, color, flipX, rotation);
        }

        /// <summary>The same as <see cref="DrawSprite"/> for a clip frame that lives on another sheet (<paramref name="sheet"/>) but is placed as <paramref name="sprite"/>.</summary>
        public void DrawFrame(ArtSprite sprite, Texture2D texture, Rectangle source, Vector2 at, float scale, Color color, bool flipX, float rotation)
        {
            Ensure(sprite.Sampler);
            float ppu = sprite.Data.PixelsPerUnit > 0f ? sprite.Data.PixelsPerUnit : UnitSize;
            float k = UnitSize / ppu * scale;
            Vector2 origin = new Vector2(flipX ? sprite.Data.FrameWidth - sprite.Pivot.X : sprite.Pivot.X, sprite.Pivot.Y);
            Color tint = Multiply(sprite.Tint, color);
            _batch.Draw(texture, at, source, tint, rotation, origin, k, flipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        /// <summary>A solid rectangle (the atlas's 1x1 pixel stretched).</summary>
        public void Fill(Texture2D pixel, Rectangle rectangle, Color color)
        {
            Ensure(SamplerState.PointClamp);
            _batch.Draw(pixel, rectangle, color);
        }

        /// <summary>A solid rectangle with float corners (for sizes that do not land on whole pixels).</summary>
        public void Fill(Texture2D pixel, Vector2 topLeft, Vector2 size, Color color)
        {
            Ensure(SamplerState.PointClamp);
            _batch.Draw(pixel, topLeft, null, color, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);
        }

        private void Ensure(SamplerState sampler)
        {
            if (_open && _openBlend == _blend && _openSampler == sampler && _openTransform == _transform)
            {
                return;
            }

            Flush();
            _batch.Begin(SpriteSortMode.Deferred, _blend, sampler, null, RasterizerState.CullNone, null, _transform);
            _open = true;
            _openBlend = _blend;
            _openSampler = sampler;
            _openTransform = _transform;
            Batches++;
        }

        private static Color Multiply(Color a, Color b)
        {
            return new Color(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, a.A * b.A / 255);
        }

        /// <summary>A world-units draw scale for a sprite of <paramref name="sprite"/>'s size to cover <paramref name="units"/> units of width.</summary>
        public static float ScaleToWidth(ArtSprite sprite, float units)
        {
            if (sprite == null || sprite.Data.FrameWidth <= 0)
            {
                return 1f;
            }

            float natural = sprite.Data.FrameWidth / Math.Max(1f, sprite.Data.PixelsPerUnit);
            return units / natural;
        }
    }
}
