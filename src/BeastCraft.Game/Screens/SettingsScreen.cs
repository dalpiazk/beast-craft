using System;
using BeastCraft.Game.Rendering;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using BeastCraft.Save;
using Microsoft.Xna.Framework;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>The settings screen's inner tabs.</summary>
    public enum SettingsTab
    {
        Gameplay = 0,
        Visuals = 1,
        Audio = 2,
        Privacy = 3
    }

    /// <summary>
    /// The settings screen (menu-screens pass, #67): a full screen, grouped into four sections —
    /// Gameplay, Visuals &amp; accessibility, Audio, Privacy — replacing the old <c>SettingsModal</c>'s
    /// single long cycling list of rows, built on the shared <see cref="Components"/> layer
    /// (<see cref="ScreenHeader"/>, <see cref="TabStrip"/>, <see cref="CardList"/>,
    /// <see cref="SectionHeader"/>, <see cref="ChipRow"/>) plus the two new reusable controls this
    /// screen needed, <see cref="Components.Toggle"/> and <see cref="Components.Slider"/>. Reachable
    /// from the title, the home header's gear and the battle pause menu (<see cref="PauseMenuModal"/>);
    /// every change saves through <see cref="SettingsViewModel"/> at once. A pinned "About &amp; Credits"
    /// link (<see cref="CreditsScreen"/>) sits under every tab's content.
    /// </summary>
    public sealed class SettingsScreen : GameScreen
    {
        private const float RowGap = 22f;
        private const float CardTopPad = 112f;
        private const float CardBottomPad = 30f;
        private const float FooterHeight = 150f;

        private static readonly string[] TabKeys = { "ui.settings.tab_gameplay", "ui.settings.tab_visuals", "ui.settings.tab_audio", "ui.settings.tab_privacy" };
        private static readonly string[] TabGlyphs = { "battle", "star", "speaker", "lock" };

        private readonly SettingsViewModel _model;
        private readonly BackdropPickerViewModel _backdrops;
        private readonly ScreenHeader _header;
        private readonly Tabs _tabs;
        private readonly CardList _gameplay;
        private readonly CardList _visuals;
        private readonly CardList _audio;
        private readonly CardList _privacy;

        public SettingsScreen(ScreenContext ctx) : base(ctx)
        {
            _model = Ctx.Game.NewSettingsModel();
            _backdrops = Ctx.Session == null ? null : new BackdropPickerViewModel(Ctx.Session);
            float top = TabStrip.ContentTop(HeaderMetrics.Standard);
            Rect page = new Rect(0, top, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - top - FooterHeight);
            _gameplay = new CardList(Ui.Add(new ScrollView { Id = "settings-gameplay", Bounds = page }));
            _visuals = new CardList(Ui.Add(new ScrollView { Id = "settings-visuals", Bounds = page, Visible = false }));
            _audio = new CardList(Ui.Add(new ScrollView { Id = "settings-audio", Bounds = page, Visible = false }));
            _privacy = new CardList(Ui.Add(new ScrollView { Id = "settings-privacy", Bounds = page, Visible = false }));

            _header = new ScreenHeader(Ui, HeaderMetrics.Standard, () => Ctx.Stack.Pop());
            _tabs = TabStrip.Build(Ui, "settings-tabs", HeaderMetrics.Standard, Array.ConvertAll(TabKeys, Loc), TabGlyphs, index => SelectTab((SettingsTab)index));

            float aboutWidth = 560f;
            AddButton(null, "about-credits", new Rect((PortraitLayout.CanvasWidth - aboutWidth) / 2f, PortraitLayout.CanvasHeight - FooterHeight + 24f, aboutWidth, 96f),
                      Loc("ui.settings.about_credits"), "secondary", () => Ctx.Stack.Push(new CreditsScreen(Ctx)));

            BuildAll();
        }

        public override string Name => "settings";

        public SettingsViewModel Model => _model;

        public override void Enter()
        {
            base.Enter();
            _backdrops?.Refresh();
            BuildAll();
        }

        public void SelectTab(SettingsTab tab)
        {
            _tabs.Selected = (int)tab;
            _gameplay.Scroll.Visible = tab == SettingsTab.Gameplay;
            _visuals.Scroll.Visible = tab == SettingsTab.Visuals;
            _audio.Scroll.Visible = tab == SettingsTab.Audio;
            _privacy.Scroll.Visible = tab == SettingsTab.Privacy;
        }

        private void Build()
        {
            BuildAll();
        }

        private void BuildAll()
        {
            BuildGameplay();
            BuildVisuals();
            BuildAudio();
            BuildPrivacy();
            SelectTab((SettingsTab)_tabs.Selected);
        }

        // ------------------------------------------------------------------------------------------
        // Gameplay: battle speed, auto next battle, team suggestions, tutorial hints, alerts.
        // ------------------------------------------------------------------------------------------

        private void BuildGameplay()
        {
            _gameplay.Begin();
            float y = 10f;
            float width = _gameplay.Width - 72f;
            float x = HeaderMetrics.Pad + 36f;
            int toggles = 3 + (_model.NotificationsAvailable ? 2 : 0);
            float height = CardTopPad + ChipRow.Height + RowGap + toggles * (Toggle.Height + RowGap) + CardBottomPad;
            float top = y;
            y = _gameplay.Card(top, height, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.settings.section_gameplay")));

            float rowY = top + CardTopPad;
            _gameplay.Add(new Label { Bounds = new Rect(x, rowY - 44f, width, 36f), Text = Loc("ui.settings.battle_speed"), Size = Ctx.Style.TextSizes.Small + 2f, ColorKey = "inkSoft" });
            string[] speeds = { Loc("ui.settings.speed_value", 1), Loc("ui.settings.speed_value", 2), Loc("ui.settings.speed_value", 3) };
            rowY = ChipRow.Build(Ctx, _gameplay.Scroll, x, rowY, width, speeds, _model.BattleSpeed - 1, "speed-", i =>
            {
                _model.SetBattleSpeed(i + 1);
                Build();
            }) + RowGap;

            rowY = AddToggle(_gameplay, x, rowY, width, "auto-advance", Loc("ui.settings.auto_advance"), _model.AutoAdvance, _model.SetAutoAdvance);
            rowY = AddToggle(_gameplay, x, rowY, width, "team-suggestions", Loc("ui.settings.team_suggestions"), _model.TeamSuggestionsEnabled, _model.SetTeamSuggestions);
            rowY = AddToggle(_gameplay, x, rowY, width, "tutorial-hints", Loc("ui.settings.tutorial_hints"), _model.TutorialHints, _model.SetTutorialHints);
            if (_model.NotificationsAvailable)
            {
                rowY = AddToggle(_gameplay, x, rowY, width, "idle-alert", Loc("ui.settings.idle_alert"), _model.IdleNotifications, _model.SetIdleNotifications);
                AddToggle(_gameplay, x, rowY, width, "grove-alert", Loc("ui.settings.grove_alert"), _model.GroveNotifications, _model.SetGroveNotifications);
            }

            _gameplay.End(y);
        }

        // ------------------------------------------------------------------------------------------
        // Visuals & accessibility: effects intensity, screen shake, flashes.
        // ------------------------------------------------------------------------------------------

        private void BuildVisuals()
        {
            _visuals.Begin();
            float y = 10f;
            float width = _visuals.Width - 72f;
            float x = HeaderMetrics.Pad + 36f;
            float height = CardTopPad + ChipRow.Height + RowGap + 2 * (Toggle.Height + RowGap) + CardBottomPad;
            float top = y;
            y = _visuals.Card(top, height, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.settings.section_visuals")));

            float rowY = top + CardTopPad;
            _visuals.Add(new Label { Bounds = new Rect(x, rowY - 44f, width, 36f), Text = Loc("ui.settings.effects"), Size = Ctx.Style.TextSizes.Small + 2f, ColorKey = "inkSoft" });
            string[] intensities = { Loc("ui.settings.effects_full"), Loc("ui.settings.effects_reduced"), Loc("ui.settings.effects_minimal") };
            rowY = ChipRow.Build(Ctx, _visuals.Scroll, x, rowY, width, intensities, (int)_model.EffectsIntensity, "effects-", i =>
            {
                _model.SetEffectsIntensity((EffectsIntensity)i);
                Build();
            }) + RowGap;

            rowY = AddToggle(_visuals, x, rowY, width, "screen-shake", Loc("ui.settings.screen_shake"), _model.ScreenShake, _model.SetScreenShake);
            AddToggle(_visuals, x, rowY, width, "flashes", Loc("ui.settings.flashes"), _model.Flashes, _model.SetFlashes);

            if (_backdrops != null && _backdrops.Rows.Count > 0)
            {
                y = BuildBackdropPicker(y);
            }

            _visuals.End(y);
        }

        // ------------------------------------------------------------------------------------------
        // Backdrop (#52 step 3, journal UI kit): a tap-to-select thumbnail per region's menu backdrop
        // (ui/backdrop/<id>, drawn behind every out-of-combat screen, GameScreen.PageBackground).
        // Reached regions (CampaignProgress.ReachedBackdropIds, unlocked on first entry,
        // CampaignRules.StartRun) are tappable; a still-locked region shows as a silhouette with its
        // name and no tap target.
        // ------------------------------------------------------------------------------------------

        private const int BackdropColumns = 3;
        private const float BackdropGap = 16f;

        private float BuildBackdropPicker(float top)
        {
            float width = _visuals.Width - 72f;
            float x = HeaderMetrics.Pad + 36f;
            float cellWidth = (width - (BackdropColumns - 1) * BackdropGap) / BackdropColumns;
            float cellHeight = cellWidth * 1.3f;
            int rows = (_backdrops.Rows.Count + BackdropColumns - 1) / BackdropColumns;
            float gridHeight = rows * cellHeight + Math.Max(0, rows - 1) * BackdropGap;
            float height = CardTopPad + gridHeight + CardBottomPad;
            float cardTop = top;
            float y = _visuals.Card(cardTop, height, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.settings.section_backdrop")));

            float gridTop = cardTop + CardTopPad;
            for (int i = 0; i < _backdrops.Rows.Count; i++)
            {
                BackdropRow row = _backdrops.Rows[i];
                int col = i % BackdropColumns;
                int line = i / BackdropColumns;
                Rect cell = new Rect(x + col * (cellWidth + BackdropGap), gridTop + line * (cellHeight + BackdropGap), cellWidth, cellHeight);
                Button button = _visuals.Add(new Button { Id = "backdrop-" + row.RegionId, Bounds = cell, StyleKey = "chip", Selected = row.Selected, Enabled = row.Reached });
                string regionId = row.RegionId;
                button.Clicked += () =>
                {
                    _backdrops.Select(regionId);
                    Build();
                };
                _visuals.TrackDraw(button, box => DrawBackdropThumb(box, row));
            }

            return y;
        }

        /// <summary>
        /// A backdrop thumbnail: the painted scene (dimmed to a silhouette when locked), its region name
        /// on a plaque strip, a lock glyph over a locked one, and — since the chip button's own selected
        /// border sits under the art and would otherwise be hidden — an explicit gold ring and check
        /// badge over a selected one.
        /// </summary>
        private void DrawBackdropThumb(Rect box, BackdropRow row)
        {
            Rect inset = box.Inset(6f);
            ArtSprite art = Painter.Sprite("ui/backdrop/" + row.RegionId);
            if (art != null)
            {
                Color tint = row.Reached ? Color.White : new Color(96, 88, 108, 255);
                Painter.NineSlice(art, inset, tint);
            }

            Rect nameStrip = new Rect(inset.X, inset.Bottom - 40f, inset.Width, 40f);
            Painter.Fill(nameStrip, Painter.C("inkPlum2", 0.6f));
            Painter.TextIn(row.DisplayName, nameStrip.Inset(6f), Ctx.Style.TextSizes.Small, Painter.C("cream"), TextAlign.Center);

            if (!row.Reached)
            {
                float size = Math.Min(inset.Width, inset.Height) * 0.3f;
                Painter.Glyph("lock", new Rect(inset.Center.X - size / 2f, inset.Center.Y - size / 2f - 20f, size, size), Painter.C("cream"));
                return;
            }

            if (row.Selected)
            {
                DrawBorder(inset, 6f, Painter.C("gold"));
                float badge = 56f;
                Rect badgeBox = new Rect(inset.Right - badge - 10f, inset.Y + 10f, badge, badge);
                Painter.Fill(badgeBox, Painter.C("gold"));
                Painter.Glyph("check", badgeBox.Inset(8f), Painter.C("inkPlum2"));
            }
        }

        /// <summary>A rectangular outline (four fills — the kit has no stroked-rect primitive) of <paramref name="thickness"/>, just inside <paramref name="box"/>.</summary>
        private void DrawBorder(Rect box, float thickness, Color color)
        {
            Painter.Fill(new Rect(box.X, box.Y, box.Width, thickness), color);
            Painter.Fill(new Rect(box.X, box.Bottom - thickness, box.Width, thickness), color);
            Painter.Fill(new Rect(box.X, box.Y, thickness, box.Height), color);
            Painter.Fill(new Rect(box.Right - thickness, box.Y, thickness, box.Height), color);
        }

        // ------------------------------------------------------------------------------------------
        // Audio: master/music/SFX as real sliders, mute, haptics.
        // ------------------------------------------------------------------------------------------

        private void BuildAudio()
        {
            _audio.Begin();
            float y = 10f;
            float width = _audio.Width - 72f;
            float x = HeaderMetrics.Pad + 36f;
            int toggles = 1 + (_model.HapticsAvailable ? 1 : 0);
            float height = CardTopPad + 3 * (Slider.Height + RowGap) + toggles * (Toggle.Height + RowGap) + CardBottomPad;
            float top = y;
            y = _audio.Card(top, height, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.settings.section_audio")));

            float rowY = top + CardTopPad;
            rowY = AddSlider(_audio, x, rowY, width, "master-volume", Loc("ui.settings.master_volume"), _model.MasterVolume, _model.SetMasterVolumeLive, _model.SetMasterVolume);
            rowY = AddSlider(_audio, x, rowY, width, "music-volume", Loc("ui.settings.music_volume"), _model.MusicVolume, _model.SetMusicVolumeLive, _model.SetMusicVolume);
            rowY = AddSlider(_audio, x, rowY, width, "sfx-volume", Loc("ui.settings.sfx_volume"), _model.SfxVolume, _model.SetSfxVolumeLive, _model.SetSfxVolume);
            rowY = AddToggle(_audio, x, rowY, width, "mute", Loc("ui.settings.mute"), _model.Muted, _model.SetMuted);
            if (_model.HapticsAvailable)
            {
                AddToggle(_audio, x, rowY, width, "haptics", Loc("ui.settings.haptics"), _model.Haptics, _model.SetHaptics);
            }

            _audio.End(y);
        }

        // ------------------------------------------------------------------------------------------
        // Privacy (#62): analytics and crash-report consent, with a short explanation.
        // ------------------------------------------------------------------------------------------

        private void BuildPrivacy()
        {
            _privacy.Begin();
            float y = 10f;
            float width = _privacy.Width - 72f;
            float x = HeaderMetrics.Pad + 36f;
            string body = Loc("ui.consent.body");
            System.Collections.Generic.List<string> lines = Ctx.Painter.Wrap(body, Ctx.Style.TextSizes.Small + 1f, width);
            float bodyHeight = lines.Count * Ctx.Text.LineHeight(Ctx.Style.TextSizes.Small + 1f);
            float height = CardTopPad + bodyHeight + RowGap + 2 * (Toggle.Height + RowGap) + CardBottomPad;
            float top = y;
            y = _privacy.Card(top, height, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.settings.section_privacy")));

            float rowY = top + CardTopPad;
            _privacy.Add(new Label { Bounds = new Rect(x, rowY, width, bodyHeight), Text = body, Size = Ctx.Style.TextSizes.Small + 1f, ColorKey = "inkSoft", Wrap = true });
            rowY += bodyHeight + RowGap;
            rowY = AddToggle(_privacy, x, rowY, width, "analytics", Loc("ui.settings.analytics"), _model.AnalyticsConsent, _model.SetAnalyticsConsent);
            AddToggle(_privacy, x, rowY, width, "crash-reports", Loc("ui.settings.crash_reports"), _model.CrashReportConsent, _model.SetCrashReportConsent);

            _privacy.End(y);
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>A <see cref="Components.Toggle"/> row: builds it, saves and rebuilds the tab on a tap.</summary>
        private float AddToggle(CardList list, float x, float y, float width, string id, string label, bool on, Action<bool> set)
        {
            Hotspot hotspot = Toggle.Build(list.Scroll, id, x, y, width, on, newOn =>
            {
                set(newOn);
                Build();
            });
            list.TrackDraw(hotspot, box => Toggle.Draw(Ctx, box, label, on, Loc("ui.settings.on"), Loc("ui.settings.off")));
            return y + Toggle.Height + RowGap;
        }

        /// <summary>
        /// A <see cref="Components.Slider"/> row: <paramref name="setLive"/> applies the exact value as
        /// it drags (so the player hears the volume move at once) but never saves;
        /// <paramref name="setFinal"/> commits — and saves — the value the drag settled on, once, on
        /// release (<see cref="SliderBar.Dropped"/>, inherited from <see cref="Draggable"/>) or on a
        /// plain tap (<see cref="SliderBar.Clicked"/>, which never raises <c>Dropped</c>) — code review:
        /// saving on every drag frame was needless I/O. Never rebuilds the tab mid-drag — the bar reads
        /// its own live <see cref="SliderBar.Value"/> at <see cref="Components.Slider.Draw"/> time.
        /// </summary>
        private float AddSlider(CardList list, float x, float y, float width, string id, string label, int percent, Action<int> setLive, Action<int> setFinal)
        {
            SliderBar bar = Slider.Build(list.Scroll, id, x, y, width, SettingsViewModel.Fraction(percent), v => setLive((int)Math.Round(v * 100f)));
            void Commit()
            {
                setFinal((int)Math.Round(bar.Value * 100f));
            }

            bar.Dropped += _ => Commit();
            bar.Clicked += Commit;
            list.TrackDraw(bar, _ => Slider.Draw(Ctx, bar, label, Loc("ui.settings.volume_value", (int)Math.Round(bar.Value * 100f))));
            return y + Slider.Height + RowGap;
        }

        public override void Draw()
        {
            PageBackground("cream", "parchment");
            base.Draw();
            _header.Paint(Ctx, Ui, Loc("ui.settings.title"));
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_gameplay.TryDraw(widget, out Action<Rect> gameplay))
            {
                gameplay(widget.Bounds);
            }
            else if (_visuals.TryDraw(widget, out Action<Rect> visuals))
            {
                visuals(widget.Bounds);
            }
            else if (_audio.TryDraw(widget, out Action<Rect> audio))
            {
                audio(widget.Bounds);
            }
            else if (_privacy.TryDraw(widget, out Action<Rect> privacy))
            {
                privacy(widget.Bounds);
            }
        }
    }
}
