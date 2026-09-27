using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Art;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>The frame, and the board half of it: tiles, units and VFX, in board space.</summary>
    public sealed partial class BattleViewerGame
    {
        /// <summary>Board pixels the plate under the tiles reaches past their box (<see cref="DrawBoard"/>).</summary>
        private const float BoardPlateMargin = PortraitLayout.BoardEdgeMargin;

        /// <summary>The plate under the tiles: a dark soil the tiles' outlines sit on, so the arena's edge is a clean rectangle.</summary>
        private static readonly Color BoardPlate = new Color(0x2b, 0x22, 0x1c);

        /// <summary>The dimming of the off-board half tiles that square off the zigzag sides.</summary>
        private static readonly Color NotchTint = new Color(120, 120, 120);

        /// <summary>Every tile edge of the arena once (<see cref="HexGridLines"/>), made on first use.</summary>
        private List<Segment> _gridLines;

        /// <summary>The anti-aliased hex mask the zone and footprint tints use over a backdrop (<see cref="SoftHex"/>).</summary>
        private Texture2D _softHex;

        /// <summary>
        /// Draws one frame onto a <paramref name="width"/> x <paramref name="height"/> target: the
        /// portrait canvas fitted inside <paramref name="insets"/> (black bars around it), the board
        /// through the auto camera's fit (<see cref="CameraRig.Fit"/>, shaken by the VFX) and
        /// the HUD in canvas pixels.
        /// </summary>
        private void RenderScene(int width, int height, SafeInsets insets)
        {
            _canvasFit = CanvasFit.Of(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight, width, height, insets);
            GraphicsDevice.Clear(Color.Black);
            Matrix canvas = Matrix.CreateScale(_canvasFit.Scale) * Matrix.CreateTranslation(_canvasFit.OffsetX, _canvasFit.OffsetY, 0f);

            _draw.ResetStats();
            _draw.SetBlend(BlendState.AlphaBlend);
            _draw.SetTransform(canvas);
            _draw.UnitSize = HexLayout.ColumnStep;
            _draw.Fill(Pixel, new Rectangle(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Ink("K", new Color(0x1c, 0x14, 0x28)));

            if (FailureMessage != null || _playback == null)
            {
                DrawFailure();
                _draw.Flush();
                return;
            }

            ScheduledBeat beat = _animation == null ? null : _animation.BeatAt(_clockMs);
            VfxFrame vfx = beat == null ? null : beat.Timeline.Sample(_clockMs - beat.StartMs);
            List<(VfxTimeline Timeline, VfxFrame Frame)> frames = BeatFrames(beat, vfx);
            Vec2 shake = Vec2.Zero;
            foreach ((VfxTimeline _, VfxFrame frame) in frames)
            {
                shake = shake + frame.Shake;
            }

            Rect board = _screen.Board;
            BattleBackdropData backdrop = _content.BattleArt?.Backdrop(_regionId, _playback.Grid.Size);
            ArtSprite backdropArt = backdrop == null ? null : _atlas.ByArtKey(backdrop.ArtKey);
            if (backdropArt == null)
            {
                _draw.Fill(Pixel, new Vector2(board.X, board.Y), new Vector2(board.Width, board.Height), Ink("p", Color.Purple) * 0.35f);
            }

            // The auto camera (CameraRig / TurnCamera): the board zoomed and panned onto the action, clipped to its area.
            _boardFit = _camera.Fit(CameraNow());
            Matrix boardSpace = Matrix.CreateTranslation(shake.X, shake.Y, 0f) * Matrix.CreateScale(_boardFit.Scale) *
                                Matrix.CreateTranslation(_boardFit.OriginX, _boardFit.OriginY, 0f) * canvas;
            if (backdropArt != null)
            {
                DrawBackdrop(backdrop, backdropArt, boardSpace, canvas);
            }

            Rect clip = _canvasFit.ToScreen(board);
            _draw.SetClip(new Rectangle((int)Math.Floor(clip.X), (int)Math.Floor(clip.Y), (int)Math.Ceiling(clip.Width), (int)Math.Ceiling(clip.Height)));
            _draw.SetTransform(boardSpace);
            if (backdropArt != null)
            {
                DrawBoardOverlay(_content.BattleArt.Board ?? new BoardOverlayData());
            }
            else
            {
                DrawBoard(boardSpace, clip);
            }

            DrawUnits(frames, backdropArt != null);
            foreach ((VfxTimeline timeline, VfxFrame frame) in frames)
            {
                DrawVfx(timeline, frame, false);
                _draw.SetBlend(BlendState.Additive);
                DrawVfx(timeline, frame, true);
                _draw.SetBlend(BlendState.AlphaBlend);
            }

            DrawLayerSprites(frames, false);
            if (vfx != null)
            {
                _draw.SetBlend(BlendState.Additive);
                DrawFlash(beat, vfx);
                _draw.SetBlend(BlendState.AlphaBlend);
                DrawDamageNumbers(beat, vfx);
            }

            _draw.SetClip(null);
            _draw.SetTransform(canvas);
            _draw.UnitSize = HexLayout.ColumnStep;
            DrawHud(beat);
            _draw.Flush();
        }

        private void DrawFailure()
        {
            float y = 200f;
            foreach (string line in (FailureMessage ?? "NO BATTLE").Split('\n'))
            {
                _text.Draw(_draw, _text.Fit(line, 15f, PortraitLayout.CanvasWidth - 2f * PortraitLayout.Margin), new Vector2(PortraitLayout.Margin, y), 15f,
                           Color.White);
                y += _text.LineHeight(15f);
            }
        }

        /// <summary>
        /// A painted backdrop (<see cref="BattleBackdropData"/>): the whole image through the board
        /// transform, placed so its board rect lies on the arena's tiles
        /// (<see cref="BackdropPlacement.ImageRect"/>), so it zooms, pans and shakes with the board;
        /// clipped to the canvas, not the board band, so its margins run under the HUD to the canvas
        /// edges. Then, in canvas space, a soft dark scrim over the HUD bands above and below the
        /// board so their text reads over the painting.
        /// </summary>
        private void DrawBackdrop(BattleBackdropData backdrop, ArtSprite art, Matrix boardSpace, Matrix canvas)
        {
            Rect screen = _canvasFit.ToScreen(_screen.Canvas);
            _draw.SetClip(new Rectangle((int)Math.Floor(screen.X), (int)Math.Floor(screen.Y), (int)Math.Ceiling(screen.Width), (int)Math.Ceiling(screen.Height)));
            _draw.SetTransform(boardSpace);
            Rect image = BackdropPlacement.ImageRect(backdrop, _playback.Grid.Width, _playback.Grid.Height);
            _draw.DrawStretched(art, new Vector2(image.X, image.Y), new Vector2(image.Width, image.Height), Color.White);

            _draw.SetTransform(canvas);
            float alpha = (_content.BattleArt.Board ?? new BoardOverlayData()).HudScrimAlpha;
            Rect board = _screen.Board;
            Color scrim = Ink("K", Color.Black);
            const float fade = 48f;
            const int steps = 12;
            _draw.Fill(Pixel, new Vector2(0f, 0f), new Vector2(PortraitLayout.CanvasWidth, board.Y - fade / 2f), scrim * alpha);
            _draw.Fill(Pixel, new Vector2(0f, board.Bottom + fade / 2f), new Vector2(PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - board.Bottom - fade / 2f),
                       scrim * alpha);
            for (int i = 0; i < steps; i++)
            {
                float k = alpha * (steps - i - 0.5f) / steps;
                float step = fade / steps;
                _draw.Fill(Pixel, new Vector2(0f, board.Y - fade / 2f + i * step), new Vector2(PortraitLayout.CanvasWidth, step), scrim * k);
                _draw.Fill(Pixel, new Vector2(0f, board.Bottom + fade / 2f - (i + 1) * step), new Vector2(PortraitLayout.CanvasWidth, step), scrim * k);
            }
        }

        /// <summary>
        /// Over a painted backdrop, in board space: the soft hex grid (every tile edge once,
        /// <see cref="HexGridLines"/>, a constant thickness in canvas pixels whatever the zoom), then
        /// the two deployment zones' tints on their tiles. Styling is data (<see cref="BoardOverlayData"/>).
        /// </summary>
        private void DrawBoardOverlay(BoardOverlayData style)
        {
            HexGrid grid = _playback.Grid;
            if (style.GridAlpha > 0f)
            {
                _gridLines ??= HexGridLines.Of(grid.Tiles, _layout);
                float thickness = style.GridWidth / Math.Max(0.0001f, _boardFit.Scale);
                Color line = Ink(style.GridColor, Color.White) * style.GridAlpha;
                foreach (Segment segment in _gridLines)
                {
                    _draw.Line(Pixel, new Vector2(segment.From.X, segment.From.Y), new Vector2(segment.To.X, segment.To.Y), thickness, line);
                }
            }

            Color player = Ink(style.PlayerZoneColor, Color.Blue) * style.PlayerZoneAlpha;
            Color enemy = Ink(style.EnemyZoneColor, Color.Red) * style.EnemyZoneAlpha;
            foreach (HexCoordinate tile in grid.Tiles)
            {
                bool mine = grid.IsInDeploymentZone(tile, BattleTeam.Player);
                bool theirs = grid.IsInDeploymentZone(tile, BattleTeam.Enemy);
                if (mine || theirs)
                {
                    DrawSoftHex(_layout.Center(tile), mine ? player : enemy);
                }
            }
        }

        /// <summary>A tile-sized, anti-aliased hex (<see cref="SoftHex"/>) on <paramref name="center"/> in <paramref name="color"/> (premultiplied).</summary>
        private void DrawSoftHex(Vec2 center, Color color)
        {
            _softHex ??= SoftHex.Create(GraphicsDevice);
            _draw.DrawCentered(_softHex, new Vector2(center.X, center.Y), new Vector2(HexLayout.TileWidth, HexLayout.TileHeight), color);
        }

        /// <summary>A tile's six edges as lines <paramref name="width"/> canvas pixels thick (the acting unit's outline over a backdrop).</summary>
        private void DrawHexOutline(Vec2 center, float width, Color color)
        {
            Vec2[] corners = HexGridLines.Corners(new HexLayout(0, 0), HexCoordinate.Zero);
            float thickness = width / Math.Max(0.0001f, _boardFit.Scale);
            for (int i = 0; i < corners.Length; i++)
            {
                Vec2 a = corners[i];
                Vec2 b = corners[(i + 1) % corners.Length];
                _draw.Line(Pixel, new Vector2(center.X + a.X, center.Y + a.Y), new Vector2(center.X + b.X, center.Y + b.Y), thickness, color);
            }
        }

        /// <summary>
        /// The arena: a rectangle. Under the tiles, a plate a few pixels bigger than their box
        /// (<see cref="HexLayout.BoardBounds"/>) in a dark soil colour, so the pointed tops
        /// and bottoms of the end rows sit on a straight edge; the tiles; then, clipped to the box, a
        /// dimmed half tile in each side notch (<see cref="HexLayout.EdgeNotches"/>), so the zigzag
        /// sides read as straight walls of the same ground rather than a ragged edge. The dimmed
        /// halves are off the board: nothing ever stands there.
        /// </summary>
        private void DrawBoard(Matrix boardSpace, Rect areaClip)
        {
            HexGrid grid = _playback.Grid;
            ArtSprite grass = _atlas.Sprite("hex_grass");
            ArtSprite rock = _atlas.Sprite("hex_scorched");
            Rect box = HexLayout.BoardBounds(grid.Width, grid.Height);

            _draw.Fill(Pixel, new Vector2(box.X - BoardPlateMargin, box.Y - BoardPlateMargin),
                       new Vector2(box.Width + 2f * BoardPlateMargin, box.Height + 2f * BoardPlateMargin), BoardPlate);

            foreach (HexCoordinate tile in grid.Tiles)
            {
                ArtSprite sprite = grid.IsInDeploymentZone(tile, BattleTeam.Enemy) ? rock : grass;
                _draw.DrawSprite(sprite, 0, At(_layout.Center(tile)), 1f, Color.White);
            }

            // The notch fillers, clipped to the tiles' box (in render-target pixels, inside the board area's clip).
            Vector2 topLeft = Vector2.Transform(new Vector2(box.X, box.Y), boardSpace);
            Vector2 bottomRight = Vector2.Transform(new Vector2(box.Right, box.Bottom), boardSpace);
            float left = Math.Max(topLeft.X, areaClip.X);
            float top = Math.Max(topLeft.Y, areaClip.Y);
            float right = Math.Min(bottomRight.X, areaClip.Right);
            float bottom = Math.Min(bottomRight.Y, areaClip.Bottom);
            if (right > left && bottom > top)
            {
                _draw.SetClip(new Rectangle((int)Math.Ceiling(left), (int)Math.Ceiling(top), (int)Math.Floor(right) - (int)Math.Ceiling(left),
                                            (int)Math.Floor(bottom) - (int)Math.Ceiling(top)));
                foreach (HexCoordinate notch in HexLayout.EdgeNotches(grid.Width, grid.Height))
                {
                    bool enemySide = notch.R <= grid.MinRow + grid.DeploymentZoneDepth - 1;
                    _draw.DrawSprite(enemySide ? rock : grass, 0, At(_layout.Center(notch)), 1f, NotchTint);
                }
            }

            _draw.SetClip(new Rectangle((int)Math.Floor(areaClip.X), (int)Math.Floor(areaClip.Y), (int)Math.Ceiling(areaClip.Width), (int)Math.Ceiling(areaClip.Height)));
        }

        /// <summary>The beat's main frame and each of its on-apply overlays' frames at the viewer's clock (empty with no beat).</summary>
        private List<(VfxTimeline Timeline, VfxFrame Frame)> BeatFrames(ScheduledBeat beat, VfxFrame main)
        {
            List<(VfxTimeline Timeline, VfxFrame Frame)> frames = new List<(VfxTimeline Timeline, VfxFrame Frame)>();
            if (beat == null)
            {
                return frames;
            }

            frames.Add((beat.Timeline, main));
            foreach (BeatOverlay overlay in beat.Overlays)
            {
                frames.Add((overlay.Timeline, overlay.Timeline.Sample(_clockMs - beat.StartMs - overlay.OffsetMs)));
            }

            return frames;
        }

        /// <summary>The layer sprites of every frame on the ground (<paramref name="ground"/>) or over the units.</summary>
        private void DrawLayerSprites(List<(VfxTimeline Timeline, VfxFrame Frame)> frames, bool ground)
        {
            foreach ((VfxTimeline _, VfxFrame frame) in frames)
            {
                foreach (VfxSprite sprite in frame.Sprites)
                {
                    if (sprite.Ground == ground)
                    {
                        DrawVfxSprite(sprite);
                    }
                }
            }

            _draw.SetBlend(BlendState.AlphaBlend);
        }

        /// <summary>
        /// One layer or aura sprite: its frame (wrapped to the sheet), at its size (a ring or decal
        /// spans <see cref="VfxSprite.SizePx"/> board pixels, whatever the texture's resolution) or
        /// scale, turned, tinted, faded, in its blend.
        /// </summary>
        private void DrawVfxSprite(VfxSprite sprite)
        {
            ArtSprite art = _atlas.Sprite(sprite.Sheet);
            if (art == null || sprite.Alpha <= 0f)
            {
                return;
            }

            float scale = sprite.Scale;
            if (sprite.SizePx > 0f)
            {
                float natural = art.Data.FrameWidth / Math.Max(1f, art.Data.PixelsPerUnit) * _draw.UnitSize;
                scale = sprite.SizePx / Math.Max(1f, natural);
            }

            _draw.SetBlend(sprite.Additive ? BlendState.Additive : BlendState.AlphaBlend);
            int frame = art.Data.Frames <= 0 ? 0 : ((sprite.Frame % art.Data.Frames) + art.Data.Frames) % art.Data.Frames;
            _draw.DrawSprite(art, frame, new Vector2(sprite.Position.X, sprite.Position.Y), scale, Ink(sprite.Tint, Color.White) * sprite.Alpha, false,
                             sprite.Rotation);
        }

        /// <summary>The lasting effect keys a unit shows now (its statuses and stat changes), as the turn animation has them.</summary>
        private List<string> StatusKeys(UnitSnapshot unit)
        {
            return _animation != null ? _animation.ShownStatusKeys(unit.Id, _clockMs) : unit.StatusKeys();
        }

        /// <summary>The aura sprites of <paramref name="unit"/>'s lasting effects, on the ground or over it.</summary>
        private void DrawAuras(UnitSnapshot unit, Vector2 feet, bool ground)
        {
            foreach (string key in StatusKeys(unit))
            {
                if (VfxAuraSampler.TrySample(_content.Vfx.Aura(key), new Vec2(feet.X, feet.Y), HexLayout.FootprintWidth(unit.Footprint), _clockMs + _idleClockMs,
                                             out VfxSprite sprite) &&
                    sprite.Ground == ground)
                {
                    DrawVfxSprite(sprite);
                }
            }

            _draw.SetBlend(BlendState.AlphaBlend);
        }

        /// <summary>
        /// The Gloam haze under a standing enemy (the VFX library's <c>EnemyAura</c>), sized to its
        /// footprint, each enemy on its own phase so a crowd does not breathe in step. Thinned under
        /// Reduced effects, gone under Minimal. Presentation only.
        /// </summary>
        private void DrawEnemyHaze(UnitSnapshot unit, Vector2 feet)
        {
            int phase = 0;
            foreach (char c in unit.Id ?? string.Empty)
            {
                phase = (phase * 31 + c) % 100003;
            }

            if (unit.Team == BattleTeam.Enemy &&
                VfxAuraSampler.TrySampleEnemyAura(_content.Vfx.EnemyAura, new Vec2(feet.X, feet.Y), HexLayout.FootprintWidth(unit.Footprint),
                                                  _clockMs + _idleClockMs + phase, _vfxSettings, out VfxSprite sprite))
            {
                DrawVfxSprite(sprite);
                _draw.SetBlend(BlendState.AlphaBlend);
            }
        }

        /// <summary>The status icons of <paramref name="unit"/>'s lasting effects, in a row centred above its HP bar.</summary>
        private void DrawStatusIcons(UnitSnapshot unit, float centerX, float barY)
        {
            List<ArtSprite> icons = new List<ArtSprite>();
            List<string> tints = new List<string>();
            foreach (string key in StatusKeys(unit))
            {
                VfxAuraData aura = _content.Vfx.Aura(key);
                ArtSprite icon = aura == null ? null : _atlas.Sprite(aura.Icon);
                if (icon != null)
                {
                    icons.Add(icon);
                    tints.Add(aura.IconTint);
                }
            }

            const float step = 10f;
            float x = centerX - (icons.Count - 1) * step / 2f;
            for (int i = 0; i < icons.Count; i++)
            {
                _draw.DrawSprite(icons[i], 0, new Vector2((float)Math.Round(x + i * step), barY - 7f), 1f, Ink(tints[i], Color.White));
            }
        }

        /// <summary>
        /// The units, sorted back to front: their footprint tints (the pixel hex mask, or over a
        /// backdrop, <paramref name="painted"/>, the soft hex and a line outline for the one acting),
        /// the ground layers and auras, then each sprite with its auras, HP bar and status icons.
        /// </summary>
        private void DrawUnits(List<(VfxTimeline Timeline, VfxFrame Frame)> frames, bool painted)
        {
            IReadOnlyDictionary<string, UnitSnapshot> state = _animation != null ? _animation.Turn.After : _playback.Current;
            List<UnitSnapshot> units = new List<UnitSnapshot>(state.Values);
            units.Sort((a, b) => Center(a).Y.CompareTo(Center(b).Y));

            string actor = ActingUnit() == null ? null : ActingUnit().Id;
            ArtSprite mask = _atlas.Sprite("hex_mask");
            ArtSprite outline = _atlas.Sprite("hex_outline");

            // Footprints first, so every sprite stands on top of every tile tint.
            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Color team = TeamColor(unit.Team);
                foreach (HexCoordinate tile in Footprints.Tiles(Position(unit), unit.Footprint))
                {
                    if (painted)
                    {
                        DrawSoftHex(_layout.Center(tile), team * 0.3f);
                        if (unit.Id == actor)
                        {
                            DrawHexOutline(_layout.Center(tile), 3f, Ink("Y", Color.Yellow));
                        }

                        continue;
                    }

                    Vector2 at = At(_layout.Center(tile));
                    _draw.DrawSprite(mask, 0, at, 1f, team * 0.35f);
                    if (unit.Id == actor)
                    {
                        _draw.DrawSprite(outline, 0, at, 1f, Ink("Y", Color.Yellow));
                    }
                }
            }

            // Then everything that lies on the ground: decals and ground rings, the enemies' Gloam haze
            // and ground auras.
            DrawLayerSprites(frames, true);
            foreach (UnitSnapshot unit in units)
            {
                if (Standing(unit))
                {
                    DrawEnemyHaze(unit, At(Center(unit)));
                    DrawAuras(unit, At(Center(unit)), true);
                }
            }

            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Vector2 at = At(Center(unit));
                bool fading = _animation != null && _animation.ShownFading(unit.Id, _clockMs);
                ArtSprite sprite = SpriteFor(unit.Id);
                DrawUnitSprite(sprite, unit, at, fading ? Color.White * 0.4f : Color.White);
                DrawAuras(unit, at, false);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.Hp;
                float barY = (float)Math.Round(at.Y - HeadHeight(sprite, 1f)) - 4f;
                DrawHpBar(at.X, barY, unit.Footprint == UnitFootprint.Single ? 24f : 40f, 3f, hp, unit.MaxHp);
                DrawStatusIcons(unit, at.X, barY);
            }
        }

        private void DrawHpBar(float centerX, float y, float width, float height, int hp, int maxHp)
        {
            float x = centerX - width / 2f;
            float fraction = maxHp <= 0 ? 0f : Math.Max(0f, Math.Min(1f, hp / (float)maxHp));
            string fill = fraction > 0.5f ? "l" : fraction > 0.25f ? "y" : "o";
            _draw.Fill(Pixel, new Vector2(x - 1f, y - 1f), new Vector2(width + 2f, height + 2f), Ink("K", Color.Black));
            _draw.Fill(Pixel, new Vector2(x, y), new Vector2(width, height), Ink("1", Color.DarkGray));
            _draw.Fill(Pixel, new Vector2(x, y), new Vector2((float)Math.Ceiling(width * fraction), height), Ink(fill, Color.Green));
        }

        /// <summary>A timeline's schema-v1 parts (projectile, flipbook, particles) in one blend pass.</summary>
        private void DrawVfx(VfxTimeline timeline, VfxFrame vfx, bool additivePass)
        {
            VfxEffectData effect = timeline.Effect;

            VfxSpriteData projectile = effect.Projectile;
            if (projectile != null && projectile.Additive == additivePass)
            {
                foreach (Vec2 at in vfx.Projectiles)
                {
                    DrawCentered(projectile.Sheet, 0, at, projectile.Scale, Ink(projectile.Tint, Color.White));
                }
            }

            VfxFlipbookData flipbook = effect.Flipbook;
            if (flipbook != null && flipbook.Additive == additivePass && vfx.FlipbookFrame >= 0)
            {
                foreach (VfxTarget target in timeline.Targets)
                {
                    DrawCentered(flipbook.Sheet, vfx.FlipbookFrame, target.Position, flipbook.Scale, Ink(flipbook.Tint, Color.White));
                }
            }

            VfxParticleData particles = timeline.BurstSpec;
            if (particles != null && particles.Additive == additivePass)
            {
                foreach (ParticleState particle in vfx.Particles)
                {
                    string ch = particles.Colors.Length == 0 ? null : particles.Colors[particle.ColorIndex];
                    DrawCentered(particles.Sheet, 0, particle.Position, 1, Ink(ch, Color.White) * particle.Alpha);
                }
            }
        }

        private void DrawFlash(ScheduledBeat beat, VfxFrame vfx)
        {
            VfxFlashData flash = beat.Timeline.Effect.HitFlash;
            if (flash == null || vfx.FlashAlpha <= 0f)
            {
                return;
            }

            Color tint = Ink(flash.Tint, Color.White) * (vfx.FlashAlpha * 0.8f);
            foreach (BeatTarget target in beat.Beat.Targets)
            {
                if (_animation.Turn.After.TryGetValue(target.UnitId, out UnitSnapshot unit))
                {
                    DrawUnitSprite(SpriteFor(unit.Id), unit, At(Center(unit)), tint, false);
                }
            }
        }

        private void DrawDamageNumbers(ScheduledBeat beat, VfxFrame vfx)
        {
            VfxDamageNumberData spec = beat.Timeline.Effect.DamageNumber;
            if (spec == null)
            {
                return;
            }

            foreach (DamageNumberState number in vfx.DamageNumbers)
            {
                string ch = number.Crit && !string.IsNullOrEmpty(spec.CritColor) ? spec.CritColor : spec.Color;
                string text = number.Value.ToString(CultureInfo.InvariantCulture) + (number.Crit ? "!" : string.Empty);
                Color color = Ink(ch, Color.White) * number.Alpha;
                // Above the unit's head: a big unit's art is taller than a hex (its WorldHeight).
                bool big = _animation.Turn.After.TryGetValue(number.UnitId ?? string.Empty, out UnitSnapshot unit) && unit.Footprint != UnitFootprint.Single;
                float lift = big ? Math.Max(38f, HeadHeight(SpriteFor(unit.Id), 1f) + 2f) : 38f;
                _text.DrawCentered(_draw, text, (float)Math.Round(number.Position.X), (float)Math.Round(number.Position.Y) - lift, 10f, color,
                                   Ink("K", Color.Black) * number.Alpha);
            }
        }

        private void DrawCentered(string sheet, int frame, Vec2 at, float scale, Color color)
        {
            _draw.DrawSprite(_atlas.Sprite(sheet), frame, At(at), scale, color);
        }

        /// <summary>
        /// A unit's sprite, pivot (feet) on <paramref name="at"/> (its footprint's visual centre),
        /// facing the other side, at its art's own size (the manifest's PixelsPerUnit, from its
        /// WorldHeight: a multi-hex enemy's art is drawn to cover its footprint, not scaled up); a
        /// sprite with an <c>idle</c> clip plays it on the viewer's clock. With
        /// <paramref name="accent"/>, an enemy's accent overlay is drawn over it in its element's colour.
        /// </summary>
        private void DrawUnitSprite(ArtSprite sprite, UnitSnapshot unit, Vector2 at, Color color, bool accent = true)
        {
            DrawCharacter(sprite, at, 1f, color, unit.Team == BattleTeam.Enemy, accent ? AccentColor(sprite, unit.Element) : null);
        }

        /// <summary>
        /// A character sprite with its pivot on <paramref name="at"/>, playing its <c>idle</c> clip when
        /// it has one; then, when <paramref name="accent"/> is set and the sprite names an accent
        /// overlay (<see cref="ArtSpriteData.Accent"/>), the overlay in the same place multiplied by
        /// that colour (and by <paramref name="color"/>, so a fading unit's overlay fades with it).
        /// </summary>
        private void DrawCharacter(ArtSprite sprite, Vector2 at, float scale, Color color, bool flip, Color? accent = null)
        {
            if (sprite == null)
            {
                return;
            }

            ArtAnimationData idle = sprite.Data.Animation("idle");
            ArtSprite sheet = idle == null ? null : _atlas.Sprite(string.IsNullOrEmpty(idle.Sheet) ? sprite.Name : idle.Sheet);
            int frame = 0;
            if (sheet != null)
            {
                frame = idle.FrameAt(_clockMs + _idleClockMs);
                _draw.DrawFrame(sprite, sheet.Texture, sheet.Frame(frame), at, scale, color, flip, 0f);
            }
            else
            {
                _draw.DrawSprite(sprite, 0, at, scale, color, flip);
            }

            // The overlay shows the same frame as the base: the manifest validator holds an accent to its
            // base's frame count (a clip on another sheet has no overlay frames, so it keeps frame 0).
            ArtSprite overlay = accent.HasValue ? _atlas.Sprite(sprite.Data.Accent) : null;
            if (overlay != null)
            {
                System.Diagnostics.Debug.Assert(overlay.Data.Frames == sprite.Data.Frames, "an accent overlay has its base's frame count");
                bool ownFrames = sheet == null || sheet == sprite;
                int overlayFrame = ownFrames ? Math.Min(frame, Math.Max(0, overlay.Data.Frames - 1)) : 0;
                _draw.DrawSprite(overlay, overlayFrame, at, scale, new Color(accent.Value.ToVector4() * color.ToVector4()), flip);
            }
        }

        /// <summary>
        /// The colour an enemy sprite's accent overlay is drawn in for a unit of <paramref name="element"/>
        /// (data: <see cref="ArtManifestData.AccentTint"/>), or null for a sprite without one.
        /// </summary>
        private Color? AccentColor(ArtSprite sprite, Creatures.Element element)
        {
            string hex = sprite == null ? null : _content.Art.AccentTint(sprite.Data, element.ToString());
            return hex == null ? (Color?)null : SpriteAtlas.ParseHex(hex);
        }

        /// <summary>How far a sprite's art (from its top opaque row) drawn at <paramref name="scale"/> reaches above its pivot, in the current space's pixels.</summary>
        private float HeadHeight(ArtSprite sprite, float scale)
        {
            if (sprite == null)
            {
                return 24f * scale;
            }

            return (sprite.Pivot.Y - sprite.ArtTop) / Math.Max(1f, sprite.Data.PixelsPerUnit) * _draw.UnitSize * scale;
        }

        private bool Standing(UnitSnapshot unit)
        {
            return _animation != null ? _animation.ShownStanding(unit.Id, _clockMs) : !unit.Defeated;
        }

        private HexCoordinate Position(UnitSnapshot unit)
        {
            if (_animation != null && _animation.Turn.Before.TryGetValue(unit.Id, out UnitSnapshot before) && _clockMs < _animation.MoveMs)
            {
                return before.Position;
            }

            return unit.Position;
        }

        private Vec2 Center(UnitSnapshot unit)
        {
            return _animation != null ? _animation.UnitCenter(unit.Id, _clockMs) : _layout.FootprintCenter(unit.Position, unit.Footprint);
        }

        /// <summary>
        /// The sprite a unit is drawn with: its species' or enemy's ArtKey (data) looked up in the art
        /// manifest; the content validator holds every shipped key to an entry, so the fallback (the
        /// brute) only shows for content loaded without one.
        /// </summary>
        private ArtSprite SpriteFor(string unitId)
        {
            string id = unitId != null && _speciesByUnit.TryGetValue(unitId, out string species) ? species : null;
            string artKey = DemoBattle.ArtKeyOf(_content, id, _regionId);
            return _atlas.ByArtKey(artKey) ?? _atlas.ByArtKey(_content.Enemies.Get(id)?.ArtKey) ?? _atlas.ByArtKey("enemy/brute");
        }

        private Color TeamColor(BattleTeam team)
        {
            return team == BattleTeam.Player ? Ink("c", Color.Blue) : Ink("r", Color.Red);
        }

        /// <summary>Palette char to colour (the fallback before the art has loaded).</summary>
        private Color Ink(string ch, Color fallback)
        {
            return _atlas == null ? fallback : _atlas.Palette(ch, fallback);
        }

        /// <summary>A board point snapped to a whole board pixel, so pixel art stays on its grid.</summary>
        private static Vector2 At(Vec2 point)
        {
            return new Vector2((float)Math.Round(point.X), (float)Math.Round(point.Y));
        }
    }
}
