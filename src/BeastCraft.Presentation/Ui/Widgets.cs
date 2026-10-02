using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Text;

namespace BeastCraft.Presentation.Ui
{
    /// <summary>Horizontal text alignment inside a widget's box.</summary>
    public enum TextAlign
    {
        Left,
        Center,
        Right
    }

    /// <summary>
    /// A node of the retained UI toolkit: a box (<see cref="Bounds"/>, in its parent's content
    /// space: canvas pixels, except under a <see cref="ScrollView"/>, whose children sit in its
    /// scrolled content), visibility, an enabled flag and children drawn in order (the last on
    /// top). Engine-neutral: widgets hold layout and state only; <c>BeastCraft.Game</c>'s painter
    /// draws them through <c>ITextRenderer</c>, and <see cref="UiRoot"/> routes mouse and touch to
    /// them. Hit-testing works in canvas pixels of the portrait 1080x1920 canvas, so the same code
    /// serves a mouse and a finger once the host maps the screen point into the canvas
    /// (<see cref="CanvasFit.ToCanvas"/>, which also takes the safe-area insets out).
    /// </summary>
    public abstract class Widget
    {
        private readonly List<Widget> _children = new List<Widget>();

        /// <summary>A name for tests and scripted input (optional).</summary>
        public string Id;

        public Rect Bounds;

        public bool Visible = true;

        public bool Enabled = true;

        /// <summary>Anything a screen wants to hang on the widget (e.g. the node id a hotspot stands for).</summary>
        public object Tag;

        public Widget Parent { get; private set; }

        public IReadOnlyList<Widget> Children
        {
            get { return _children; }
        }

        /// <summary>Whether a press on this widget is its own (a button), rather than falling through to its parent.</summary>
        public virtual bool Interactive
        {
            get { return false; }
        }

        /// <summary>Whether children outside this box are cut off (and never hit).</summary>
        public virtual bool ClipsChildren
        {
            get { return false; }
        }

        public T Add<T>(T child) where T : Widget
        {
            if (child == null)
            {
                return null;
            }

            child.Parent?._children.Remove(child);
            child.Parent = this;
            _children.Add(child);
            return child;
        }

        public void Remove(Widget child)
        {
            if (child != null && _children.Remove(child))
            {
                child.Parent = null;
            }
        }

        public void ClearChildren()
        {
            foreach (Widget child in _children)
            {
                child.Parent = null;
            }

            _children.Clear();
        }

        /// <summary>A point in this widget's space as its children's space (a scroll view shifts it by the scroll).</summary>
        public virtual Vec2 ToChildSpace(Vec2 point)
        {
            return point;
        }

