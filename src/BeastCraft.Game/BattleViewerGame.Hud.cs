using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Text;
using BeastCraft.Save;
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
        private GlossaryTerm _popupTerm;

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
            DrawSkillCard(shadow);

            if (_playback.IsOver && (_animation == null || _clockMs >= _animation.DurationMs))
            {
                string banner = _playback.Outcome == BattleOutcome.PlayerVictory ? "VICTORY" : _playback.Outcome == BattleOutcome.EnemyVictory ? "DEFEAT" : "STALEMATE";
                Rect board = _screen.Board;
                _draw.Fill(Pixel, new Vector2(board.X, board.Center.Y - 70f), new Vector2(board.Width, 140f), shadow * 0.75f);
                _text.DrawCentered(_draw, banner, board.Center.X, board.Center.Y - 30f, 60f, Ink("y", Color.Gold), shadow);
            }

            DrawSettings(shadow);
        }

        /// <summary>
        /// The settings overlay, when open: a panel over the board with one row per effects setting
        /// (tap to change) and a close row; the rest of the screen dimmed.
        /// </summary>
        private void DrawSettings(Color shadow)
        {
            if (!_settingsOpen)
            {
                return;
            }

            _draw.Fill(Pixel, new Rectangle(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), shadow * 0.55f);
            Rect panel = _screen.SettingsPanel;
            _draw.Fill(Pixel, new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), Ink("y", Color.Gold));
            _draw.Fill(Pixel, new Vector2(panel.X + 5f, panel.Y + 5f), new Vector2(panel.Width - 10f, panel.Height - 10f), Ink("p", Color.Purple));
            _text.DrawCentered(_draw, "EFFECTS SETTINGS", panel.Center.X, panel.Y + 36f, Large, Ink("y", Color.Gold), shadow);

            string intensity = _settings.EffectsIntensity == EffectsIntensity.Minimal ? "MINIMAL" : _settings.EffectsIntensity == EffectsIntensity.Reduced ? "REDUCED" : "FULL";
            string[] labels = { "EFFECTS", "SCREEN SHAKE", "FLASHES", null };
            string[] values = { intensity, _settings.ScreenShake ? "ON" : "OFF", _settings.Flashes ? "ON" : "OFF", null };
            for (int i = 0; i < PortraitLayout.SettingsRowCount; i++)
            {
                Rect row = _screen.SettingsRow(i);
                bool close = labels[i] == null;
                _draw.Fill(Pixel, new Vector2(row.X, row.Y), new Vector2(row.Width, row.Height), close ? Ink("2", Color.Gray) : Ink("1", Color.DarkGray));
                if (close)
                {
                    _text.DrawCentered(_draw, "CLOSE", row.Center.X, row.Center.Y - 12f, Large, Ink("4", Color.White), shadow);
                    continue;
                }

                _text.Draw(_draw, labels[i], new Vector2(row.X + 24f, row.Center.Y - 12f), Large, Ink("4", Color.White), shadow);
                bool on = values[i] != "OFF";
                _text.DrawRight(_draw, values[i], row.Right - 24f, row.Center.Y - 12f, Large, on ? Ink("l", Color.LightGreen) : Ink("o", Color.OrangeRed), shadow);
            }
        }

        private void DrawHeader(Color shadow)
        {
            Rect header = _screen.Header;
            _text.Draw(_draw, _host.HudTitle, new Vector2(header.X, header.Y + 12f), Large, Ink("y", Color.Gold), shadow);
            string turn = "TURN " + _playback.Played.Count.ToString(CultureInfo.InvariantCulture) + "  SEED " +
                          _options.Seed.ToString(CultureInfo.InvariantCulture);
            Rect gear = _screen.SettingsButton;
            _text.DrawRight(_draw, turn, gear.X - 20f, header.Y + 16f, Medium, Ink("3", Color.Gray), shadow);

            // The settings gear (opens the effects settings overlay).
            _draw.Fill(Pixel, new Vector2(gear.X, gear.Y), new Vector2(gear.Width, gear.Height), _settingsOpen ? Ink("y", Color.Gold) : Ink("2", Color.Gray));
            _draw.Fill(Pixel, new Vector2(gear.X + 3f, gear.Y + 3f), new Vector2(gear.Width - 6f, gear.Height - 6f), Ink("p", Color.Purple));
            ArtSprite icon = _atlas.ByArtKey("ui/gear");
            if (icon != null)
            {
                DrawIcon(icon, gear.Inset(10f));
            }
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
        /// The skill detail card of the selected (hovered or tapped) skill, grown from the old
        /// range-diagram card (<see cref="SkillCard"/>, laid out by <see cref="SkillCardLayout"/>):
        /// icon, name, element / category / target tags, cooldown, range and uses, one power line
        /// per effect and the level scaling, the hex range diagram, and the description as rich
        /// text with its glossary terms highlighted (tap one for its definition).
        /// </summary>
        private void DrawSkillCard(Color shadow)
        {
            int selected = SelectedSkill();
            BattleUnit unit = ActingUnit();
            if (selected < 0 || unit == null)
            {
                return;
            }

            SkillSO skill = ActingSkills()[selected];
            SkillCard card = SkillCard.Of(skill, _content.Glossary);
            SkillCardLayout layout = CardLayout(card);
            Rect panel = layout.Card;
            _draw.Fill(Pixel, new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), Ink("Y", Color.Yellow));
            _draw.Fill(Pixel, new Vector2(panel.X + 5f, panel.Y + 5f), new Vector2(panel.Width - 10f, panel.Height - 10f), Ink("K", Color.Black) * 0.96f);

            DrawSkillIcon(skill, layout.Icon, shadow);
            Rect name = layout.Name;
            _text.Draw(_draw, _text.Fit(card.Name, SkillCardLayout.NameSize, name.Width), new Vector2(name.X, name.Y), SkillCardLayout.NameSize, Ink("y", Color.Gold),
                       shadow);

            // Tags: element, damage category, then who it lands on.
            float x = name.X;
            if (card.Element != null)
            {
                x = DrawChip(card.Element, x, layout.ChipsY, ElementColor(skill.Element));
            }

            if (card.Category != null)
            {
                x = DrawChip(card.Category, x, layout.ChipsY, Ink("2", Color.Gray));
            }

            foreach (string target in card.Targets)
            {
                Color tint = target == "Enemy" ? Ink("r", Color.DarkRed) : target == "Ally" ? Ink("g", Color.DarkGreen) : Ink("q", Color.Chocolate);
                x = DrawChip(target, x, layout.ChipsY, tint);
            }

            // Cooldown, range and uses; a line per effect; the scaling: all wrapped to the card's width.
            DrawPlain(layout.Stats, layout.StatsAt, SkillCardLayout.StatsSize, Ink("4", Color.White), shadow);
            DrawPlain(layout.Power, layout.PowerAt, SkillCardLayout.PowerSize, Ink("C", Color.LightBlue), shadow);
            DrawPlain(layout.Scaling, layout.ScalingAt, SkillCardLayout.ScalingSize, Ink("3", Color.Gray), shadow);

            DrawRangeDiagram(skill, unit.Footprint, layout.Diagram);
            DrawRichText(layout, shadow);
            DrawGlossaryPopup(layout, shadow);
        }

        /// <summary>A tag chip with its top-left at (<paramref name="x"/>, <paramref name="y"/>); returns where the next one starts.</summary>
        private float DrawChip(string label, float x, float y, Color fill)
        {
            const float size = 17f;
            float width = _text.Measure(label, size) + 28f;
            _draw.Fill(Pixel, new Vector2(x, y), new Vector2(width, 36f), fill);
            _text.Draw(_draw, label, new Vector2(x + 14f, y + 10f), size, Ink("4", Color.White), Ink("K", Color.Black));
            return x + width + 12f;
        }

        /// <summary>The card's layout for the current text renderer (the same one taps are tested against).</summary>
        private SkillCardLayout CardLayout(SkillCard card)
        {
            return SkillCardLayout.Of(card, _screen.SkillDetail, (s, size) => _text.Measure(s, size), size => _text.LineHeight(size));
        }

        /// <summary>A wrapped plain-text layout's runs, relative to <paramref name="origin"/>.</summary>
        private void DrawPlain(RichTextLayout layout, Vec2 origin, float size, Color color, Color shadow)
        {
            foreach (RichRun run in layout.Runs)
            {
                _text.Draw(_draw, run.Text, new Vector2(origin.X + run.X, origin.Y + run.Y), size, color, shadow);
            }
        }

        /// <summary>The description: plain runs in white, glossary terms in their category's colour and underlined; the hint under it when there are terms.</summary>
        private void DrawRichText(SkillCardLayout layout, Color shadow)
        {
            Rect area = layout.Text;
            RichTextLayout text = layout.Description;
            const float size = SkillCardLayout.DescriptionSize;
            foreach (RichRun run in text.Runs)
            {
                Vector2 at = new Vector2(area.X + run.X, area.Y + run.Y);
                if (run.Term == null)
                {
                    _text.Draw(_draw, run.Text, at, size, Ink("4", Color.White), shadow);
                    continue;
                }

                Color color = TermColor(run.Term);
                if (_popupTerm == run.Term)
                {
                    _draw.Fill(Pixel, new Vector2(at.X - 4f, at.Y - 6f), new Vector2(run.Width + 8f, text.LineHeight - 2f), color * 0.3f);
                }

                _text.Draw(_draw, run.Text, at, size, color, shadow);
                _draw.Fill(Pixel, new Vector2(at.X, at.Y + size + 7f), new Vector2(run.Width, 3f), color);
            }

            if (layout.HintY.HasValue)
            {
                _text.Draw(_draw, "Tap a highlighted word to learn it", new Vector2(area.X, layout.HintY.Value), SkillCardLayout.HintSize, Ink("3", Color.Gray), shadow);
            }
        }

        /// <summary>
        /// The open glossary term's definition in a popup clear of the term and the description
        /// (<see cref="SkillCardLayout.PlacePopup"/>: below them, else above), with a pointer toward the term.
        /// </summary>
        private void DrawGlossaryPopup(SkillCardLayout layout, Color shadow)
        {
            if (_popupTerm == null)
            {
                return;
            }

            const float width = 600f;
            const float size = 20f;
            RichTextLayout definition = RichTextLayout.Of(new[] { new RichSpan(_popupTerm.Definition ?? string.Empty, null) }, s => _text.Measure(s, size), width - 48f,
                                                          _text.LineHeight(size));
            PopupPlacement place = layout.PlacePopup(_popupTerm, width, 104f + definition.Height);
            Rect popup = place.Rect;
            Color color = TermColor(_popupTerm);
            _draw.Fill(Pixel, new Vector2(popup.X, popup.Y), new Vector2(popup.Width, popup.Height), color);
            _draw.Fill(Pixel, new Vector2(popup.X + 4f, popup.Y + 4f), new Vector2(popup.Width - 8f, popup.Height - 8f), Ink("p", Color.Purple));

            // The pointer: a stepped triangle in the gap, pointing at the term.
            float half = PopupPlacement.PointerHalfWidth;
            for (int step = 0; step < 4; step++)
            {
                float w = 2f * half * (4 - step) / 4f;
                float y = place.Below ? popup.Y - 4f * (step + 1) : popup.Bottom + 4f * step;
                _draw.Fill(Pixel, new Vector2(place.PointerX - w / 2f, y), new Vector2(w, 4f), color);
            }

            _text.Draw(_draw, _popupTerm.Term, new Vector2(popup.X + 24f, popup.Y + 22f), 28f, color, shadow);
            _text.DrawRight(_draw, (_popupTerm.Category ?? string.Empty).ToUpperInvariant(), popup.Right - 24f, popup.Y + 30f, 15f, Ink("3", Color.Gray), shadow);
            foreach (RichRun run in definition.Runs)
            {
                _text.Draw(_draw, run.Text, new Vector2(popup.X + 24f + run.X, popup.Y + 80f + run.Y), size, Ink("4", Color.White), shadow);
            }

            if (_options.Screenshot)
            {
                Console.WriteLine("Glossary popup for " + _popupTerm.TermId + ": " + (place.Below ? "below" : "above") + " the description" + (place.Fits ? string.Empty : " (clamped)") +
                                  " at " + popup + ".");
            }
        }

        /// <summary>A glossary term's colour by category: statuses gold, stances sky, combat terms peach, passives lilac.</summary>
        private Color TermColor(GlossaryTerm term)
        {
            switch (term.Category)
            {
                case "Stance":
                    return Ink("C", Color.LightBlue);
                case "Combat":
                    return Ink("s", Color.PeachPuff);
                case "Passive":
                    return Ink("u", Color.Plum);
                default:
                    return Ink("Y", Color.Yellow);
            }
        }

        /// <summary>
        /// A hex diagram of the skill's range and area (<see cref="SkillFootprint"/>, for the
        /// acting unit's own footprint) fitted to <paramref name="area"/>: the caster gold, the
        /// tiles it may pick a target on tinted, the tiles it hits in its side's colour, the example
        /// target outlined.
        /// </summary>
        private void DrawRangeDiagram(SkillSO skill, UnitFootprint casterFootprint, Rect area)
        {
            SkillFootprint footprint = SkillFootprint.Of(skill, casterFootprint);
            bool allies = footprint.Side == SkillTargetSide.Ally;
            _draw.Fill(Pixel, new Vector2(area.X, area.Y), new Vector2(area.Width, area.Height), Ink("p", Color.Purple) * 0.6f);
            int radius = Math.Min(6, footprint.Extent);
            BoardFit fit = PortraitLayout.FitBoard(radius, area.Inset(12f));
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

        /// <summary>An icon sprite (its pivot at its centre) scaled to fit <paramref name="box"/>, centred.</summary>
        private void DrawIcon(ArtSprite art, Rect box)
        {
            float ppu = art.Data.PixelsPerUnit > 0f ? art.Data.PixelsPerUnit : _draw.UnitSize;
            float scale = Math.Min(box.Width / art.Data.FrameWidth, box.Height / Math.Max(1, art.Data.FrameHeight)) * ppu / _draw.UnitSize;
            float k = scale * _draw.UnitSize / ppu;
            Vector2 pivot = new Vector2(box.Center.X - art.Data.FrameWidth * k / 2f + art.Pivot.X * k, box.Center.Y - art.Data.FrameHeight * k / 2f + art.Pivot.Y * k);
            _draw.DrawSprite(art, 0, pivot, scale, Color.White);
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
                DrawIcon(art, box);
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
