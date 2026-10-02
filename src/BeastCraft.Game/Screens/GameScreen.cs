using System;
using System.Collections.Generic;
using BeastCraft.Game.Rendering;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Audio;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>What every screen draws and plays with: the host's device, renderers and content, and the game's session and stacks.</summary>
    public sealed class ScreenContext
    {
        public BeastCraftGame Game;
        public GraphicsDevice Device;
        public SpriteRenderer Draw;
        public SpriteAtlas Atlas;
        public ITextRenderer Text;
        public UiPainter Painter;
        public GameContent Content;
        public ViewerOptions Options;
        public ViewerHost Host;

        /// <summary>The player's game (null in the command-line battle demo).</summary>
        public GameSession Session;

        public ScreenStack Stack;
        public ToastQueue Toast;

        /// <summary>Sound, music and haptics (<see cref="AudioDirector"/>; silent in scripted runs).</summary>
        public AudioDirector Audio;

        /// <summary>This frame's fit of the canvas into the target (set by the host before drawing).</summary>
        public CanvasFit CanvasFit;

        /// <summary>The screens' own text by key (<c>ui.*</c> in <c>content/data/Localization/en.json</c>, <see cref="GameContent.Text"/>).</summary>
        public string Loc(string key)
        {
            return Content.Text.Get(key);
        }

        /// <summary><see cref="Loc(string)"/> with <c>{0}</c>-style arguments.</summary>
        public string Loc(string key, params object[] args)
        {
            return Content.Text.Format(key, args);
        }

        public Texture2D Pixel
        {
            get { return Atlas.Pixel; }
        }

        public UiStyle Style
        {
            get { return Painter.Style; }
        }

        /// <summary>Canvas pixels to target pixels.</summary>
        public Matrix CanvasMatrix
        {
            get { return Matrix.CreateScale(CanvasFit.Scale) * Matrix.CreateTranslation(CanvasFit.OffsetX, CanvasFit.OffsetY, 0f); }
        }
    }

    /// <summary>One frame's input, read once by the host: the keyboard, the mouse, the touches, focus.</summary>
    public sealed class FrameInput
    {
        public KeyboardState Keys;
        public MouseState Mouse;
        public TouchCollection Touches;

        /// <summary>Whether the window has focus (desktop).</summary>
        public bool IsActive;
    }

    /// <summary>
    /// A full screen of the game: an <see cref="IScreen"/> for the stack with a widget tree
    /// (<see cref="Ui"/>, its taps routed by the host) and drawing. Layout and look live here; what
    /// it shows comes from a view-model in <c>BeastCraft.Presentation.Screens</c>.
    /// </summary>
    public abstract class GameScreen : IScreen
    {
        protected GameScreen(ScreenContext ctx)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        protected ScreenContext Ctx { get; }

        /// <summary>The screens' own text by key (<c>ui.*</c> in <c>content/data/Localization/en.json</c>, <see cref="GameContent.Text"/>).</summary>
        protected string Loc(string key)
        {
            return Ctx.Content.Text.Get(key);
        }

        /// <summary><see cref="Loc(string)"/> with <c>{0}</c>-style arguments.</summary>
        protected string Loc(string key, params object[] args)
        {
            return Ctx.Content.Text.Format(key, args);
        }

        public UiRoot Ui { get; } = new UiRoot();

        public abstract string Name { get; }

        /// <summary>A screen that reads the keyboard, mouse and touches itself (the battle) rather than through <see cref="Ui"/>.</summary>
        public virtual bool UsesRawInput
        {
            get { return false; }
        }

        protected UiPainter Painter
        {
            get { return Ctx.Painter; }
        }

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
            Ui.CancelPointer();
        }

        public virtual bool HandleBack()
        {
            return false;
        }

        /// <summary>Advances the screen; <paramref name="input"/> is null while a modal has the input.</summary>
        public virtual void Update(float elapsedMs, FrameInput input)
        {
            Ui.Tick(elapsedMs);
        }

        /// <summary>Draws the whole canvas (the host has set the canvas transform).</summary>
        public virtual void Draw()
        {
            Painter.Paint(Ui, Ui, DrawCustom);
        }

        /// <summary>Called for each widget during <see cref="Draw"/>, in its children's space: what the toolkit does not draw.</summary>
        protected virtual void DrawCustom(Widget widget)
        {
        }

        /// <summary>The canvas filled top to bottom from <paramref name="top"/> to <paramref name="bottom"/> (style colour keys).</summary>
        protected void Gradient(string top, string bottom, Rect area)
        {
            const int bands = 48;
            Color a = Painter.C(top);
            Color b = Painter.C(bottom);
            float step = area.Height / bands;
            for (int i = 0; i < bands; i++)
            {
                Painter.Fill(new Rect(area.X, area.Y + i * step, area.Width, step + 1f), Color.Lerp(a, b, (i + 0.5f) / bands));
            }
        }

        /// <summary>
        /// A menu screen's full-canvas background (journal UI kit, #52 step 3): the player's selected
        /// region backdrop (<c>ui/backdrop/&lt;id&gt;</c>, <see cref="Campaign.CampaignProgress.SelectedBackdropId"/>),
        /// stretched over the canvas with a soft cream wash on top so the parchment UI kit's contrast
        /// ratios hold against a painted scene instead of a flat colour. Falls back to the plain
        /// <paramref name="top"/>/<paramref name="bottom"/> <see cref="Gradient"/> when the kit is off,
        /// there is no session yet (the title, before a save loads) or the backdrop art is missing —
        /// so every screen keeps working exactly as before whenever a backdrop is not available.
        /// </summary>
        protected void PageBackground(string top, string bottom)
        {
            Rect canvas = new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
            ArtSprite backdrop = ResolveBackdrop();
            if (backdrop == null)
            {
                Gradient(top, bottom, canvas);
                return;
            }

            Painter.NineSlice(backdrop, canvas);
            Painter.Fill(canvas, Painter.C("cream", BackdropWashAlpha));
        }

        /// <summary>How much of the cream wash <see cref="PageBackground"/> lays over the backdrop (0 = none, 1 = opaque cream).</summary>
        private const float BackdropWashAlpha = 0.55f;

        /// <summary>The selected region's backdrop art, or null when the kit is off, there is no session, or it has none.</summary>
        private ArtSprite ResolveBackdrop()
        {
            if (!UiKit.Enabled || Ctx.Session?.Save?.Campaign == null)
            {
                return null;
            }

            string id = Ctx.Session.Save.Campaign.SelectedBackdropId;
            return string.IsNullOrEmpty(id) ? null : Painter.Sprite("ui/backdrop/" + id);
        }

        /// <summary>A button added to <see cref="Ui"/> (or <paramref name="parent"/>) that runs <paramref name="onClick"/>.</summary>
        protected Button AddButton(Widget parent, string id, Rect bounds, string text, string style, Action onClick, string glyph = null)
        {
            Button button = (parent ?? Ui).Add(new Button { Id = id, Bounds = bounds, Text = text, StyleKey = style, Glyph = glyph });
            if (onClick != null)
            {
                button.Clicked += onClick;
            }

            return button;
        }

        protected Label AddLabel(Widget parent, Rect bounds, string text, float size, string color = "ink", TextAlign align = TextAlign.Left, bool wrap = false)
        {
            return (parent ?? Ui).Add(new Label { Bounds = bounds, Text = text ?? string.Empty, Size = size, ColorKey = color, Align = align, Wrap = wrap });
        }

        /// <summary>
        /// Shows the tutorial hint due for <paramref name="trigger"/> here (<see cref="HintService.Next"/>),
        /// pointing at its anchor widget on this screen; once dismissed, the next one due for the same
        /// moment follows. Nothing when hints are off, none is due, or a hint is already showing.
        /// </summary>
        public void ShowHints(string trigger, int nodeId = -1, int pickStep = 0)
        {
            if (Ctx.Session == null || (Ctx.Stack.TopModal as HintModal) != null)
            {
                return;
            }

            BeastCraft.Tutorial.HintData hint = HintService.Next(Ctx.Session, trigger, nodeId, pickStep);
            if (hint == null)
            {
                return;
            }

            Ctx.Stack.PushModal(new HintModal(Ctx, hint, AnchorRect(hint.Anchor), () => ShowHints(trigger, nodeId, pickStep)));
        }

        /// <summary>Where widget <paramref name="id"/> sits on the canvas (scrolling included), or null (no such live widget).</summary>
        public Rect? AnchorRect(string id)
        {
            Widget widget = string.IsNullOrEmpty(id) ? null : Ui.Find(id);
            if (widget == null || !widget.IsLive())
            {
                return null;
            }

            Vec2 a = widget.ToCanvas(new Vec2(widget.Bounds.X, widget.Bounds.Y));
            Vec2 b = widget.ToCanvas(new Vec2(widget.Bounds.Right, widget.Bounds.Bottom));
            return new Rect(a.X, a.Y, b.X - a.X, b.Y - a.Y);
        }

        /// <summary>Taps the widget with <paramref name="id"/> (scripted walkthroughs); false when there is none or it is not live.</summary>
        public bool TapWidget(string id)
        {
            Widget widget = Ui.Find(id);
            if (widget == null || !widget.IsLive())
            {
                return false;
            }

            Vec2 center = widget.ToCanvas(widget.Bounds.Center);
            return Ui.Tap(center) == widget;
        }
    }

    /// <summary>A modal over the top screen: a scrim, then its widgets; its taps are the only ones while it is open.</summary>
    public abstract class GameModal : IModal
    {
        protected GameModal(ScreenContext ctx)
        {
            Ctx = ctx;
        }

        protected ScreenContext Ctx { get; }

        /// <summary>The screens' own text by key (<c>ui.*</c> in <c>content/data/Localization/en.json</c>, <see cref="GameContent.Text"/>).</summary>
        protected string Loc(string key)
        {
            return Ctx.Content.Text.Get(key);
        }

        /// <summary><see cref="Loc(string)"/> with <c>{0}</c>-style arguments.</summary>
        protected string Loc(string key, params object[] args)
        {
            return Ctx.Content.Text.Format(key, args);
        }

        public UiRoot Ui { get; } = new UiRoot();

        public abstract string Name { get; }

        /// <summary>Back closes the modal unless this says otherwise.</summary>
        public virtual bool HandleBack()
        {
            return false;
        }

        public virtual void Update(float elapsedMs)
        {
            Ui.Tick(elapsedMs);
        }

        public virtual void Draw()
        {
            Ctx.Painter.Fill(new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight), Ctx.Painter.C("scrim"));
            Ctx.Painter.Paint(Ui, Ui);
        }

        public void Close()
        {
            Ctx.Stack.CloseModal(this);
        }

        public bool TapWidget(string id)
        {
            Widget widget = Ui.Find(id);
            return widget != null && widget.IsLive() && Ui.Tap(widget.ToCanvas(widget.Bounds.Center)) == widget;
        }
    }

    /// <summary>A question with one or two answers (Quit?, Skip?, New game?), or a notice with OK.</summary>
    public sealed class ConfirmModal : GameModal
    {
        public ConfirmModal(ScreenContext ctx, string title, string message, string cancel, string confirm, Action onConfirm, string confirmStyle = "primary")
            : base(ctx)
        {
            UiStyle style = ctx.Style;
            float width = 900f;
            List<string> lines = ctx.Painter.Wrap(message ?? string.Empty, style.TextSizes.Body, width - 120f);
            float bodyHeight = lines.Count * ctx.Text.LineHeight(style.TextSizes.Body);
            float height = 150f + bodyHeight + 60f + 130f;
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label { Bounds = new Rect(card.X + 60f, card.Y + 50f, width - 120f, 60f), Text = title, Size = style.TextSizes.Heading, ColorKey = "plum", Align = TextAlign.Center });
            panel.Add(new Label
            {
                Bounds = new Rect(card.X + 60f, card.Y + 140f, width - 120f, bodyHeight),
                Text = message ?? string.Empty,
                Size = style.TextSizes.Body,
                ColorKey = "ink",
                Align = TextAlign.Center,
                Wrap = true
            });
            float buttonsY = card.Bottom - 160f;
            if (cancel != null)
            {
                Button no = panel.Add(new Button { Id = "cancel", Bounds = new Rect(card.X + 60f, buttonsY, (width - 150f) / 2f, 110f), Text = cancel, StyleKey = "secondary" });
                no.Clicked += Close;
                Button yes = panel.Add(new Button { Id = "confirm", Bounds = new Rect(card.X + 90f + (width - 150f) / 2f, buttonsY, (width - 150f) / 2f, 110f), Text = confirm, StyleKey = confirmStyle });
                yes.Clicked += () =>
                {
                    Close();
                    onConfirm?.Invoke();
                };
            }
            else
            {
                Button ok = panel.Add(new Button { Id = "confirm", Bounds = new Rect(card.Center.X - 200f, buttonsY, 400f, 110f), Text = confirm, StyleKey = confirmStyle });
                ok.Clicked += () =>
                {
                    Close();
                    onConfirm?.Invoke();
                };
            }

            Name = "confirm:" + title;
        }

        public override string Name { get; }
    }

}
