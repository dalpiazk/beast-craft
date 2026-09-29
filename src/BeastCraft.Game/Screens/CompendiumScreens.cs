using System;
using System.Collections.Generic;
using BeastCraft.Discovery;
using BeastCraft.Economy;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The compendium screen (<see cref="CompendiumViewModel"/>, the Collector persona): every roster
    /// species as a card (an unknown silhouette, a live Kinship offer, or owned — and, through Kinship,
    /// at which site), every lore entry (found: title and text; not found: a locked placeholder), and
    /// the combined completion percent. Reached from the Roster tab's "Compendium" chip.
    /// </summary>
    public sealed class CompendiumScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float TopBar = 190f;
        private const float CardHeight = 320f;

        private readonly CompendiumViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, CompendiumBeastRow> _beastCards = new Dictionary<Widget, CompendiumBeastRow>();
        private readonly Dictionary<Widget, CompendiumLoreRow> _loreCards = new Dictionary<Widget, CompendiumLoreRow>();

        public CompendiumScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new CompendiumViewModel(ctx.Session);
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            Build();
        }

        public override string Name
        {
            get { return "compendium"; }
        }

        public CompendiumViewModel Model
        {
            get { return _model; }
        }

        public override void Enter()
        {
            base.Enter();
            _model.Refresh();
            Build();
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _beastCards.Clear();
            _loreCards.Clear();
            UiStyle style = Ctx.Style;
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float cardWidth = (width - 2f * 20f) / 3f;
            float y = 10f;
            AddLabel(_scroll, new Rect(Pad, y, 400f, style.TextSizes.Heading - 4f), "Beasts", style.TextSizes.Heading - 4f, "plum");
            y += Ctx.Text.LineHeight(style.TextSizes.Heading - 4f) + 20f;
            for (int i = 0; i < _model.Beasts.Count; i++)
            {
                CompendiumBeastRow row = _model.Beasts[i];
                Rect card = new Rect(Pad + (i % 3) * (cardWidth + 20f), y + (i / 3) * (CardHeight + 20f), cardWidth, CardHeight);
                string panelStyle = row.State == CompendiumBeastState.Offered ? "banner" : row.State == CompendiumBeastState.Owned ? "card" : "slot";
                Panel panel = _scroll.Add(new Panel { Bounds = card, StyleKey = panelStyle });
                _beastCards[panel] = row;
            }

            y += ((_model.Beasts.Count + 2) / 3) * (CardHeight + 20f) + 30f;
            AddLabel(_scroll, new Rect(Pad, y, 400f, style.TextSizes.Heading - 4f), "Lore", style.TextSizes.Heading - 4f, "plum");
            y += Ctx.Text.LineHeight(style.TextSizes.Heading - 4f) + 20f;
            foreach (CompendiumLoreRow lore in _model.Lore)
            {
                int lines = Painter.Wrap(lore.Text, style.TextSizes.Body, width - 80f).Count;
                float height = 110f + lines * Ctx.Text.LineHeight(style.TextSizes.Body);
                Panel panel = _scroll.Add(new Panel { Bounds = new Rect(Pad, y, width, height), StyleKey = lore.Found ? "card" : "slot" });
                _loreCards[panel] = lore;
                y += height + 20f;
            }

            _scroll.ContentHeight = y + 30f;
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            UiStyle style = Ctx.Style;
            Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, TopBar), Painter.C("cream"));
            Painter.Fill(new Rect(0, TopBar - 5f, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.Paint(Ui.Find("back"), Ui);
            Painter.TextIn("Compendium", new Rect(180f, 44f, 600f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
            CompendiumCompletion completion = _model.Completion;
            if (completion == null)
            {
                return;
            }

            Painter.Progress(new Rect(180f, 118f, PortraitLayout.CanvasWidth - 260f, 34f), completion.Percent / 100f, -1f, "gold", "gold", "track", completion.Percent + "% complete");
            string sub = completion.BeastsOwned + "/" + completion.BeastsTotal + " beasts  -  " + completion.LoreFound + "/" + completion.LoreTotal + " lore  -  " +
                         completion.KinshipClaimed + "/" + completion.KinshipTotal + " kinship";
            Painter.TextIn(sub, new Rect(180f, 158f, PortraitLayout.CanvasWidth - 260f, 28f), style.TextSizes.Small + 2f, Painter.C("inkSoft"), TextAlign.Left);
        }

        protected override void DrawCustom(Widget widget)
        {
            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            if (_beastCards.TryGetValue(widget, out CompendiumBeastRow beast))
            {
                Rect card = widget.Bounds;
                Rect portrait = new Rect(card.X + 16f, card.Y + 14f, card.Width - 32f, 150f);
                painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 6f), 60f, painter.C("plum", 0.25f), 0.3f);
                if (beast.State == CompendiumBeastState.Unknown)
                {
                    painter.Art(painter.Sprite(beast.ArtKey), portrait, false, new Microsoft.Xna.Framework.Color(34, 22, 38, 225));
                }
                else
                {
                    painter.Art(painter.Sprite(beast.ArtKey), portrait, false);
                }

                painter.TextIn(beast.Name, new Rect(card.X + 10f, card.Y + 172f, card.Width - 20f, 34f), style.TextSizes.Body, painter.C(beast.State == CompendiumBeastState.Unknown ? "plumSoft" : "ink"),
                               TextAlign.Center);
                if (beast.State != CompendiumBeastState.Unknown)
                {
                    painter.ElementBadge(beast.Element, new Rect(card.Center.X - 20f, card.Y + 210f, 40f, 40f));
                }

                float hintY = card.Y + 256f;
                foreach (string line in painter.Wrap(beast.Hint, style.TextSizes.Small - 1f, card.Width - 24f))
                {
                    if (hintY + 22f > card.Bottom - 4f)
                    {
                        break;
                    }

                    painter.TextIn(line, new Rect(card.X + 10f, hintY, card.Width - 20f, 22f), style.TextSizes.Small - 1f, painter.C("inkSoft"), TextAlign.Center);
                    hintY += 24f;
                }

                return;
            }

            if (_loreCards.TryGetValue(widget, out CompendiumLoreRow lore))
            {
                Rect box = widget.Bounds;
                painter.TextIn(lore.Title, new Rect(box.X + 36f, box.Y + 24f, box.Width - 72f, style.TextSizes.Body + 4f), style.TextSizes.Body + 4f,
                               painter.C(lore.Found ? "plum" : "plumSoft"), TextAlign.Left);
                float y = box.Y + 76f;
                foreach (string line in painter.Wrap(lore.Text, style.TextSizes.Body, box.Width - 72f))
                {
                    painter.TextIn(line, new Rect(box.X + 36f, y, box.Width - 72f, style.TextSizes.Body), style.TextSizes.Body, painter.C(lore.Found ? "ink" : "inkSoft"), TextAlign.Left, false);
                    y += Ctx.Text.LineHeight(style.TextSizes.Body);
                }
            }
        }
    }

    /// <summary>
    /// The achievements screen (<see cref="AchievementsViewModel"/>, the Collector persona): the
    /// Beastbinder's level and equipped title, the title picker (every owned title plus "No title"),
    /// and every achievement — earned or not — with its condition in words and the title it awards.
    /// Reached from the Avatar tab.
    /// </summary>
    public sealed class AchievementsScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float TopBar = 250f;
        private const float ChipHeight = 76f;

        private readonly AchievementsViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, AchievementRow> _rows = new Dictionary<Widget, AchievementRow>();

        public AchievementsScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new AchievementsViewModel(ctx.Session);
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            Build();
        }

        public override string Name
        {
            get { return "achievements"; }
        }

        public AchievementsViewModel Model
        {
            get { return _model; }
        }

        public override void Enter()
        {
            base.Enter();
            _model.Refresh();
            Build();
        }

        /// <summary>Equips <paramref name="titleId"/> ("" clears it) and rebuilds.</summary>
        public void Equip(string titleId)
        {
            _model.Equip(titleId);
            Build();
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _rows.Clear();
            UiStyle style = Ctx.Style;
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float y = 10f;
            AddLabel(_scroll, new Rect(Pad, y, 400f, style.TextSizes.Heading - 4f), "Titles", style.TextSizes.Heading - 4f, "plum");
            y += Ctx.Text.LineHeight(style.TextSizes.Heading - 4f) + 16f;
            float x = Pad;
            float chipSize = Ctx.Style.Button("chip").TextSize;
            foreach (TitleRow title in _model.Titles)
            {
                float w = Math.Min(width, Ctx.Text.Measure(title.Text, chipSize) + 60f);
                if (x + w > Pad + width)
                {
                    x = Pad;
                    y += ChipHeight + 14f;
                }

                string id = title.TitleId;
                Button chip = AddButton(_scroll, "title-" + (string.IsNullOrEmpty(id) ? "none" : id), new Rect(x, y, w, ChipHeight), title.Text, "chip", () => Equip(id));
                chip.Selected = title.Equipped;
                x += w + 14f;
            }

            y += ChipHeight + 30f;
            AddLabel(_scroll, new Rect(Pad, y, 500f, style.TextSizes.Heading - 4f), "Achievements", style.TextSizes.Heading - 4f, "plum");
            y += Ctx.Text.LineHeight(style.TextSizes.Heading - 4f) + 16f;
            foreach (AchievementRow row in _model.Achievements)
            {
                int lines = Painter.Wrap(row.ConditionText, style.TextSizes.Body - 1f, width - 132f).Count;
                float height = 170f + Math.Max(0, lines - 1) * Ctx.Text.LineHeight(style.TextSizes.Body - 1f);
                Panel panel = _scroll.Add(new Panel { Bounds = new Rect(Pad, y, width, height), StyleKey = row.Earned ? "card" : "slot" });
                _rows[panel] = row;
                y += height + 18f;
            }

            _scroll.ContentHeight = y + 30f;
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            UiStyle style = Ctx.Style;
            Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, TopBar), Painter.C("cream"));
            Painter.Fill(new Rect(0, TopBar - 5f, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.Paint(Ui.Find("back"), Ui);
            Painter.TextIn("Achievements", new Rect(180f, 44f, 600f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
            Painter.TextIn(_model.AvatarDisplayName + "  -  Level " + _model.AvatarLevel, new Rect(180f, 116f, PortraitLayout.CanvasWidth - 260f, 34f), style.TextSizes.Body + 2f,
                           Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(_model.EarnedCount + " / " + _model.Achievements.Count + " earned", new Rect(180f, 160f, PortraitLayout.CanvasWidth - 260f, 28f), style.TextSizes.Small + 2f,
                           Painter.C("inkSoft"), TextAlign.Left);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (!_rows.TryGetValue(widget, out AchievementRow row))
            {
                return;
            }

            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            Rect box = widget.Bounds;
            painter.Glyph(row.Earned ? "check" : "lock", new Rect(box.X + 24f, box.Y + 26f, 52f, 52f), painter.C(row.Earned ? "leafDeep" : "inkSoft"));
            painter.TextIn(row.DisplayName, new Rect(box.X + 96f, box.Y + 22f, box.Width - 132f, style.TextSizes.Body + 2f), style.TextSizes.Body + 2f,
                           painter.C(row.Earned ? "plum" : "ink"), TextAlign.Left);
            painter.TextIn("Awards: " + row.TitleText, new Rect(box.X + 96f, box.Y + 64f, box.Width - 132f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f,
                           painter.C("goldDeep"), TextAlign.Left);
            float y = box.Y + 112f;
            foreach (string line in painter.Wrap(row.ConditionText, style.TextSizes.Body - 1f, box.Width - 132f))
            {
                painter.TextIn(line, new Rect(box.X + 96f, y, box.Width - 132f, style.TextSizes.Body - 1f), style.TextSizes.Body - 1f, painter.C("inkSoft"), TextAlign.Left, false);
                y += Ctx.Text.LineHeight(style.TextSizes.Body - 1f);
            }
        }
    }

    /// <summary>
    /// The look-token shop (<see cref="LookTokenShopViewModel"/>): the token balance and every
    /// token-purchasable look (owned, affordable, or not), bought directly with
    /// <see cref="CosmeticRules.SpendLookToken"/>. Reached from the Avatar tab and from a beast's Gear
    /// &amp; bonds tab ("Look shop", beside its worn looks).
    /// </summary>
    public sealed class LookTokenShopScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float TopBar = 210f;
        private const float RowHeight = 150f;

        private readonly LookTokenShopViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Dictionary<Widget, LookTokenRow> _rows = new Dictionary<Widget, LookTokenRow>();

        public LookTokenShopScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new LookTokenShopViewModel(ctx.Session);
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            Build();
        }

        public override string Name
        {
            get { return "look-tokens"; }
        }

        public LookTokenShopViewModel Model
        {
            get { return _model; }
        }

        public override void Enter()
        {
            base.Enter();
            _model.Refresh();
            Build();
        }

        /// <summary>Buys <paramref name="key"/>, toasts the outcome, and rebuilds.</summary>
        public void Buy(string key)
        {
            LookTokenResult result = _model.Buy(key);
            Ctx.Game.Toast(MessageFor(result));
            Build();
        }

        private static string MessageFor(LookTokenResult result)
        {
            switch (result)
            {
                case LookTokenResult.Unlocked:
                    return "Look unlocked! Wear it from the wardrobe.";
                case LookTokenResult.InsufficientTokens:
                    return "Not enough look tokens.";
                case LookTokenResult.AlreadyUsable:
                    return "You can already wear that look.";
                default:
                    return "That look cannot be bought with tokens.";
            }
        }

        private void Build()
        {
            _scroll.ClearChildren();
            _rows.Clear();
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float y = 10f;
            foreach (LookTokenRow row in _model.Looks)
            {
                Panel panel = _scroll.Add(new Panel { Bounds = new Rect(Pad, y, width, RowHeight), StyleKey = row.Owned ? "card" : "panel" });
                _rows[panel] = row;
                if (!row.Owned)
                {
                    string key = row.Key;
                    Button buy = AddButton(_scroll, "buy-" + key, new Rect(Pad + width - 230f, y + 40f, 190f, 76f), "Buy", "primary", () => Buy(key));
                    buy.Enabled = row.CanAfford;
                }

                y += RowHeight + 18f;
            }

            if (_model.Looks.Count == 0)
            {
                AddLabel(_scroll, new Rect(Pad, y, width, 40f), "No looks in the token pool yet.", Ctx.Style.TextSizes.Body, "inkSoft");
                y += 60f;
            }

            _scroll.ContentHeight = y + 30f;
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            UiStyle style = Ctx.Style;
            Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, TopBar), Painter.C("cream"));
            Painter.Fill(new Rect(0, TopBar - 5f, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.Paint(Ui.Find("back"), Ui);
            Painter.TextIn("Look tokens", new Rect(180f, 44f, 600f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"), TextAlign.Left);
            Rect coin = new Rect(180f, 118f, 48f, 48f);
            Painter.Glyph("coin", coin, Painter.C("goldDeep"));
            Painter.TextIn(_model.Balance + " look tokens", new Rect(coin.Right + 14f, coin.Y + 4f, PortraitLayout.CanvasWidth - 260f, 40f), style.TextSizes.Body + 2f, Painter.C("ink"),
                           TextAlign.Left);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (!_rows.TryGetValue(widget, out LookTokenRow row))
            {
                return;
            }

            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            Rect box = widget.Bounds;
            painter.TextIn(row.DisplayName, new Rect(box.X + 36f, box.Y + 24f, box.Width - 280f, style.TextSizes.Body + 2f), style.TextSizes.Body + 2f, painter.C("ink"), TextAlign.Left);
            painter.TextIn(row.CategoryName + "  -  " + (row.Rarity <= 0 ? "Common" : "Rare"), new Rect(box.X + 36f, box.Y + 68f, box.Width - 280f, style.TextSizes.Small + 2f),
                           style.TextSizes.Small + 2f, painter.C("inkSoft"), TextAlign.Left);
            string status = row.Owned ? "Owned" : row.Price + " tokens" + (row.CanAfford ? string.Empty : " (not enough)");
            painter.TextIn(status, new Rect(box.X + 36f, box.Y + 108f, box.Width - 280f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f,
                           painter.C(row.Owned ? "leafDeep" : row.CanAfford ? "goldDeep" : "berry"), TextAlign.Left);
        }
    }
}
