using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Creatures;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// A location's encounter (<see cref="EncounterViewModel"/>): the full preview for free (the
    /// battlefield it will be fought on, every enemy with its type, element and level), the
    /// dismissible team suggestion after repeated losses, the party picker (tap a beast to add or
    /// remove it, up to the party size, deployment order shown), one consumable at most, and Start
    /// Battle. The page scrolls under a fixed title bar and Start button.
    /// </summary>
    public sealed class EncounterScreen : GameScreen
    {
        private const float Pad = 36f;
        private const float TopBar = 190f;
        private const float BottomBar = 230f;

        private readonly EncounterViewModel _model;
        private readonly ScrollView _scroll;
        private readonly Button _start;
        private readonly Dictionary<Widget, PartyMemberView> _cards = new Dictionary<Widget, PartyMemberView>();
        private readonly Dictionary<Widget, EnemyGroupView> _enemyCards = new Dictionary<Widget, EnemyGroupView>();
        private Rect _battlefield;
        private Rect _suggestionBox;

        public EncounterScreen(ScreenContext ctx, int nodeId) : base(ctx)
        {
            _model = new EncounterViewModel(ctx.Session, nodeId);
            _scroll = Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar - BottomBar) });
            AddButton(null, "back", new Rect(Pad, 40f, 110f, 110f), null, "secondary", () => Ctx.Stack.Pop(), "back");
            _start = AddButton(null, "start", new Rect(Pad + 60f, PortraitLayout.CanvasHeight - BottomBar + 70f, PortraitLayout.CanvasWidth - 2f * Pad - 120f, 140f), "Start Battle",
                               "primary", StartBattle, "battle");
            Build();
        }

        public override string Name
        {
            get { return "encounter"; }
        }

        public EncounterViewModel Model
        {
            get { return _model; }
        }

        /// <summary>Begins the battle and shows it (or says why not).</summary>
        public void StartBattle()
        {
            NodeBattle battle = _model.Start(out string error);
            if (battle == null)
            {
                Ctx.Game.Toast(error ?? "The battle could not begin.");
                if (Ctx.Options.Screenshot || !string.IsNullOrEmpty(Ctx.Options.WalkthroughDir))
                {
                    throw new InvalidOperationException(error);
                }

                return;
            }

            Ctx.Stack.Replace(BattleScreen.Campaign(Ctx, battle, _model.Title, ShowResults));
        }

        private void ShowResults(NodeBattle battle)
        {
            ResultsViewModel results = battle.Complete();
            Console.WriteLine("Results: " + results.Outcome + " at " + results.Subtitle + " (" + results.MapOutcome + ", seed " + battle.Seed + ").");
            Ctx.Stack.Replace(new ResultsScreen(Ctx, results));
        }

        /// <summary>Lays the page out (again, after a pick changes what shows).</summary>
        private void Build()
        {
            float scrollY = _scroll.ScrollY;
            _scroll.ClearChildren();
            _cards.Clear();
            _enemyCards.Clear();
            UiStyle style = Ctx.Style;
            float width = PortraitLayout.CanvasWidth - 2f * Pad;
            float y = 10f;

            if (_model.Battle == null)
            {
                AddLabel(_scroll, new Rect(Pad, y, width, 200f), _model.Error, style.TextSizes.Body, "berry", TextAlign.Center, true);
                _start.Enabled = false;
                return;
            }

            // The battlefield.
            _battlefield = new Rect(Pad, y, width, 330f);
            _scroll.Add(new Panel { Bounds = _battlefield, StyleKey = "card" });
            y = _battlefield.Bottom + 34f;

            // The enemies.
            int total = 0;
            foreach (EnemyGroupView group in _model.Enemies)
            {
                total += group.Count;
            }

            AddLabel(_scroll, new Rect(Pad, y, width, 40f), "Enemies (" + total + ")", style.TextSizes.Heading - 4f, "plum");
            y += 60f;
            float enemyWidth = (width - 24f) / 2f;
            for (int i = 0; i < _model.Enemies.Count; i++)
            {
                Rect card = new Rect(Pad + (i % 2) * (enemyWidth + 24f), y + (i / 2) * 184f, enemyWidth, 164f);
                Panel panel = _scroll.Add(new Panel { Bounds = card, StyleKey = "slot" });
                _enemyCards[panel] = _model.Enemies[i];
            }

            y += ((_model.Enemies.Count + 1) / 2) * 184f + 20f;

            // The suggestion banner.
            if (_model.Suggestion != null)
            {
                _suggestionBox = new Rect(Pad, y, width, 200f);
                _scroll.Add(new Panel { Bounds = _suggestionBox, StyleKey = "banner" });
                AddLabel(_scroll, new Rect(Pad + 30f, y + 22f, width - 140f, 36f), "Stuck here? Try this team", style.TextSizes.Body + 4f, "plum");
                AddLabel(_scroll, new Rect(Pad + 30f, y + 70f, width - 60f, 30f), string.Join(", ", _model.Suggestion.Names), style.TextSizes.Body, "ink");
                AddButton(_scroll, "use-suggestion", new Rect(Pad + 30f, y + 118f, 260f, 64f), "Use team", "primary", () =>
                {
                    _model.ApplySuggestion();
                    Build();
                });
                AddButton(_scroll, "dismiss-suggestion", new Rect(_suggestionBox.Right - 96f, y + 18f, 72f, 72f), null, "ghost", () =>
                {
                    _model.DismissSuggestion();
                    Build();
                }, "close");
                y += 230f;
            }

            // The party.
            AddLabel(_scroll, new Rect(Pad, y, width, 40f), "Your party (" + _model.Team.Count + "/" + _model.PartySize + ")", style.TextSizes.Heading - 4f, "plum");
            AddLabel(_scroll, new Rect(Pad, y + 4f, width, 36f), "Tap to add or remove", style.TextSizes.Small + 2f, "inkSoft", TextAlign.Right);
            y += 60f;
            float cardWidth = (width - 2f * 20f) / 3f;
            for (int i = 0; i < _model.Owned.Count; i++)
            {
                PartyMemberView member = _model.Owned[i];
                Rect card = new Rect(Pad + (i % 3) * (cardWidth + 20f), y + (i / 3) * 300f, cardWidth, 280f);
                Button button = _scroll.Add(new Button { Id = "beast-" + member.BeastId, Bounds = card, StyleKey = "chip", Selected = member.Selected });
                button.Clicked += () => ToggleMember(member.BeastId);
                _cards[button] = member;
            }

            y += ((_model.Owned.Count + 2) / 3) * 300f + 20f;

            // The consumable.
            AddLabel(_scroll, new Rect(Pad, y, width, 40f), "Consumable (one per battle)", style.TextSizes.Heading - 4f, "plum");
            y += 60f;
            if (_model.Consumables.Count == 0)
            {
                AddLabel(_scroll, new Rect(Pad, y, width, 40f), "None held yet: traders sell them.", style.TextSizes.Body, "inkSoft");
                y += 60f;
            }
            else
            {
                float x = Pad;
                foreach (ConsumableView consumable in _model.Consumables)
                {
                    string text = consumable.Name + " x" + consumable.Quantity.ToString(CultureInfo.InvariantCulture);
                    float w = Math.Min(width, Ctx.Text.Measure(text, style.Button("chip").TextSize) + 60f);
                    if (x + w > Pad + width)
                    {
                        x = Pad;
                        y += 96f;
                    }

                    string id = consumable.ConsumableId;
                    Button chip = AddButton(_scroll, "consumable-" + id, new Rect(x, y, w, 80f), text, "chip", () =>
                    {
                        _model.ToggleConsumable(id);
                        Build();
                    });
                    chip.Selected = consumable.Selected;
                    x += w + 16f;
                }

                y += 110f;
            }

            _scroll.ContentHeight = y + 20f;
            _scroll.ScrollTo(scrollY);
            _start.Enabled = _model.CanStart;
        }

        private void ToggleMember(string beastId)
        {
            if (!_model.ToggleMember(beastId, out string message))
            {
                Ctx.Game.Toast(message);
                return;
            }

            Build();
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();

            // The title bar over the page.
            Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, TopBar), Painter.C("cream"));
            Painter.Fill(new Rect(0, TopBar - 5f, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            Painter.Paint(Ui.Find("back"), Ui);
            UiStyle style = Ctx.Style;
            Painter.TextIn(_model.Title ?? "Encounter", new Rect(180f, 44f, PortraitLayout.CanvasWidth - 220f, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Painter.C("plum"),
                           TextAlign.Left);
            if (_model.Battle != null)
            {
                string sub = _model.KindLabel + "  -  Lv " + _model.Level + "  -  " + _model.Arena + " arena" + (_model.Attempt > 0 ? "  -  losses here: " + _model.Attempt : string.Empty);
                Painter.TextIn(sub, new Rect(180f, 112f, PortraitLayout.CanvasWidth - 220f, 30f), style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left);
            }

            // The start bar.
            Rect bar = new Rect(0, PortraitLayout.CanvasHeight - BottomBar, PortraitLayout.CanvasWidth, BottomBar);
            Painter.Fill(bar, Painter.C("cream"));
            Painter.Fill(new Rect(0, bar.Y, PortraitLayout.CanvasWidth, 5f), Painter.C("plumSoft", 0.5f));
            TeamValidation validation = _model.Validate();
            Painter.TextIn(validation.Ok ? _model.Team.Count + " beast" + (_model.Team.Count == 1 ? string.Empty : "s") + (_model.SelectedConsumable != null ? " + 1 consumable" : string.Empty)
                                         : validation.Message,
                           new Rect(0, bar.Y + 18f, PortraitLayout.CanvasWidth, 30f), style.TextSizes.Body, Painter.C(validation.Ok ? "inkSoft" : "berry"), TextAlign.Center);
            Painter.Paint(_start, Ui);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (widget == _scroll)
            {
                return;
            }

            UiStyle style = Ctx.Style;
            if (widget is Panel && widget.Bounds.Equals(_battlefield) && _model.Battle != null)
            {
                DrawBattlefield(widget.Bounds);
                return;
            }

            if (_enemyCards.TryGetValue(widget, out EnemyGroupView enemy))
            {
                Rect card = widget.Bounds;
                Rect portrait = new Rect(card.X + 14f, card.Y + 14f, 136f, 136f);
                Painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 8f), 60f, Painter.C("plum", 0.25f), 0.3f);
                Painter.Art(Painter.Sprite(enemy.ArtKey), portrait, true);
                float x = portrait.Right + 14f;
                float w = card.Right - x - 14f;
                Painter.TextIn(enemy.Name + (enemy.Count > 1 ? "  x" + enemy.Count : string.Empty), new Rect(x, card.Y + 22f, w, 30f), style.TextSizes.Body, Painter.C("ink"),
                               TextAlign.Left);
                Painter.ElementBadge(enemy.Element, new Rect(x, card.Y + 66f, 44f, 44f));
                Painter.TextIn(enemy.Element == Element.None ? "No element" : enemy.Element.ToString(), new Rect(x + 54f, card.Y + 72f, w - 54f, 30f), style.TextSizes.Small + 2f,
                               Painter.C("inkSoft"), TextAlign.Left);
                string stance = enemy.Stance.HasValue ? enemy.Stance.Value.ToString() : "?";
                Painter.TextIn("Lv " + enemy.Level + "  -  " + stance, new Rect(x, card.Y + 118f, w, 30f), style.TextSizes.Small + 2f, Painter.C("plum"), TextAlign.Left);
                return;
            }

            if (_cards.TryGetValue(widget, out PartyMemberView member))
            {
                Rect card = widget.Bounds;
                Rect portrait = new Rect(card.X + 20f, card.Y + 16f, card.Width - 40f, 150f);
                Painter.Soft(new Vec2(portrait.Center.X, portrait.Bottom - 6f), 70f, Painter.C("plum", 0.25f), 0.3f);
                Painter.Art(Painter.Sprite(member.ArtKey), portrait, false);
                Painter.TextIn(member.Name, new Rect(card.X + 12f, card.Y + 176f, card.Width - 24f, 30f), style.TextSizes.Body, Painter.C("ink"), TextAlign.Center);
                Painter.ElementBadge(member.Element, new Rect(card.X + 18f, card.Y + 222f, 40f, 40f));
                Painter.TextIn("Lv " + member.Level + "  " + member.Stance, new Rect(card.X + 64f, card.Y + 226f, card.Width - 76f, 30f), style.TextSizes.Small + 1f,
                               Painter.C("inkSoft"), TextAlign.Left);
                if (member.Selected)
                {
                    Vec2 badge = new Vec2(card.Right - 30f, card.Y + 30f);
                    Painter.Disc(badge, 26f, Painter.C("plum"));
                    Painter.Disc(badge, 21f, Painter.C("leaf"));
                    Painter.TextIn((member.PartyIndex + 1).ToString(CultureInfo.InvariantCulture), new Rect(badge.X - 20f, badge.Y - 14f, 40f, 28f), style.TextSizes.Body,
                                   Painter.C("white"), TextAlign.Center);
                }
            }
        }

        private void DrawBattlefield(Rect card)
        {
            UiStyle style = Ctx.Style;
            Rect image = new Rect(card.X + 18f, card.Y + 18f, 400f, card.Height - 36f);
            ArtSprite backdrop = Painter.Sprite(_model.BackdropArtKey);
            if (backdrop != null)
            {
                Painter.Fill(image, Painter.C("plumDeep"));
                Painter.Art(backdrop, image, false);
            }
            else
            {
                Painter.RoundedRect(image, 18f, Painter.C("moss"));
                Painter.Glyph("map", image.Inset(70f), Painter.C("plum", 0.6f));
            }

            float x = image.Right + 30f;
            float w = card.Right - x - 24f;
            Painter.TextIn("Battlefield", new Rect(x, card.Y + 36f, w, 36f), style.TextSizes.Heading - 6f, Painter.C("plum"), TextAlign.Left);
            Painter.TextIn(_model.LayoutName ?? "Open ground", new Rect(x, card.Y + 92f, w, 30f), style.TextSizes.Body, Painter.C("ink"), TextAlign.Left);
            Painter.TextIn(_model.Arena + " arena", new Rect(x, card.Y + 140f, w, 30f), style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left);
            Painter.TextIn(_model.Obstacles > 0 ? _model.Obstacles + " obstacles" : "No obstacles", new Rect(x, card.Y + 184f, w, 30f), style.TextSizes.Body, Painter.C("inkSoft"),
                           TextAlign.Left);
            if (_model.DominantElement.HasValue)
            {
                Painter.ElementBadge(_model.DominantElement.Value, new Rect(x, card.Y + 236f, 50f, 50f));
                Painter.TextIn("Mostly " + _model.DominantElement.Value, new Rect(x + 62f, card.Y + 244f, w - 62f, 30f), style.TextSizes.Body, Painter.C("ink"), TextAlign.Left);
            }
        }
    }
}
