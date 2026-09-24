using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
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
        /// <summary>
        /// Draws one frame onto a <paramref name="width"/> x <paramref name="height"/> target: the
        /// portrait canvas fitted inside <paramref name="insets"/> (black bars around it), the board
        /// through its own fit (<see cref="PortraitLayout.FitBoard(int)"/>, shaken by the VFX) and
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
            Vec2 shake = vfx == null ? Vec2.Zero : vfx.Shake;

            Rect board = _screen.Board;
            _draw.Fill(Pixel, new Vector2(board.X, board.Y), new Vector2(board.Width, board.Height), Ink("p", Color.Purple) * 0.35f);

            Matrix boardSpace = Matrix.CreateTranslation(shake.X, shake.Y, 0f) * Matrix.CreateScale(_boardFit.Scale) *
                                Matrix.CreateTranslation(_boardFit.OriginX, _boardFit.OriginY, 0f) * canvas;
            _draw.SetTransform(boardSpace);
            DrawBoard();
            DrawUnits();
            if (vfx != null)
            {
                DrawVfx(beat, vfx, false);
                _draw.SetBlend(BlendState.Additive);
                DrawVfx(beat, vfx, true);
                DrawFlash(beat, vfx);
                _draw.SetBlend(BlendState.AlphaBlend);
                DrawDamageNumbers(beat, vfx);
            }

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

        private void DrawBoard()
        {
            ArtSprite grass = _atlas.Sprite("hex_grass");
            ArtSprite rock = _atlas.Sprite("hex_scorched");
            foreach (HexCoordinate tile in _playback.Grid.Tiles)
            {
                ArtSprite sprite = _playback.Grid.IsInDeploymentZone(tile, BattleTeam.Enemy) ? rock : grass;
                _draw.DrawSprite(sprite, 0, At(_layout.Center(tile)), 1f, Color.White);
            }
        }

        private void DrawUnits()
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
                    Vector2 at = At(_layout.Center(tile));
                    _draw.DrawSprite(mask, 0, at, 1f, team * 0.35f);
                    if (unit.Id == actor)
                    {
                        _draw.DrawSprite(outline, 0, at, 1f, Ink("Y", Color.Yellow));
                    }
                }
            }

            foreach (UnitSnapshot unit in units)
            {
                if (!Standing(unit))
                {
                    continue;
                }

                Vector2 at = At(Center(unit));
                float scale = UnitScale(unit);
                bool fading = _animation != null && _animation.ShownFading(unit.Id, _clockMs);
                ArtSprite sprite = SpriteFor(unit.Id);
                DrawUnitSprite(sprite, unit, at, fading ? Color.White * 0.4f : Color.White);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.Hp;
                DrawHpBar(at.X, (float)Math.Round(at.Y - HeadHeight(sprite, scale)) - 4f, scale <= 1f ? 24f : 40f, 3f, hp, unit.MaxHp);
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

        private void DrawVfx(ScheduledBeat beat, VfxFrame vfx, bool additivePass)
        {
            VfxEffectData effect = beat.Timeline.Effect;

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
                foreach (VfxTarget target in beat.Timeline.Targets)
                {
                    DrawCentered(flipbook.Sheet, vfx.FlipbookFrame, target.Position, flipbook.Scale, Ink(flipbook.Tint, Color.White));
                }
            }

            VfxParticleData particles = effect.Particles;
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
                    DrawUnitSprite(SpriteFor(unit.Id), unit, At(Center(unit)), tint);
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
                // Above the unit's head: a big unit is drawn at twice the size.
                bool big = _animation.Turn.After.TryGetValue(number.UnitId ?? string.Empty, out UnitSnapshot unit) && unit.Footprint != UnitFootprint.Single;
                float lift = big ? 66f : 38f;
                _text.DrawCentered(_draw, text, (float)Math.Round(number.Position.X), (float)Math.Round(number.Position.Y) - lift, 10f, color,
                                   Ink("K", Color.Black) * number.Alpha);
            }
        }

        private void DrawCentered(string sheet, int frame, Vec2 at, float scale, Color color)
        {
            _draw.DrawSprite(_atlas.Sprite(sheet), frame, At(at), scale, color);
        }

        /// <summary>
        /// A unit's sprite, pivot (feet) on <paramref name="at"/>, facing the other side, at its
        /// footprint's size; a sprite with an <c>idle</c> clip plays it on the viewer's clock.
        /// </summary>
        private void DrawUnitSprite(ArtSprite sprite, UnitSnapshot unit, Vector2 at, Color color)
        {
            DrawCharacter(sprite, at, UnitScale(unit), color, unit.Team == BattleTeam.Enemy);
        }

        /// <summary>A character sprite with its pivot on <paramref name="at"/>, playing its <c>idle</c> clip when it has one.</summary>
        private void DrawCharacter(ArtSprite sprite, Vector2 at, float scale, Color color, bool flip)
        {
            if (sprite == null)
            {
                return;
            }

            ArtAnimationData idle = sprite.Data.Animation("idle");
            ArtSprite sheet = idle == null ? null : _atlas.Sprite(string.IsNullOrEmpty(idle.Sheet) ? sprite.Name : idle.Sheet);
            if (sheet != null)
            {
                _draw.DrawFrame(sprite, sheet.Texture, sheet.Frame(idle.FrameAt(_clockMs + _idleClockMs)), at, scale, color, flip, 0f);
                return;
            }

            _draw.DrawSprite(sprite, 0, at, scale, color, flip);
        }

        /// <summary>A unit's draw scale: one hex for a one-tile unit, two for a large one.</summary>
        private static float UnitScale(UnitSnapshot unit)
        {
            return unit.Footprint == UnitFootprint.Single ? 1f : 2f;
        }

        /// <summary>How far a sprite drawn at <paramref name="scale"/> reaches above its pivot, in the current space's pixels.</summary>
        private float HeadHeight(ArtSprite sprite, float scale)
        {
            if (sprite == null)
            {
                return 24f * scale;
            }

            return sprite.Pivot.Y / Math.Max(1f, sprite.Data.PixelsPerUnit) * _draw.UnitSize * scale;
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
            string artKey = _content.Battle.GetSpecies(id)?.ArtKey ?? _content.Enemies.Get(id)?.ArtKey;
            return _atlas.ByArtKey(artKey) ?? _atlas.ByArtKey("enemy/brute");
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
