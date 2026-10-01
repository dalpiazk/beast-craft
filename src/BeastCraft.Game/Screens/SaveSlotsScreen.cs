using System;
using System.Collections.Generic;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The save slot list (<see cref="SaveSlotsViewModel"/>), reached from the title (Load game, or
    /// New Game — always, producer decision, menu-screens pass #67, so picking a slot or confirming an
    /// overwrite is the one way a game starts): one card per slot. A save's primary button is Continue;
    /// a second row offers New game (over this very save, asking first — the overwrite producer review
    /// #2 asked for, so three full slots can still start a new game without deleting one first), Delete
    /// (asks first) and Export/Import where the host can move files (<see cref="ViewerHost.SaveTransfer"/>).
    /// An empty slot is a shorter card with New game as its one primary button. Importing over an
    /// existing save asks first too.
    /// </summary>
    public sealed class SaveSlotsScreen : GameScreen
    {
        private const float Pad = HeaderMetrics.Pad;
        private const float TopBar = HeaderMetrics.Standard;
        private const float CardHeight = 540f;
        private const float EmptyCardHeight = 320f;
        private const float PortraitSize = 86f;
        private const float ButtonHeight = 96f;
        private const float ButtonGap = 16f;

        private readonly SaveSlotsViewModel _model;
        private readonly ScreenHeader _header;
        private readonly CardList _cards;

        public SaveSlotsScreen(ScreenContext ctx) : base(ctx)
        {
            _model = new SaveSlotsViewModel(ctx.Session, ctx.Host?.SaveTransfer);
            _cards = new CardList(Ui.Add(new ScrollView { Id = "page", Bounds = new Rect(0, TopBar, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - TopBar) }));
            _header = new ScreenHeader(Ui, TopBar, () => Ctx.Stack.Pop());
            Build();
        }

        public override string Name
        {
            get { return "save-slots"; }
        }

        public SaveSlotsViewModel Model
        {
            get { return _model; }
        }

        public override void Enter()
        {
            base.Enter();
            Build();
        }

        /// <summary>The primary button: Continue when the slot's save loads, else New game (an empty slot, or one that cannot be loaded).</summary>
        public void Play(SaveSlotRow row)
        {
            if (row.Readable)
            {
                LoadOutcome outcome = _model.Continue(row.Slot);
                if (!outcome.Success)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.title.not_loaded_title"), outcome.Message, null, Loc("ui.common.ok"), null));
                    return;
                }

                Ctx.Stack.Pop();
                Ctx.Stack.Push(new HomeScreen(Ctx));
                if (outcome.Message != null)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.title.restored_title"), outcome.Message, null, Loc("ui.common.ok"), null));
                }

                if (Ctx.Session.LastContinueClaim?.Message != null)
                {
                    Ctx.Game.Toast(Ctx.Session.LastContinueClaim.Message);
                }

                if (outcome.RefundMessage != null)
                {
                    Ctx.Game.Toast(outcome.RefundMessage);
                }

                return;
            }

            NewGame(row);
        }

        /// <summary>
        /// Starts a new game in <paramref name="row"/>'s slot, asking first when it replaces a save
        /// (<see cref="SaveSlotsViewModel.NewGameReplaces"/>) — an empty slot's primary button, or the
        /// "New game" row a readable save also offers (producer review #2: three full slots can still
        /// start a new game by picking one to overwrite, with a confirm, rather than deleting one first).
        /// </summary>
        public void NewGame(SaveSlotRow row)
        {
            Action start = () =>
            {
                _model.StartNew(row.Slot);
                Ctx.Stack.Pop();
                Ctx.Stack.Push(new StarterPickScreen(Ctx));
            };
            if (_model.NewGameReplaces(row.Slot))
            {
                Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.save_slots.replace_title"), Loc("ui.save_slots.replace_body", row.Title), Loc("ui.common.cancel"), Loc("ui.save_slots.start"), start,
                                                     "danger"));
                return;
            }

            start();
        }

        /// <summary>Deletes <paramref name="row"/>'s save after asking.</summary>
        public void Delete(SaveSlotRow row)
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.save_slots.delete_title", row.Title), Loc("ui.save_slots.delete_body"), Loc("ui.common.cancel"), Loc("ui.save_slots.delete"), () =>
            {
                Ctx.Game.Toast(_model.Delete(row.Slot));
                Build();
            }, "danger"));
        }

        public void Export(SaveSlotRow row)
        {
            _model.Export(row.Slot, message => Ctx.Game.Toast(message));
        }

        /// <summary>Imports a save file into <paramref name="row"/>, asking first when it would replace a save.</summary>
        public void Import(SaveSlotRow row)
        {
            Action import = () => _model.Import(row.Slot, message =>
            {
                Ctx.Game.Toast(message);
                Build();
            });
            if (row.HasSave)
            {
                Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.save_slots.import_title", row.Title), Loc("ui.save_slots.import_body"), Loc("ui.common.cancel"), Loc("ui.save_slots.import"),
                                                     import, "danger"));
                return;
            }

            import();
        }

        private void Build()
        {
            _cards.Begin();
            float width = _cards.Width;
            float y = 10f;
            float fullWidth = width - 72f;
            float x0 = Pad + 36f;
            foreach (SaveSlotRow row in _model.Rows())
            {
                float top = y;
                float cardHeight = row.HasSave ? CardHeight : EmptyCardHeight;
                y = _cards.Card(y, cardHeight, row.Current ? "card" : row.HasSave ? "panel" : "slot", box => DrawSlot(box, row));

                if (!row.HasSave)
                {
                    // An empty slot: New game is the one primary button, no second row.
                    AddButton(_cards.Scroll, "play-" + row.Slot, new Rect(x0, top + cardHeight - ButtonHeight - 30f, fullWidth, ButtonHeight), row.PlayText, "primary", () => Play(row));
                    continue;
                }

                float primaryY = top + cardHeight - 2f * ButtonHeight - ButtonGap - 30f;
                AddButton(_cards.Scroll, "play-" + row.Slot, new Rect(x0, primaryY, fullWidth, ButtonHeight), row.PlayText, row.Readable ? "primary" : "secondary", () => Play(row));

                // The second row: New game (only a readable save, whose primary is Continue, needs a
                // separate overwrite action here — producer review #2), Delete, Export/Import.
                List<(string Id, string Text, Action Click)> chips = new List<(string, string, Action)>();
                if (row.Readable)
                {
                    chips.Add(("newgame-" + row.Slot, Loc("ui.save_slots.new_game"), () => NewGame(row)));
                }

                chips.Add(("delete-" + row.Slot, Loc("ui.save_slots.delete"), () => Delete(row)));
                if (_model.CanTransfer && row.Readable)
                {
                    chips.Add(("export-" + row.Slot, Loc("ui.save_slots.export"), () => Export(row)));
                }

                if (_model.CanTransfer)
                {
                    chips.Add(("import-" + row.Slot, Loc("ui.save_slots.import"), () => Import(row)));
                }

                float chipY = primaryY + ButtonHeight + ButtonGap;
                float chipWidth = (fullWidth - ButtonGap * (chips.Count - 1)) / chips.Count;
                float x = x0;
                foreach ((string id, string text, Action click) in chips)
                {
                    AddButton(_cards.Scroll, id, new Rect(x, chipY, chipWidth, ButtonHeight), text, "chip", click);
                    x += chipWidth + ButtonGap;
                }
            }

            _cards.End(y);
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            _header.Paint(Ctx, Ui, Loc("ui.save_slots.title"), Loc("ui.save_slots.subtitle"));
        }

        private void DrawSlot(Rect box, SaveSlotRow row)
        {
            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            painter.TextIn(row.Current ? Loc("ui.save_slots.last_played", row.Title) : row.Title, new Rect(box.X + 36f, box.Y + 26f, box.Width - 72f, style.TextSizes.Heading),
                           style.TextSizes.Heading, painter.C("plum"), TextAlign.Left);
            painter.TextIn(row.Detail, new Rect(box.X + 36f, box.Y + 96f, box.Width - 72f, style.TextSizes.Body + 2f), style.TextSizes.Body + 2f,
                           painter.C(row.HasSave && !row.Readable ? "berry" : "ink"), TextAlign.Left);
            if (!row.HasSave)
            {
                // A clearer empty-slot state: what Detail ("Empty") alone used to leave unsaid.
                painter.TextIn(Loc("ui.save_slots.empty_hint"), new Rect(box.X + 36f, box.Y + 146f, box.Width - 72f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f,
                               painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            if (!string.IsNullOrEmpty(row.Where))
            {
                painter.TextIn(row.Where, new Rect(box.X + 36f, box.Y + 146f, box.Width - 72f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f, painter.C("inkSoft"),
                               TextAlign.Left);
            }

            DrawTeamPortraits(box, row);
        }

        /// <summary>The slot's small team portrait row (the first few owned beasts' art, the same the roster cards use), under the "where saved" line.</summary>
        private void DrawTeamPortraits(Rect box, SaveSlotRow row)
        {
            if (row.TeamArtKeys == null || row.TeamArtKeys.Count == 0)
            {
                return;
            }

            UiPainter painter = Ctx.Painter;
            float x = box.X + 36f;
            float y = box.Y + 190f;
            foreach (string artKey in row.TeamArtKeys)
            {
                Rect portrait = new Rect(x, y, PortraitSize, PortraitSize);
                painter.Framed(portrait, 14f, 3f, painter.C("plumSoft"), painter.C("parchment"));
                painter.Art(painter.Sprite(artKey), portrait.Inset(6f), false);
                x += PortraitSize + 14f;
            }
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_cards.TryDraw(widget, out Action<Rect> draw))
            {
                draw(widget.Bounds);
            }
        }
    }
}