        /// <summary>The first descendant (or this) with <see cref="Id"/> <paramref name="id"/>, depth first.</summary>
        public Widget Find(string id)
        {
            if (Id == id)
            {
                return this;
            }

            foreach (Widget child in _children)
            {
                Widget found = child.Find(id);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// The deepest visible widget under <paramref name="point"/> (this widget's space), topmost
        /// child first; null when the point misses. A clipping widget's children are only hit
        /// inside it.
        /// </summary>
        public Widget HitTest(Vec2 point)
        {
            if (!Visible)
            {
                return null;
            }

            bool inside = Bounds.Contains(point.X, point.Y);
            if (ClipsChildren && !inside)
            {
                return null;
            }

            Vec2 local = ToChildSpace(point);
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                Widget hit = _children[i].HitTest(local);
                if (hit != null)
                {
                    return hit;
                }
            }

            return inside ? this : null;
        }

        /// <summary>The nearest widget from this one up (this included) that takes presses, or null.</summary>
        public Widget InteractiveSelfOrAncestor()
        {
            for (Widget widget = this; widget != null; widget = widget.Parent)
            {
                if (widget.Interactive)
                {
                    return widget;
                }
            }

            return null;
        }

        /// <summary>The nearest <see cref="ScrollView"/> from this one up (this included), or null.</summary>
        public ScrollView ScrollAncestor()
        {
            for (Widget widget = this; widget != null; widget = widget.Parent)
            {
                if (widget is ScrollView scroll)
                {
                    return scroll;
                }
            }

            return null;
        }

        /// <summary>Whether this and every ancestor are visible and enabled.</summary>
        public bool IsLive()
        {
            for (Widget widget = this; widget != null; widget = widget.Parent)
            {
                if (!widget.Visible || !widget.Enabled)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A tap or click that pressed and released on this widget (<paramref name="canvasPoint"/>
        /// where it was released, canvas pixels). Only called on interactive, live widgets.
        /// </summary>
        public virtual void Click(Vec2 canvasPoint)
        {
        }

        /// <summary>Advances time-based state (scroll inertia, animations) by <paramref name="elapsedMs"/>.</summary>
        public virtual void Tick(float elapsedMs)
        {
            foreach (Widget child in _children)
            {
                child.Tick(elapsedMs);
            }
        }

        /// <summary>Where a point of this widget's own space is on the canvas (undoing every scroll above it).</summary>
        public Vec2 ToCanvas(Vec2 point)
        {
            Vec2 at = point;
            for (Widget parent = Parent; parent != null; parent = parent.Parent)
            {
                at = parent.FromChildSpace(at);
            }

            return at;
        }

        /// <summary>The inverse of <see cref="ToChildSpace"/>.</summary>
        public virtual Vec2 FromChildSpace(Vec2 point)
        {
            return point;
        }
    }

    /// <summary>A plain container: no look of its own (a layout group).</summary>
    public sealed class Group : Widget
    {
    }

    /// <summary>A rounded panel in one of the style's panel looks (<see cref="UiStyle.Panel"/>).</summary>
    public sealed class Panel : Widget
    {
        public string StyleKey = "panel";
    }

    /// <summary>One or more lines of text: wrapped to the box's width when <see cref="Wrap"/>.</summary>
    public sealed class Label : Widget
    {
        public string Text = string.Empty;

        /// <summary>Cap height in canvas pixels; 0 = the style's body size.</summary>
        public float Size;

        public string ColorKey = "ink";

        public TextAlign Align = TextAlign.Left;

        public bool Wrap;

        /// <summary>
        /// Which typeface this label draws in (<see cref="Text.UiFontFace"/>): <c>Heading</c>
        /// (Fredoka, the default — every label before the body typeface landed) or <c>Body</c>
        /// (Atkinson Hyperlegible: long-form reading text — a paragraph, a description, the credits).
        /// </summary>
        public UiFontFace Face = UiFontFace.Heading;

        /// <summary>Wrapped lines beyond this are dropped (0 = no limit).</summary>
        public int MaxLines;

        /// <summary>Centre the text block vertically in the box (else top-aligned).</summary>
        public bool CenterVertically;
    }

    /// <summary>
    /// A picture: an art key from the manifest (<see cref="IconKey"/>), else a built-in glyph
    /// (<see cref="Glyph"/>, drawn in code: the map's node icons, the nav tabs), tinted.
    /// </summary>
    public sealed class Icon : Widget
    {
        public string IconKey;

        public string Glyph;

        public string TintKey = "white";

        /// <summary>A disc behind the icon in this colour, or null for none.</summary>
        public string DiscKey;
    }

    /// <summary>A horizontal bar: <see cref="Value"/> filled over <see cref="From"/> (e.g. XP before and after a battle), both 0-1.</summary>
    public sealed class ProgressBar : Widget
    {
        public float Value;

        /// <summary>The part already filled before (drawn in <see cref="FromColorKey"/>); negative = none.</summary>
        public float From = -1f;

        public string FillKey = "leaf";

        public string FromColorKey = "moss";

        public string TrackKey = "track";

        public string Text;
    }

    /// <summary>
    /// A tappable button: text and/or an icon on a rounded face in a button look
    /// (<see cref="UiStyle.Button"/>): pressed, disabled and selected states.
    /// </summary>
    public class Button : Widget
    {
        public string Text = string.Empty;

        public string StyleKey = "primary";

        /// <summary>A glyph or art key drawn left of (or, with no text, instead of) the text.</summary>
        public string IconKey;

        public string Glyph;

        /// <summary>A toggled-on look (a selected tab, a picked party member).</summary>
        public bool Selected;

        /// <summary>Small text under the main text (e.g. a tab's caption).</summary>
        public string Caption;

        public event Action Clicked;

        public override bool Interactive
        {
            get { return true; }
        }

        public override void Click(Vec2 canvasPoint)
        {
            Clicked?.Invoke();
        }
    }

    /// <summary>
    /// Something the player drags about (a Grove decor piece on its habitat canvas): a press that moves more than
    /// <see cref="DragThreshold"/> drags it instead of tapping it, and wins over any <see cref="ScrollView"/> it sits in
    /// (the scroll never starts). <see cref="Moved"/> reports how far the pointer is from where it went down, in this
    /// widget's own space (the space its <see cref="Widget.Bounds"/> are in); the owner moves <see cref="Widget.Bounds"/> as
    /// it sees fit (<see cref="StartBounds"/> is where it was) and commits on <see cref="Dropped"/>. A press that does not
    /// move is an ordinary tap (<see cref="Clicked"/>).
    /// </summary>
    public class Draggable : Widget
    {
        /// <summary>How far (canvas px) a press moves before it is a drag rather than a tap.</summary>
        public const float DragThreshold = 10f;

        /// <summary>Whether a drag is under way.</summary>
        public bool Dragging { get; private set; }

        /// <summary>The bounds when the drag started.</summary>
        public Rect StartBounds { get; private set; }

        /// <summary>Where the press went down, canvas pixels (set by <see cref="UiRoot"/> as it lands).</summary>
        public Vec2 PressedAt { get; internal set; }

        /// <summary>The pointer moved: how far from where it went down (x, y).</summary>
        public event Action<Draggable, Vec2> Moved;

        /// <summary>The drag ended (released, or cancelled by a modal).</summary>
        public event Action<Draggable> Dropped;

        public event Action Clicked;

        public override bool Interactive
        {
            get { return true; }
        }

        public override void Click(Vec2 canvasPoint)
        {
            Clicked?.Invoke();
        }

        internal void BeginDrag()
        {
            Dragging = true;
            StartBounds = Bounds;
        }

        internal void DragBy(Vec2 delta)
        {
            Moved?.Invoke(this, delta);
        }

        internal void EndDrag()
        {
            if (!Dragging)
            {
                return;
            }

            Dragging = false;
            Dropped?.Invoke(this);
        }
    }

    /// <summary>
    /// A horizontal value bar, 0-1 across its width: a tap sets the value where it lands, a drag follows the pointer
    /// (<see cref="Draggable"/>, so it wins over a scroll). <see cref="Changed"/> reports every new value; the screen
    /// draws the bar (a colour picker's hue, saturation and value).
    /// </summary>
    public sealed class SliderBar : Draggable
    {
        public SliderBar()
        {
            Moved += (bar, delta) => Set(PressedAt.X + delta.X);
            Clicked += () => Set(PressedAt.X);
        }

        /// <summary>The value, 0 (left) to 1 (right).</summary>
        public float Value;

        public event Action<float> Changed;

        /// <summary>The value at canvas x <paramref name="canvasX"/> (clamped to the bar).</summary>
        public float ValueAt(float canvasX)
        {
            float left = ToCanvas(new Vec2(Bounds.X, Bounds.Y)).X;
            float t = Bounds.Width <= 0f ? 0f : (canvasX - left) / Bounds.Width;
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        private void Set(float canvasX)
        {
            Value = ValueAt(canvasX);
            Changed?.Invoke(Value);
        }
    }

    /// <summary>An invisible tappable area (a node on the map, a card): the screen draws what it stands for.</summary>
    public sealed class Hotspot : Widget
    {
        public event Action<Hotspot> Clicked;

        public override bool Interactive
        {
            get { return true; }
        }

        public override void Click(Vec2 canvasPoint)
        {
            Clicked?.Invoke(this);
        }
    }

    /// <summary>A row of tabs (the bottom nav): one button face per item; <see cref="Changed"/> on a tap of another.</summary>
    public sealed class Tabs : Widget
    {
        public readonly List<string> Items = new List<string>();

        /// <summary>A glyph per item (same order), for the painter.</summary>
        public readonly List<string> Glyphs = new List<string>();

        public int Selected;

        public string StyleKey = "nav";

        public event Action<int> Changed;

        public override bool Interactive
        {
            get { return true; }
        }

        /// <summary>The box of item <paramref name="index"/> (equal widths across the bar).</summary>
        public Rect ItemBounds(int index)
        {
            int count = Math.Max(1, Items.Count);
            float width = Bounds.Width / count;
            return new Rect(Bounds.X + index * width, Bounds.Y, width, Bounds.Height);
        }

        /// <summary>The item under <paramref name="point"/> (this widget's space), or -1.</summary>
        public int ItemAt(Vec2 point)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (ItemBounds(i).Contains(point.X, point.Y))
                {
                    return i;
                }
            }

            return -1;
        }

        public void Select(int index)
        {
            if (index < 0 || index >= Items.Count || index == Selected)
            {
                return;
            }

            Selected = index;
            Changed?.Invoke(index);
        }

        public override void Click(Vec2 canvasPoint)
        {
            Select(ItemAt(FromCanvas(canvasPoint)));
        }

        private Vec2 FromCanvas(Vec2 canvasPoint)
        {
            // Tabs never sit inside a scroll view in practice; undo any scroll above just in case.
            Vec2 origin = ToCanvas(Vec2.Zero);
            return canvasPoint - origin;
        }
    }

    /// <summary>
    /// A vertically scrolling area: its children sit in content space (y 0 at the top of the
    /// content), <see cref="ScrollY"/> of it scrolled off the top. Dragged by the finger or mouse
    /// (through <see cref="UiRoot"/>), it keeps sliding after release with the release speed and
    /// eases to a stop (<see cref="Friction"/>); dragged past an end it resists, and springs back
    /// on release.
    /// </summary>
    public sealed class ScrollView : Widget
    {
        /// <summary>Velocity kept per 16 ms of free sliding (0-1).</summary>
        public const float Friction = 0.93f;

        /// <summary>Below this speed (px/ms) the slide stops.</summary>
        public const float StopSpeed = 0.01f;

        /// <summary>How far past an end a drag may pull, as a share of the drag.</summary>
        public const float OverscrollResistance = 0.35f;

        /// <summary>The share of an overscroll taken back per 16 ms after release.</summary>
        public const float SpringBack = 0.25f;

        /// <summary>The fastest release speed kept (px/ms).</summary>
        public const float MaxSpeed = 6f;

        private readonly List<(float TimeMs, float Y)> _samples = new List<(float, float)>();
        private float _dragStartY;
        private float _dragStartScroll;

        public float ContentHeight;

        public float ScrollY;

        /// <summary>The slide speed, content pixels per ms (positive scrolls down the content).</summary>
        public float Velocity { get; private set; }

        public bool Dragging { get; private set; }

        public float MaxScroll
        {
            get { return Math.Max(0f, ContentHeight - Bounds.Height); }
        }

        public override bool ClipsChildren
        {
            get { return true; }
        }

        public override Vec2 ToChildSpace(Vec2 point)
        {
            return new Vec2(point.X - Bounds.X, point.Y - Bounds.Y + ScrollY);
        }

        public override Vec2 FromChildSpace(Vec2 point)
        {
            return new Vec2(point.X + Bounds.X, point.Y + Bounds.Y - ScrollY);
        }

        /// <summary>Scrolls straight to <paramref name="y"/> (clamped), stopping any slide.</summary>
        public void ScrollTo(float y)
        {
            ScrollY = Clamp(y);
            Velocity = 0f;
        }

        /// <summary>Scrolls so the content rectangle from <paramref name="top"/> to <paramref name="bottom"/> is centred in view (clamped).</summary>
        public void CenterOn(float top, float bottom)
        {
            ScrollTo((top + bottom) / 2f - Bounds.Height / 2f);
        }

        public void BeginDrag(float canvasY, float timeMs)
        {
            Dragging = true;
            Velocity = 0f;
            _dragStartY = canvasY;
            _dragStartScroll = ScrollY;
            _samples.Clear();
            _samples.Add((timeMs, canvasY));
        }

        public void DragTo(float canvasY, float timeMs)
        {
            if (!Dragging)
            {
                return;
            }

            float wanted = _dragStartScroll - (canvasY - _dragStartY);
            float clamped = Clamp(wanted);
            ScrollY = clamped + (wanted - clamped) * OverscrollResistance;
            _samples.Add((timeMs, canvasY));
            while (_samples.Count > 2 && timeMs - _samples[0].TimeMs > 100f)
            {
                _samples.RemoveAt(0);
            }
        }

        /// <summary>Lets go: the slide carries on with the finger's speed over its last ~100 ms.</summary>
        public void EndDrag(float timeMs)
        {
            if (!Dragging)
            {
                return;
            }

            Dragging = false;
            Velocity = 0f;
            if (_samples.Count >= 2)
            {
                (float t0, float y0) = _samples[0];
                (float t1, float y1) = _samples[_samples.Count - 1];
                float dt = Math.Max(1f, timeMs - t0);
                float speed = -(y1 - y0) / dt;
                if (t1 >= timeMs - 100f)
                {
                    Velocity = Math.Max(-MaxSpeed, Math.Min(MaxSpeed, speed));
                }
            }

            _samples.Clear();
        }

        public override void Tick(float elapsedMs)
        {
            base.Tick(elapsedMs);
            if (Dragging || elapsedMs <= 0f)
            {
                return;
            }

            float steps = elapsedMs / 16f;
            if (Math.Abs(Velocity) > StopSpeed)
            {
                ScrollY += Velocity * elapsedMs;
                Velocity *= (float)Math.Pow(Friction, steps);
            }
            else
            {
                Velocity = 0f;
            }

            float clamped = Clamp(ScrollY);
            if (clamped != ScrollY)
            {
                // Past an end: stop sliding and spring back.
                Velocity = 0f;
                float keep = (float)Math.Pow(1f - SpringBack, steps);
                ScrollY = clamped + (ScrollY - clamped) * keep;
                if (Math.Abs(ScrollY - clamped) < 0.5f)
                {
                    ScrollY = clamped;
                }
            }
        }

        private float Clamp(float y)
        {
            return Math.Max(0f, Math.Min(MaxScroll, y));
        }
    }

    /// <summary>
    /// The root of a screen's (or a modal's) widgets, and its input router: a press picks the
    /// interactive widget under it; moving past <see cref="DragThreshold"/> inside a scroll view
    /// turns the press into a drag of that view (and cancels the tap); a release on the widget
    /// pressed is a <see cref="Widget.Click"/>. One pointer at a time, which serves a mouse and a
    /// finger alike. Points are canvas pixels.
    /// </summary>
    public sealed class UiRoot : Widget
    {
        /// <summary>How far (canvas px) a press may wander and still be a tap.</summary>
        public const float DragThreshold = 18f;

        private Widget _pressed;
        private ScrollView _scroll;
        private Draggable _draggable;
        private Vec2 _downAt;
        private bool _dragging;

        public UiRoot()
        {
            Bounds = new Rect(0f, 0f, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight);
        }

        /// <summary>Milliseconds of <see cref="Tick"/> so far: the clock drag speeds are measured on.</summary>
        public float NowMs { get; private set; }

        /// <summary>The widget held down now (drawn pressed), or null.</summary>
        public Widget Pressed
        {
            get { return _dragging ? null : _pressed; }
        }

        /// <summary>Whether the pointer is down (pressed or dragging).</summary>
        public bool PointerDown { get; private set; }

        public void OnPointerDown(Vec2 point)
        {
            PointerDown = true;
            _downAt = point;
            _dragging = false;
            Widget hit = HitTest(point);
            _pressed = hit?.InteractiveSelfOrAncestor();
            if (_pressed != null && !_pressed.IsLive())
            {
                _pressed = null;
            }

            _scroll = hit?.ScrollAncestor();

            // A draggable wins over the scroll it sits in: that scroll never starts from this press.
            _draggable = _pressed as Draggable;
            if (_draggable != null)
            {
                _draggable.PressedAt = point;
                _scroll = null;
            }
        }

        public void OnPointerMove(Vec2 point)
        {
            if (!PointerDown)
            {
                return;
            }

            if (_draggable != null)
            {
                Vec2 delta = new Vec2(point.X - _downAt.X, point.Y - _downAt.Y);
                if (!_dragging && delta.X * delta.X + delta.Y * delta.Y > Draggable.DragThreshold * Draggable.DragThreshold)
                {
                    _dragging = true;
                    _draggable.BeginDrag();
                }

                if (_dragging)
                {
                    _draggable.DragBy(delta);
                }

                return;
            }

            if (!_dragging && _scroll != null)
            {
                float dx = point.X - _downAt.X;
                float dy = point.Y - _downAt.Y;
                if (dx * dx + dy * dy > DragThreshold * DragThreshold)
                {
                    _dragging = true;
                    _pressed = null;
                    _scroll.BeginDrag(_downAt.Y, NowMs);
                }
            }

            if (_dragging)
            {
                _scroll.DragTo(point.Y, NowMs);
            }
        }

        /// <summary>The release; returns the widget clicked, if any.</summary>
        public Widget OnPointerUp(Vec2 point)
        {
            if (!PointerDown)
            {
                return null;
            }

            PointerDown = false;
            if (_dragging)
            {
                if (_draggable != null)
                {
                    Draggable dropped = _draggable;
                    _draggable = null;
                    _pressed = null;
                    _dragging = false;
                    dropped.EndDrag();
                    return null;
                }

                _dragging = false;
                _scroll.EndDrag(NowMs);
                _scroll = null;
                return null;
            }

            Widget pressed = _pressed;
            _pressed = null;
            _scroll = null;
            _draggable = null;
            Widget hit = HitTest(point)?.InteractiveSelfOrAncestor();
            if (pressed == null || hit != pressed || !pressed.IsLive())
            {
                return null;
            }

            pressed.Click(point);
            return pressed;
        }

        /// <summary>A whole tap at <paramref name="point"/> (press and release): scripted input and tests.</summary>
        public Widget Tap(Vec2 point)
        {
            OnPointerDown(point);
            return OnPointerUp(point);
        }

        /// <summary>Drops any press or drag in progress (e.g. when a modal opens over this root).</summary>
        public void CancelPointer()
        {
            if (_dragging)
            {
                if (_draggable != null)
                {
                    _draggable.EndDrag();
                }
                else
                {
                    _scroll?.EndDrag(NowMs);
                }
            }

            PointerDown = false;
            _dragging = false;
            _pressed = null;
            _scroll = null;
            _draggable = null;
        }

        public override void Tick(float elapsedMs)
        {
            NowMs += Math.Max(0f, elapsedMs);
            base.Tick(elapsedMs);
        }
    }

    /// <summary>
    /// A short message that shows at the bottom of the screen for a moment and fades
    /// (<see cref="Show"/>; a new one replaces the old). Drawn by the host over every screen.
    /// </summary>
    public sealed class ToastQueue
    {
        public const float DefaultDurationMs = 2200f;

        public const float FadeMs = 250f;

        public string Message { get; private set; }

        public float RemainingMs { get; private set; }

        public float DurationMs { get; private set; }

        public bool Visible
        {
            get { return Message != null && RemainingMs > 0f; }
        }

        /// <summary>0-1: fades in over the first and out over the last <see cref="FadeMs"/>.</summary>
        public float Alpha
        {
            get
            {
                if (!Visible)
                {
                    return 0f;
                }

                float shown = DurationMs - RemainingMs;
                return Math.Min(1f, Math.Min(shown / FadeMs, RemainingMs / FadeMs));
            }
        }

        public void Show(string message, float durationMs = DefaultDurationMs)
        {
            Message = message;
            DurationMs = Math.Max(FadeMs * 2f, durationMs);
            RemainingMs = DurationMs;
        }

        public void Tick(float elapsedMs)
        {
            if (Message == null)
            {
                return;
            }

            RemainingMs -= elapsedMs;
            if (RemainingMs <= 0f)
            {
                Message = null;
                RemainingMs = 0f;
            }
        }
    }
}
