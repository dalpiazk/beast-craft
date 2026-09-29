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
    /// The save slot list (<see cref="SaveSlotsViewModel"/>), reached from the title: one card per slot
    /// with Continue (or New game for an empty slot), Delete, and Export/Import when the host can move
    /// files (<see cref="ViewerHost.SaveTransfer"/>). Replacing a save (a new game or an import over it)
    /// and deleting one each ask first.
    /// </summary>
    public sealed class SaveSlotsScreen : GameScreen
    {
        private const float Pad = HeaderMetrics.Pad;
        private const float TopBar = HeaderMetrics.Standard;
        private const float CardHeight = 330f;
        private const float ButtonHeight = 96f;

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

        /// <summary>Continue <paramref name="row"/>'s save, or start a new game in it (asking first when it replaces one).</summary>
        public void Play(SaveSlotRow row)
        {
            if (row.Readable)
            {
                LoadOutcome outcome = _model.Continue(row.Slot);
                if (!outcome.Success)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Save not loaded", outcome.Message, null, "OK", null));
                    return;
                }

                Ctx.Stack.Pop();
                Ctx.Stack.Push(new HomeScreen(Ctx));
                if (outcome.Message != null)
                {
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Save restored", outcome.Message, null, "OK", null));
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

            Action start = () =>
            {
                _model.StartNew(row.Slot);
                Ctx.Stack.Pop();
                Ctx.Stack.Push(new StarterPickScreen(Ctx));
            };
            if (_model.NewGameReplaces(row.Slot))
            {
                Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Start a new game?", "This replaces the save in " + row.Title + ".", "Cancel", "Start", start, "danger"));
                return;
            }

            start();
        }

        /// <summary>Deletes <paramref name="row"/>'s save after asking.</summary>
        public void Delete(SaveSlotRow row)
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Delete " + row.Title + "?", "This save and its backup are deleted for good.", "Cancel", "Delete", () =>
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
                Ctx.Stack.PushModal(new ConfirmModal(Ctx, "Import into " + row.Title + "?", "The imported save replaces this one. The file is checked first.", "Cancel", "Import",
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
            foreach (SaveSlotRow row in _model.Rows())
            {
                float top = y;
                y = _cards.Card(y, CardHeight, row.Current ? "card" : "panel", box => DrawSlot(box, row));
                List<(string Id, string Text, string Style, Action Click)> buttons = new List<(string, string, string, Action)>
                {
                    ("play-" + row.Slot, row.PlayText, row.Readable ? "primary" : "secondary", () => Play(row))
                };
                if (row.HasSave)
                {
                    buttons.Add(("delete-" + row.Slot, "Delete", "chip", () => Delete(row)));
                }

                if (_model.CanTransfer && row.Readable)
                {
                    buttons.Add(("export-" + row.Slot, "Export", "chip", () => Export(row)));
                }

                if (_model.CanTransfer)
                {
                    buttons.Add(("import-" + row.Slot, "Import", "chip", () => Import(row)));
                }

                float gap = 16f;
                float buttonWidth = (width - 72f - gap * 3f) / 4f;
                float x = Pad + 36f;
                foreach ((string id, string text, string style, Action click) in buttons)
                {
                    AddButton(_cards.Scroll, id, new Rect(x, top + CardHeight - ButtonHeight - 30f, buttonWidth, ButtonHeight), text, style, click);
                    x += buttonWidth + gap;
                }
            }

            _cards.End(y);
        }

        public override void Draw()
        {
            Gradient("cream", "parchment", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            _header.Paint(Ctx, Ui, "Save slots", "Three slots, each its own game");
        }

        private void DrawSlot(Rect box, SaveSlotRow row)
        {
            UiPainter painter = Ctx.Painter;
            UiStyle style = Ctx.Style;
            painter.TextIn(row.Title + (row.Current ? "  (last played)" : string.Empty), new Rect(box.X + 36f, box.Y + 26f, box.Width - 72f, style.TextSizes.Heading),
                           style.TextSizes.Heading, painter.C("plum"), TextAlign.Left);
            painter.TextIn(row.Detail, new Rect(box.X + 36f, box.Y + 96f, box.Width - 72f, style.TextSizes.Body + 2f), style.TextSizes.Body + 2f,
                           painter.C(row.HasSave && !row.Readable ? "berry" : "ink"), TextAlign.Left);
            if (!string.IsNullOrEmpty(row.Where))
            {
                painter.TextIn(row.Where, new Rect(box.X + 36f, box.Y + 146f, box.Width - 72f, style.TextSizes.Small + 2f), style.TextSizes.Small + 2f, painter.C("inkSoft"),
                               TextAlign.Left);
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
