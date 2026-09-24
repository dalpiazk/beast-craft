using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>The HUD half of the frame, in canvas pixels: header, turn order, toast, skill strip, controls.</summary>
    public sealed partial class BattleViewerGame
    {
        private const float Small = 15f;
        private const float Medium = 20f;
        private const float Large = 25f;

        private Texture2D _pixel;

        /// <summary>A 1x1 white texture for rectangles and bars (made on first use, so a failure screen has one too).</summary>
        private Texture2D Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(GraphicsDevice, 1, 1);
                    _pixel.SetData(new[] { Color.White });
                }

                return _pixel;
            }
        }

        private void DrawHud(ScheduledBeat beat)
        {
            Color shadow = Ink("K", Color.Black);
            DrawHeader(shadow);
            DrawTurnOrder(shadow);
            DrawToast(shadow);
            DrawSkillStrip(beat, shadow);
            DrawControls(shadow);
            DrawSkillDiagram(shadow);

            if (_playback.IsOver && (_animation == null || _clockMs >= _animation.DurationMs))
            {
                string banner = _playback.Outcome == BattleOutcome.PlayerVictory ? "VICTORY" : _playback.Outcome == BattleOutcome.EnemyVictory ? "DEFEAT" : "STALEMATE";
                Rect board = _screen.Board;
                _draw.Fill(Pixel, new Vector2(board.X, board.Center.Y - 70f), new Vector2(board.Width, 140f), shadow * 0.75f);
                _text.DrawCentered(_draw, banner, board.Center.X, board.Center.Y - 30f, 60f, Ink("y", Color.Gold), shadow);
            }
        }

        private void DrawHeader(Color shadow)
        {
            Rect header = _screen.Header;
            _text.Draw(_draw, _host.HudTitle, new Vector2(header.X, header.Y + 12f), Large, Ink("y", Color.Gold), shadow);
            string turn = "TURN " + _playback.Played.Count.ToString(CultureInfo.InvariantCulture) + "  SEED " +
                          _options.Seed.ToString(CultureInfo.InvariantCulture);
            _text.DrawRight(_draw, turn, header.Right, header.Y + 16f, Medium, Ink("3", Color.Gray), shadow);
        }

        /// <summary>The acting unit (this turn's, else the next to act) and the ones after it, as portraits.</summary>
        private void DrawTurnOrder(Color shadow)
        {
            Rect band = _screen.TurnOrder;
            _text.DrawRight(_draw, "TURN ORDER", band.Right, band.Y + 8f, Small, Ink("3", Color.Gray), shadow);

            List<BattleUnit> order = new List<BattleUnit>();
            if (_animation != null)
            {
                order.Add(_animation.Turn.Turn.Unit);
            }

            const int slots = 8;
            foreach (BattleUnit unit in _playback.Forecast(slots))
            {
                if (order.Count < slots)
                {
                    order.Add(unit);
                }
            }

            for (int i = 0; i < order.Count; i++)
            {
                BattleUnit unit = order[i];
                Rect slot = _screen.TurnOrderSlot(i, slots);
                bool current = i == 0;
                Color border = current ? Ink("Y", Color.Yellow) : TeamColor(unit.Team);
                float thickness = current ? 6f : 3f;
                _draw.Fill(Pixel, new Vector2(slot.X, slot.Y), new Vector2(slot.Width, slot.Height), border);
                _draw.Fill(Pixel, new Vector2(slot.X + thickness, slot.Y + thickness), new Vector2(slot.Width - 2f * thickness, slot.Height - 2f * thickness),
                           Ink("1", Color.DarkGray));
                DrawFitted(SpriteFor(unit.Id), slot.Inset(thickness + 6f), unit.Team == BattleTeam.Enemy);

                int hp = _animation != null ? _animation.ShownHp(unit.Id, _clockMs) : unit.CurrentHp;
                float barWidth = slot.Width - 2f * thickness - 12f;
                DrawHpBar(slot.Center.X, slot.Bottom - thickness - 12f, barWidth, 6f, hp, unit.Stats.Hp);
                if (current)
                {
                    _text.DrawCentered(_draw, _animation != null ? "NOW" : "NEXT", slot.Center.X, band.Y + 8f, Small, Ink("Y", Color.Yellow), shadow);
                }
            }
        }

        /// <summary>The log, collapsed to its latest line.</summary>
        private void DrawToast(Color shadow)
        {
            if (_log.Count == 0)
            {
                return;
            }

            Rect toast = _screen.Toast;
            _draw.Fill(Pixel, new Vector2(toast.X, toast.Y), new Vector2(toast.Width, toast.Height), Ink("p", Color.Purple) * 0.8f);
            string count = "#" + _log.Count.ToString(CultureInfo.InvariantCulture);
            float countWidth = _text.Measure(count, Small);
            _text.Draw(_draw, _text.Fit(_log[_log.Count - 1], Medium, toast.Width - countWidth - 48f), new Vector2(toast.X + 16f, toast.Y + 22f), Medium,
                       Ink("4", Color.White), shadow);
            _text.DrawRight(_draw, count, toast.Right - 16f, toast.Y + 25f, Small, Ink("3", Color.Gray), shadow);
        }

        /// <summary>The acting unit's skills as cards: icon, name, range and shape, cooldown; the one firing and the one selected marked.</summary>
        private void DrawSkillStrip(ScheduledBeat beat, Color shadow)
        {
            Rect strip = _screen.SkillStrip;
            BattleUnit unit = ActingUnit();
            IReadOnlyList<SkillSO> skills = ActingSkills();
            if (unit == null)
            {
                return;
            }

            _text.Draw(_draw, Name(unit.Id), new Vector2(strip.X, strip.Y + 8f), Medium, Ink("y", Color.Gold), shadow);
            _text.Draw(_draw, "SKILLS", new Vector2(strip.X + _text.Measure(Name(unit.Id), Medium) + 20f, strip.Y + 12f), Small, Ink("3", Color.Gray), shadow);

            int shown = SelectedSkill();
            for (int i = 0; i < skills.Count; i++)
            {
                SkillSO skill = skills[i];
                Rect card = _screen.SkillCard(i, skills.Count);
                bool firing = beat != null && beat.Beat.SkillId == skill.SkillId;
                Color border = i == shown ? Ink("Y", Color.Yellow) : firing ? ElementColor(skill.Element) : Ink("2", Color.Gray);
                float thickness = i == shown || firing ? 5f : 3f;
                _draw.Fill(Pixel, new Vector2(card.X, card.Y), new Vector2(card.Width, card.Height), border);
                _draw.Fill(Pixel, new Vector2(card.X + thickness, card.Y + thickness), new Vector2(card.Width - 2f * thickness, card.Height - 2f * thickness),
                           Ink("K", Color.Black));

                Rect icon = new Rect(card.X + 14f, card.Y + 14f, 72f, 72f);
                DrawSkillIcon(skill, icon, shadow);

                int cooldown = unit.Skills == null ? 0 : unit.Skills.RemainingCooldown(i);
                string state = firing ? "CAST" : cooldown <= 0 ? "READY" : "CD " + cooldown.ToString(CultureInfo.InvariantCulture);
                _text.Draw(_draw, state, new Vector2(icon.Right + 12f, icon.Y + 6f), Small, cooldown <= 0 || firing ? Ink("l", Color.LightGreen) : Ink("3", Color.Gray),
                           shadow);
                _text.Draw(_draw, _text.Fit(ShapeLabel(skill), Small, card.Right - icon.Right - 20f), new Vector2(icon.Right + 12f, icon.Y + 40f), Small,
                           Ink("3", Color.Gray), shadow);

                float y = icon.Bottom + 16f;
                foreach (string line in Wrap(skill.DisplayName ?? skill.SkillId, Small, card.Width - 28f, 2))
                {
                    _text.Draw(_draw, line, new Vector2(card.X + 14f, y), Small, Ink("4", Color.White), shadow);
                    y += _text.LineHeight(Small);
                }
            }
        }

        /// <summary>
        /// The selected (hovered or tapped) skill's card: its name, shape, side and cooldown, and a
        /// hex diagram of its range and area (<see cref="SkillFootprint"/>, for the acting unit's own
        /// footprint): the caster gold, the tiles it may pick a target on tinted, the tiles it hits
        /// in its side's colour, the example target outlined. Drawn over the foot of the board, above
        /// the strip — the seed of a skill detail card.
        /// </summary>
        private void DrawSkillDiagram(Color shadow)
        {
            int selected = SelectedSkill();
            BattleUnit unit = ActingUnit();
            if (selected < 0 || unit == null)
            {
                return;
            }

            SkillSO skill = ActingSkills()[selected];
            SkillFootprint footprint = SkillFootprint.Of(skill, unit.Footprint);
            Rect board = _screen.Board;
            Rect panel = new Rect(board.Right - 460f, board.Bottom - 470f, 460f, 460f);
            _draw.Fill(Pixel, new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), Ink("Y", Color.Yellow));
            _draw.Fill(Pixel, new Vector2(panel.X + 4f, panel.Y + 4f), new Vector2(panel.Width - 8f, panel.Height - 8f), Ink("K", Color.Black) * 0.94f);

            bool allies = footprint.Side == SkillTargetSide.Ally;
            DrawSkillIcon(skill, new Rect(panel.X + 20f, panel.Y + 18f, 56f, 56f), shadow);
            _text.Draw(_draw, _text.Fit(skill.DisplayName ?? skill.SkillId, Medium, panel.Width - 116f), new Vector2(panel.X + 92f, panel.Y + 20f), Medium,
                       Ink("y", Color.Gold), shadow);
            string what = ShapeLabel(skill) + "  " + (footprint.IsGlobal ? (allies ? "EVERY ALLY" : "EVERY FOE") : allies ? "ALLIES" : "FOES") + "  CD " +
                          skill.Cooldown.ToString(CultureInfo.InvariantCulture);
            _text.Draw(_draw, _text.Fit(what, Small, panel.Width - 116f), new Vector2(panel.X + 92f, panel.Y + 52f), Small, Ink("3", Color.Gray), shadow);

            // The diagram, in its own hex space (tile (0, 0) = the caster's anchor) fitted to the card.
            Rect area = new Rect(panel.X + 20f, panel.Y + 80f, panel.Width - 40f, panel.Height - 100f);
            int radius = Math.Min(6, footprint.Extent);
            BoardFit fit = PortraitLayout.FitBoard(radius, area);
            Matrix canvas = Matrix.CreateScale(_canvasFit.Scale) * Matrix.CreateTranslation(_canvasFit.OffsetX, _canvasFit.OffsetY, 0f);
            _draw.SetTransform(Matrix.CreateScale(fit.Scale) * Matrix.CreateTranslation(fit.OriginX, fit.OriginY, 0f) * canvas);

            HashSet<HexCoordinate> caster = new HashSet<HexCoordinate>(footprint.Caster);
            HashSet<HexCoordinate> reach = new HashSet<HexCoordinate>(footprint.Reach);
            HashSet<HexCoordinate> hits = new HashSet<HexCoordinate>(footprint.Area);
            Color sideColor = allies ? Ink("l", Color.LightGreen) : Ink("o", Color.OrangeRed);
            ArtSprite mask = _atlas.Sprite("hex_mask");
            ArtSprite outline = _atlas.Sprite("hex_outline");
            foreach (HexCoordinate tile in SkillFootprint.Disc(HexCoordinate.Zero, radius))
            {
                Vector2 at = At(_layout.Center(tile));
                Color fill = Ink("1", Color.DarkGray);
                if (caster.Contains(tile))
                {
                    fill = Ink("y", Color.Gold);
                }
                else if (hits.Contains(tile) || (footprint.IsGlobal && tile != HexCoordinate.Zero))
                {
                    fill = sideColor * (footprint.IsGlobal ? 0.6f : 0.95f);
                }
                else if (reach.Contains(tile))
                {
                    fill = Ink("c", Color.Blue) * 0.7f;
                }

                _draw.DrawSprite(mask, 0, at, 1f, fill);
                _draw.DrawSprite(outline, 0, at, 1f, Ink("K", Color.Black) * 0.6f);
            }

            if (footprint.Focus.HasValue)
            {
                _draw.DrawSprite(outline, 0, At(_layout.Center(footprint.Focus.Value)), 1f, Ink("Y", Color.Yellow));
            }

            _draw.SetTransform(canvas);
        }

        /// <summary>Pause/play, the three speeds and skip.</summary>
        private void DrawControls(Color shadow)
        {
            string[] labels = { _auto ? "PAUSE" : "PLAY", "X1", "X2", "X3", "SKIP" };
            for (int i = 0; i < labels.Length; i++)
            {
                Rect button = _screen.Control(i);
                bool on = (i == ControlPause && _auto) || (i >= 1 && i <= 3 && _speed == i);
                _draw.Fill(Pixel, new Vector2(button.X, button.Y), new Vector2(button.Width, button.Height), on ? Ink("y", Color.Gold) : Ink("2", Color.Gray));
                _draw.Fill(Pixel, new Vector2(button.X + 4f, button.Y + 4f), new Vector2(button.Width - 8f, button.Height - 8f),
                           on ? Ink("q", Color.Orange) * 0.9f : Ink("p", Color.Purple));
                _text.DrawCentered(_draw, labels[i], button.Center.X, button.Center.Y - 12f, Large, on ? Ink("Y", Color.Yellow) : Ink("4", Color.White), shadow);
            }
        }

        /// <summary>
        /// A character sprite scaled so its whole frame fits <paramref name="box"/>, centred (a
        /// turn-order portrait): whatever its source size, since it is placed by pixels-per-unit.
        /// </summary>
        private void DrawFitted(ArtSprite sprite, Rect box, bool flip)
        {
            if (sprite == null)
            {
                return;
            }

            float ppu = Math.Max(1f, sprite.Data.PixelsPerUnit);
            float frame = Math.Max(sprite.Data.FrameWidth, sprite.Data.FrameHeight);
            float unit = _draw.UnitSize;
            _draw.UnitSize = Math.Min(box.Width, box.Height) * ppu / frame;
            float k = _draw.UnitSize / ppu;
            float pivotX = flip ? sprite.Data.FrameWidth - sprite.Pivot.X : sprite.Pivot.X;
            Vector2 at = new Vector2(box.X + (box.Width - sprite.Data.FrameWidth * k) / 2f + pivotX * k,
                                     box.Y + (box.Height - sprite.Data.FrameHeight * k) / 2f + sprite.Pivot.Y * k);
            DrawCharacter(sprite, at, 1f, Color.White, flip);
            _draw.UnitSize = unit;
        }

        /// <summary>
        /// A skill's icon fitted to <paramref name="box"/>: its art (<see cref="SkillSO.ArtKey"/> looked
        /// up in the manifest), else — a skill with no icon, such as an enemy-library one — an
        /// element-coloured tile with the skill's initial.
        /// </summary>
        private void DrawSkillIcon(SkillSO skill, Rect box, Color shadow)
        {
            ArtSprite art = _atlas.ByArtKey(skill.ArtKey);
            if (art != null && art.Data.FrameWidth > 0)
            {
                float ppu = art.Data.PixelsPerUnit > 0f ? art.Data.PixelsPerUnit : _draw.UnitSize;
                float scale = Math.Min(box.Width / art.Data.FrameWidth, box.Height / Math.Max(1, art.Data.FrameHeight)) * ppu / _draw.UnitSize;
                Vector2 pivot = new Vector2(box.Center.X - art.Data.FrameWidth * scale * _draw.UnitSize / ppu / 2f + art.Pivot.X * scale * _draw.UnitSize / ppu,
                                            box.Center.Y - art.Data.FrameHeight * scale * _draw.UnitSize / ppu / 2f + art.Pivot.Y * scale * _draw.UnitSize / ppu);
                _draw.DrawSprite(art, 0, pivot, scale, Color.White);
                return;
            }

            _draw.Fill(Pixel, new Vector2(box.X, box.Y), new Vector2(box.Width, box.Height), ElementColor(skill.Element));
            string initial = string.IsNullOrEmpty(skill.DisplayName) ? "?" : skill.DisplayName.Substring(0, 1);
            _text.DrawCentered(_draw, initial, box.Center.X + 2f, box.Y + box.Height * 0.22f, box.Height * 0.55f, Ink("4", Color.White), shadow);
        }

        /// <summary>The unit whose skills the strip shows: the one acting now, else the next to act.</summary>
        private BattleUnit ActingUnit()
        {
            if (_playback == null)
            {
                return null;
            }

            if (_animation != null)
            {
                return _animation.Turn.Turn.Unit;
            }

            List<BattleUnit> next = _playback.Forecast(1);
            return next.Count > 0 ? next[0] : null;
        }

        private IReadOnlyList<SkillSO> ActingSkills()
        {
            BattleUnit unit = ActingUnit();
            return unit == null || unit.Skills == null ? (IReadOnlyList<SkillSO>)new SkillSO[0] : unit.Skills.Skills;
        }

        /// <summary>The skill card shown selected: the one hovered (desktop), else the one tapped; -1 for none.</summary>
        private int SelectedSkill()
        {
            int count = ActingSkills().Count;
            int shown = _hoveredSkill >= 0 ? _hoveredSkill : _selectedSkill;
            return shown >= 0 && shown < count ? shown : -1;
        }

        private static string ShapeLabel(SkillSO skill)
        {
            switch (skill.TargetShape)
            {
                case SkillTargetShape.Self:
                    return "SELF";
                case SkillTargetShape.AllAllies:
                    return "ALL ALLIES";
                case SkillTargetShape.AllEnemies:
                    return "ALL FOES";
                case SkillTargetShape.AreaBurst:
                    return "BURST R" + skill.Range.ToString(CultureInfo.InvariantCulture);
                case SkillTargetShape.Line:
                    return "LINE R" + skill.Range.ToString(CultureInfo.InvariantCulture);
                case SkillTargetShape.Cross:
                    return "CROSS R" + skill.Range.ToString(CultureInfo.InvariantCulture);
                default:
                    return "SINGLE R" + skill.Range.ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Words of <paramref name="text"/> packed into at most <paramref name="maxLines"/> lines of <paramref name="width"/>.</summary>
        private List<string> Wrap(string text, float size, float width, int maxLines)
        {
            List<string> lines = new List<string>();
            string line = string.Empty;
            foreach (string word in (text ?? string.Empty).Split(' '))
            {
                string next = line.Length == 0 ? word : line + " " + word;
                if (_text.Measure(next, size) <= width || line.Length == 0)
                {
                    line = next;
                    continue;
                }

                lines.Add(line);
                line = word;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }

            if (lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
            }

            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = _text.Fit(lines[i], size, width);
            }

            return lines;
        }

        /// <summary>An element's placeholder colour (a palette char per element).</summary>
        private Color ElementColor(Element element)
        {
            switch (element)
            {
                case Element.Fire:
                    return Ink("o", Color.OrangeRed);
                case Element.Water:
                    return Ink("c", Color.Blue);
                case Element.Earth:
                    return Ink("m", Color.Brown);
                case Element.Air:
                    return Ink("A", Color.Teal);
                case Element.Lightning:
                    return Ink("h", Color.Gold);
                case Element.Ice:
                    return Ink("C", Color.LightBlue);
                case Element.Nature:
                    return Ink("G", Color.Green);
                case Element.Metal:
                    return Ink("2", Color.Gray);
                case Element.Light:
                    return Ink("M", Color.Beige);
                case Element.Dark:
                    return Ink("P", Color.Purple);
                default:
                    return Ink("2", Color.Gray);
            }
        }
    }
}
