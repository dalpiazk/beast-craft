using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Camera;
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
            _draw.Fill(Pixel, new Vector2(board.X, board.Y), new Vector2(board.Width, board.Height), Ink("p", Color.Purple) * 0.35f);

            // The auto camera (CameraRig / TurnCamera): the board zoomed and panned onto the action, clipped to its area.
            _boardFit = _camera.Fit(CameraNow());
            Rect clip = _canvasFit.ToScreen(board);
            _draw.SetClip(new Rectangle((int)Math.Floor(clip.X), (int)Math.Floor(clip.Y), (int)Math.Ceiling(clip.Width), (int)Math.Ceiling(clip.Height)));
            Matrix boardSpace = Matrix.CreateTranslation(shake.X, shake.Y, 0f) * Matrix.CreateScale(_boardFit.Scale) *
                                Matrix.CreateTranslation(_boardFit.OriginX, _boardFit.OriginY, 0f) * canvas;
            _draw.SetTransform(boardSpace);
            DrawBoard();
            DrawUnits(frames);
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
                if (VfxAuraSampler.TrySample(_content.Vfx.Aura(key), new Vec2(feet.X, feet.Y), UnitScale(unit), _clockMs + _idleClockMs, out VfxSprite sprite) &&
                    sprite.Ground == ground)
                {
                    DrawVfxSprite(sprite);
                }
            }

            _draw.SetBlend(BlendState.AlphaBlend);
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

        private void DrawUnits(List<(VfxTimeline Timeline, VfxFrame Frame)> frames)
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

            // Then everything that lies on the ground: decals and ground rings, and ground auras.
            DrawLayerSprites(frames, true);
            foreach (UnitSnapshot unit in units)
            {
                if (Standing(unit))
                {
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
                float scale = UnitScale(unit);
                bool fading = _animation != null && _animation.ShownFading(unit.Id, _clockMs);
                ArtSprite sprite = SpriteFor(unit.Id);
                DrawUnitSprite(sprite, unit, at, fading ? Color.White * 0.4f : Color.White);
                DrawAuras(unit, at, false);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.Hp;
                float barY = (float)Math.Round(at.Y - HeadHeight(sprite, scale)) - 4f;
                DrawHpBar(at.X, barY, scale <= 1f ? 24f : 40f, 3f, hp, unit.MaxHp);
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
