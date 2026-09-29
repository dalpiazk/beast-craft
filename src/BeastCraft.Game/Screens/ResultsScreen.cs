using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The one consolidated results screen (<see cref="ResultsViewModel"/>): victory or defeat, each
    /// team beast's XP bar filling from before to after (level-ups called out), the bench's share,
    /// gold, drops, the first-clear bonus and banked XP, and what happened on the map (cleared, a
    /// stage or region won with its seal, or the retry note), and the battle log (every hit's damage
    /// breakdown, filterable by unit). Continue (or Back) returns to the map.
    /// </summary>
    public sealed class ResultsScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float FillMs = 1200f;

        private readonly ResultsViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, BeastResultRow> _rows = new Dictionary<Widget, BeastResultRow>();
        private readonly List<ProgressBar> _bars = new List<ProgressBar>();
        private Panel _rewards;
        private Panel _notes;
        private float _elapsedMs;

        public ResultsScreen(ScreenContext ctx, ResultsViewModel model) : base(ctx)
        {
            _model = model;
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, 330f, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - 330f - 220f) });
            AddButton(null, "continue", new Rect(Pad + 60f, PortraitLayout.CanvasHeight - 180f, PortraitLayout.CanvasWidth - 2f * Pad - 120f, 140f), "Continue", "primary", Continue);
            Build();

            // Scripted screenshots show the bars filled.
            _elapsedMs = ctx.Options.Screenshot || !string.IsNullOrEmpty(ctx.Options.WalkthroughDir) ? FillMs : 0f;
        }

        public override string Name
        {
            get { return "results"; }
        }

        public override void Enter()
        {
            base.Enter();
            ShowHints(BeastCraft.Tutorial.HintTriggers.ResultsOpen, _model.NodeId);
        }

        public ResultsViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Back to the map (it refreshes as it shows) — or, with auto-advance on after a win, on to the next encounter.</summary>
        public void Continue()
        {
            Ctx.Stack.Pop();
            (Ctx.Stack.Top as HomeScreen)?.AutoAdvance(_model.Victory);
        }

        public override bool HandleBack()
        {
            Continue();
            return true;
        }

        public override void Update(float elapsedMs, FrameInput input)
        {
            base.Update(elapsedMs, input);
            _elapsedMs += elapsedMs;
        }

        private void Build()
        {
            UiStyle style = Ctx.Style;
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float y = 10f;
            foreach (BeastResultRow row in _model.Team)
            {
                Rect box = new Rect(Pad, y, width, 180f);
                Panel panel = _scroll.Add(new Panel { Bounds = box, StyleKey = "card" });
                _rows[panel] = row;
                ProgressBar bar = _scroll.Add(new ProgressBar { Bounds = new Rect(box.X + 190f, box.Y + 112f, width - 420f, 40f), FillKey = "leaf", FromColorKey = "moss" });
                bar.Tag = row;
                _bars.Add(bar);
                y += 200f;
            }

            int lines = 2 + (_model.AvatarXp > 0 ? 1 : 0) + _model.Drops.Count + (_model.FirstClear ? 1 : 0) + (_model.FirstClearGear != null ? 1 : 0) + (_model.XpBanked > 0 ? 1 : 0) + (_model.BenchXp > 0 ? 1 : 0);
            _rewards = _scroll.Add(new Panel { Bounds = new Rect(Pad, y + 10f, width, 90f + lines * 52f), StyleKey = "panel" });
            y = _rewards.Bounds.Bottom + 30f;

            float noteHeight = 90f;
            foreach (string note in _model.Notes)
            {
                noteHeight += Math.Max(1, Painter.Wrap(note, style.TextSizes.Body, width - 80f).Count) * Ctx.Text.LineHeight(style.TextSizes.Body) + 12f;
            }

            _notes = _scroll.Add(new Panel { Bounds = new Rect(Pad, y, width, noteHeight), StyleKey = _model.Victory ? "card" : "slot" });
            y = _notes.Bounds.Bottom + 30f;

            // The whole battle's log: every hit with its damage breakdown, filterable by unit.
            AddButton(_scroll, "battle-log", new Rect(Pad + 120f, y, width - 240f, 110f), "Battle log", "secondary", () => OpenLog());
            _scroll.ContentHeight = y + 140f;
        }

        /// <summary>The post-battle log (every hit's damage breakdown, filterable by unit).</summary>
        public BattleLogModal OpenLog()
        {
            BattleLogModal modal = new BattleLogModal(Ctx, _model.Log, "Battle log: " + _model.Subtitle);
            Ctx.Stack.PushModal(modal);
            return modal;
        }

        public override void Draw()
        {
            Gradient(_model.Victory ? "title_top" : "creamDeep", _model.Victory ? "title_bottom" : "stone", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            float t = Math.Min(1f, _elapsedMs / FillMs);
            float eased = 1f - (1f - t) * (1f - t);
            foreach (ProgressBar bar in _bars)
            {
                BeastResultRow row = (BeastResultRow)bar.Tag;
                bool levelled = row.LevelsGained > 0;
                bar.From = levelled ? -1f : row.FractionBefore;
                bar.Value = levelled ? row.FractionAfter * eased : row.FractionBefore + (row.FractionAfter - row.FractionBefore) * eased;
            }

            UiStyle style = Ctx.Style;
            float titleSize = style.TextSizes.Title * 1.2f;
            Painter.TextIn(_model.Title, new Rect(0, 80f, PortraitLayout.CanvasWidth, titleSize), titleSize, Painter.C(_model.Victory ? "cream" : "berry"), TextAlign.Center, true,
                           Painter.C("plum"));
            Painter.TextIn(_model.Subtitle, new Rect(0, 200f, PortraitLayout.CanvasWidth, 40f), style.TextSizes.Body + 6f, Painter.C("plum"), TextAlign.Center);
            base.Draw();
        }

        protected override void DrawCustom(Widget widget)
        {
            UiStyle style = Ctx.Style;
            if (_rows.TryGetValue(widget, out BeastResultRow row))
            {
                Rect box = widget.Bounds;
                Rect portrait = new Rect(box.X + 16f, box.Y + 14f, 152f, 152f);
                Painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 8f), 64f, Painter.C("plum", 0.25f), 0.3f);
                Painter.Art(Painter.Sprite(Ctx.Content.Battle.GetSpecies(row.SpeciesId)?.ArtKey), portrait, false, row.KnockedOut ? new Microsoft.Xna.Framework.Color(170, 160, 170) : (Microsoft.Xna.Framework.Color?)null);
                string level = row.LevelsGained > 0 ? "Lv " + row.LevelBefore + " > " + row.LevelAfter : "Lv " + row.LevelAfter;
                Painter.TextIn(row.Name, new Rect(box.X + 190f, box.Y + 26f, 330f, 34f), style.TextSizes.Body + 4f, Painter.C("ink"), TextAlign.Left);
                Painter.TextIn(level, new Rect(box.X + 190f, box.Y + 70f, 330f, 30f), style.TextSizes.Body, Painter.C("plum"), TextAlign.Left);
                if (row.LevelsGained > 0)
                {
                    Rect badge = new Rect(box.X + 470f, box.Y + 24f, 230f, 56f);
                    Painter.Framed(badge, 28f, 4f, Painter.C("plum"), Painter.C("gold"));
                    Painter.TextIn("LEVEL UP!", badge, style.TextSizes.Body, Painter.C("plum"), TextAlign.Center);
                }

                string xp = _model.IsKinshipTrial ? "Trial" : "+" + row.XpGained.ToString(CultureInfo.InvariantCulture) + " XP";
                Painter.TextIn(xp, new Rect(box.Right - 220f, box.Y + 112f, 196f, 40f), style.TextSizes.Body, Painter.C("leafDeep"), TextAlign.Right);
                string note = row.KnockedOut ? "Knocked out (share only)" : row.Banked > 0 ? "+" + row.Banked + " banked at the limit" : row.FalloffPercent < 100 ? row.FalloffPercent + "% (out-levelled)" : null;
                if (note != null)
                {
                    Painter.TextIn(note, new Rect(box.Right - 400f, box.Y + 70f, 376f, 30f), style.TextSizes.Small + 1f, Painter.C("inkSoft"), TextAlign.Right);
                }

                return;
            }

            if (widget == _rewards)
            {
                DrawRewards(widget.Bounds);
                return;
            }

            if (widget == _notes)
            {
                Rect box = widget.Bounds;
                Painter.TextIn(_model.Victory ? "On the map" : "What now", new Rect(box.X + 40f, box.Y + 26f, box.Width - 80f, 36f), style.TextSizes.Heading - 6f, Painter.C("plum"),
                               TextAlign.Left);
                float y = box.Y + 84f;
                foreach (string note in _model.Notes)
                {
                    foreach (string line in Painter.Wrap(note, style.TextSizes.Body, box.Width - 80f))
                    {
                        Painter.TextIn(line, new Rect(box.X + 40f, y, box.Width - 80f, style.TextSizes.Body), style.TextSizes.Body, Painter.C("ink"), TextAlign.Left, false);
                        y += Ctx.Text.LineHeight(style.TextSizes.Body);
                    }

                    y += 12f;
                }
            }
        }

        private void DrawRewards(Rect box)
        {
            UiStyle style = Ctx.Style;
            Painter.TextIn("Rewards", new Rect(box.X + 40f, box.Y + 26f, box.Width - 80f, 36f), style.TextSizes.Heading - 6f, Painter.C("plum"), TextAlign.Left);
            float y = box.Y + 90f;
            float size = style.TextSizes.Body;

            void Line(string glyph, string text, string color = "ink")
            {
                Painter.Glyph(glyph, new Rect(box.X + 40f, y - 4f, 40f, 40f), Painter.C(glyph == "coin" ? "goldDeep" : "plumSoft"));
                Painter.TextIn(text, new Rect(box.X + 96f, y, box.Width - 136f, size), size, Painter.C(color), TextAlign.Left, false);
                y += 52f;
            }

            if (_model.IsKinshipTrial)
            {
                // A Kinship trial pays no XP or loot: its reward is the beast that joins.
                Line("kinship", _model.Victory ? "A beast will join you" : "The stone waits for you", _model.Victory ? "leafDeep" : "inkSoft");
                Line("seal", "Trials pay no XP or loot", "inkSoft");
                return;
            }

            Line("coin", "+" + _model.Gold + " gold  (" + _model.GoldTotal + " held)");
            if (_model.FirstClear)
            {
                Line("star", "First clear bonus!", "leafDeep");
            }

            if (_model.FirstClearGear != null)
            {
                Line("star", "First clear gear: " + _model.FirstClearGear, "leafDeep");
            }

            foreach (RewardLine drop in _model.Drops)
            {
                Line(drop.Kind == "gear" ? "inventory" : drop.Kind == "look" ? "star" : "seal", drop.Name + (drop.Quantity > 1 ? " x" + drop.Quantity : string.Empty));
            }

            if (_model.Drops.Count == 0)
            {
                Line("seal", _model.Victory ? "No drops this time" : "No drops (no clear)", "inkSoft");
            }

            if (_model.AvatarXp > 0)
            {
                Line("avatar", _model.AvatarDisplayName + " +" + _model.AvatarXp + " XP" + (_model.AvatarLevelsGained > 0 ? " (level up!)" : string.Empty), "inkSoft");
            }

            if (_model.BenchXp > 0)
            {
                Line("roster", "Bench beasts shared " + _model.BenchXp + " XP" + (_model.BenchLevelsGained > 0 ? " (+" + _model.BenchLevelsGained + " levels)" : string.Empty), "inkSoft");
            }

            if (_model.XpBanked > 0)
            {
                Line("lock", _model.XpBanked + " XP banked at the binding limit (Lv " + _model.BindingLimit + ")", "inkSoft");
            }
        }
    }
}
