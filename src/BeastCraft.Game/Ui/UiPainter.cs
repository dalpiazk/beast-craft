using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Game.Rendering;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace BeastCraft.Game.Ui
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>
    /// Draws the retained UI toolkit's widgets (<see cref="Widget"/>, engine-neutral) with the
    /// sprite renderer and the UI font (<see cref="ITextRenderer"/>), in the house style
    /// (<see cref="UiStyle"/>: data). Rounded shapes come from one engine-made anti-aliased disc
    /// texture, nine-sliced: a panel is its outline's rounded rectangle with the fill's inset on
    /// top, over a soft shadow. Icons are either art from the manifest or small code-drawn glyphs
    /// (the map's location types, the nav tabs) until the UI art lands. Scroll views clip to their
    /// box (in screen pixels, through the canvas fit) and shift their children by the scroll.
    /// </summary>
    public sealed class UiPainter : IDisposable
    {
        private const int DiscSize = 128;

        private readonly SpriteRenderer _draw;
        private readonly ITextRenderer _text;
        private readonly SpriteAtlas _atlas;
        private readonly Texture2D _disc;
        private readonly Texture2D _soft;
        private readonly Stack<(Vector2 Offset, Rectangle? Clip)> _frames = new Stack<(Vector2, Rectangle?)>();
        private Vector2 _offset;
        private Rectangle? _clip;
        private Matrix _canvas = Matrix.Identity;
        private CanvasFit _fit;

        public UiPainter(GraphicsDevice device, SpriteRenderer draw, ITextRenderer text, SpriteAtlas atlas, UiStyle style)
        {
            _draw = draw;
            _text = text;
            _atlas = atlas;
            Style = style ?? UiStyle.Build(null);
            _disc = MakeDisc(device, false);
            _soft = MakeDisc(device, true);
        }

        public UiStyle Style { get; }

        public ITextRenderer Text
        {
            get { return _text; }
        }

        /// <summary>The time the painter animates pulses with (ms), set by the host each frame.</summary>
        public float TimeMs { get; set; }

        /// <summary>Starts a frame: the canvas fit (for clipping) and the canvas transform.</summary>
        public void Begin(CanvasFit fit, Matrix canvas)
        {
            _fit = fit;
            _canvas = canvas;
            _offset = Vector2.Zero;
            _clip = null;
            _frames.Clear();
            Apply();
        }

        public void Dispose()
        {
            _disc.Dispose();
            _soft.Dispose();
        }

        // ------------------------------------------------------------------------------------------
        // Colours
        // ------------------------------------------------------------------------------------------

        /// <summary>A style colour (key or #hex) as a premultiplied XNA colour, times <paramref name="alpha"/>.</summary>
        public Color C(string keyOrHex, float alpha = 1f)
        {
            return C(Style.Color(keyOrHex), alpha);
        }

        public static Color C(UiColor color, float alpha = 1f)
        {
            float a = color.A / 255f * Math.Max(0f, Math.Min(1f, alpha));
            return new Color(color.R / 255f * a, color.G / 255f * a, color.B / 255f * a, a);
        }

        /// <summary>An element's colour key (<c>el_fire</c>, ...).</summary>
        public static string ElementKey(Element element)
        {
            return "el_" + element.ToString().ToLowerInvariant();
        }

        // ------------------------------------------------------------------------------------------
        // Widgets
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// Paints <paramref name="root"/> and its visible children, back to front.
        /// <paramref name="custom"/>, when given, is called for every widget after its own look and
        /// before its children, in its children's space (inside a scroll view: scrolled and clipped):
        /// where a screen draws what the toolkit does not (the map, portraits).
        /// </summary>
        public void Paint(Widget root, UiRoot input = null, Action<Widget> custom = null)
        {
            if (root == null || !root.Visible)
            {
                return;
            }

            bool pressed = input != null && input.Pressed == root;
            switch (root)
            {
                case Panel panel:
                    Panel(panel.Bounds, Style.Panel(panel.StyleKey));
                    break;
                case Label label:
                    Label(label);
                    break;
                case Button button:
                    Button(button.Bounds, button.Text, Style.Button(button.StyleKey), pressed, button.Enabled && button.IsLive(), button.Selected, button.Glyph ?? button.IconKey,
                           button.Caption);
                    break;
                case Icon icon:
                    Icon(icon);
                    break;
                case ProgressBar bar:
                    Progress(bar.Bounds, bar.Value, bar.From, bar.FillKey, bar.FromColorKey, bar.TrackKey, bar.Text);
                    break;
                case Tabs tabs:
                    Tabs(tabs);
                    break;
            }

            bool scroll = root is ScrollView;
            if (scroll)
            {
                ScrollView view = (ScrollView)root;
                Push(new Vector2(view.Bounds.X, view.Bounds.Y - view.ScrollY), view.Bounds);
            }

            custom?.Invoke(root);
            foreach (Widget child in root.Children)
            {
                Paint(child, input, custom);
            }

            if (scroll)
            {
                Pop();
            }
        }

        /// <summary>Shifts everything drawn after by <paramref name="offset"/> and clips it to <paramref name="clip"/> (current space), until <see cref="Pop"/>.</summary>
        public void Push(Vector2 offset, Rect? clip)
        {
            _frames.Push((_offset, _clip));
            Rectangle? next = _clip;
            if (clip.HasValue)
            {
                Rect canvasRect = new Rect(clip.Value.X + _offset.X, clip.Value.Y + _offset.Y, clip.Value.Width, clip.Value.Height);
                Rect screen = _fit.ToScreen(canvasRect);
                Rectangle rect = new Rectangle((int)Math.Floor(screen.X), (int)Math.Floor(screen.Y), (int)Math.Ceiling(screen.Width), (int)Math.Ceiling(screen.Height));
                next = _clip.HasValue ? Rectangle.Intersect(_clip.Value, rect) : rect;
            }

            _offset += offset;
            _clip = next;
            Apply();
        }

        public void Pop()
        {
            (Vector2 offset, Rectangle? clip) = _frames.Pop();
            _offset = offset;
            _clip = clip;
            Apply();
        }

        private void Apply()
        {
            _draw.SetTransform(Matrix.CreateTranslation(_offset.X, _offset.Y, 0f) * _canvas);
            _draw.SetClip(_clip);
        }

        public void Panel(Rect box, UiPanelStyle style, float alpha = 1f)
        {
            if (style.Shadow.HasValue)
            {
                RoundedRect(new Rect(box.X, box.Y + 8f, box.Width, box.Height), style.Radius, C(style.Shadow.Value, alpha));
            }

            Framed(box, style.Radius, style.OutlineWidth, C(style.Outline, alpha), C(style.Fill, alpha));
        }

        /// <summary>A rounded rectangle outlined: the outline's shape, the fill's inset by <paramref name="outline"/> on top.</summary>
        public void Framed(Rect box, float radius, float outline, Color outlineColor, Color fill)
        {
            if (outline > 0f && outlineColor.A > 0 && fill.A < 255)
            {
                // A see-through fill: the outline as a ring, so it does not show through.
                RoundedRect(box.Inset(outline), Math.Max(0f, radius - outline), fill);
                RoundedOutline(box, radius, outline, outlineColor);
            }
            else if (outline > 0f && outlineColor.A > 0)
            {
                RoundedRect(box, radius, outlineColor);
                RoundedRect(box.Inset(outline), Math.Max(0f, radius - outline), fill);
            }
            else
            {
                RoundedRect(box, radius, fill);
            }
        }

        public void Button(Rect box, string text, UiButtonStyle style, bool pressed, bool enabled, bool selected, string glyph = null, string caption = null)
        {
            Color fill = C(!enabled ? style.DisabledFill : pressed ? style.PressedFill : selected ? style.SelectedFill : style.Fill);
            Color ink = C(enabled ? style.Text : style.DisabledText);
            Rect face = pressed ? new Rect(box.X, box.Y + 4f, box.Width, box.Height - 4f) : box;
            if (!pressed && enabled && style.OutlineWidth > 0f)
            {
                RoundedRect(new Rect(box.X, box.Y + 6f, box.Width, box.Height), style.Radius, C(style.Outline, 0.35f));
            }

            Framed(face, style.Radius, style.OutlineWidth, C(style.Outline, enabled ? 1f : 0.6f), fill);
            float size = style.TextSize;
            bool hasText = !string.IsNullOrEmpty(text);
            if (!string.IsNullOrEmpty(glyph))
            {
                float g = hasText && caption == null ? Math.Min(face.Height * 0.6f, 64f) : Math.Min(face.Height * (caption != null ? 0.5f : 0.7f), face.Width * 0.7f);
                Rect icon = hasText && caption == null
                                ? new Rect(face.X + 28f, face.Center.Y - g / 2f, g, g)
                                : new Rect(face.Center.X - g / 2f, face.Y + (caption != null ? face.Height * 0.12f : (face.Height - g) / 2f), g, g);
                Glyph(glyph, icon, ink);
                if (hasText && caption == null)
                {
                    TextIn(text, new Rect(icon.Right + 12f, face.Y, face.Right - icon.Right - 40f, face.Height), size, ink, TextAlign.Center);
                    return;
                }
            }

            if (caption != null)
            {
                float y = face.Bottom - face.Height * 0.3f;
                _text.DrawCentered(_draw, _text.Fit(caption, size, face.Width - 12f), face.Center.X, y, size, ink);
                return;
            }

            if (hasText)
            {
                TextIn(text, face, size, ink, TextAlign.Center);
            }
        }

        private void Label(Label label)
        {
            float size = label.Size > 0f ? label.Size : Style.TextSizes.Body;
            Color ink = C(label.ColorKey);
            if (!label.Wrap)
            {
                TextIn(label.Text, label.Bounds, size, ink, label.Align, label.CenterVertically);
                return;
            }

            List<string> lines = Wrap(label.Text, size, label.Bounds.Width, label.MaxLines);
            float lineHeight = _text.LineHeight(size);
            float y = label.CenterVertically ? label.Bounds.Center.Y - (lines.Count * lineHeight - (lineHeight - size)) / 2f : label.Bounds.Y;
            foreach (string line in lines)
            {
                TextIn(line, new Rect(label.Bounds.X, y, label.Bounds.Width, size), size, ink, label.Align, false);
                y += lineHeight;
            }
        }

        /// <summary>One line in <paramref name="box"/>, cut to fit, aligned; centred vertically on its cap height (or top-aligned).</summary>
        public void TextIn(string text, Rect box, float size, Color ink, TextAlign align, bool center = true, Color? shadow = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            string fitted = _text.Fit(text, size, box.Width);
            float y = center ? box.Center.Y - size / 2f : box.Y;
            switch (align)
            {
                case TextAlign.Center:
                    _text.DrawCentered(_draw, fitted, box.Center.X, y, size, ink, shadow);
                    break;
                case TextAlign.Right:
                    _text.DrawRight(_draw, fitted, box.Right, y, size, ink, shadow);
                    break;
                default:
                    _text.Draw(_draw, fitted, new Vector2(box.X, y), size, ink, shadow);
                    break;
            }
        }

        /// <summary>Words of <paramref name="text"/> packed into lines of <paramref name="width"/> (at most <paramref name="maxLines"/>; 0 = any).</summary>
        public List<string> Wrap(string text, float size, float width, int maxLines = 0)
        {
            List<string> lines = new List<string>();
            foreach (string paragraph in (text ?? string.Empty).Split('\n'))
            {
                string line = string.Empty;
                foreach (string word in paragraph.Split(' '))
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

                lines.Add(line);
            }

            if (maxLines > 0 && lines.Count > maxLines)
            {
                lines.RemoveRange(maxLines, lines.Count - maxLines);
            }

            return lines;
        }

        private void Icon(Icon icon)
        {
            Rect box = icon.Bounds;
            if (!string.IsNullOrEmpty(icon.DiscKey))
            {
                Disc(box.Center, Math.Min(box.Width, box.Height) / 2f, C(icon.DiscKey));
                box = box.Inset(Math.Min(box.Width, box.Height) * 0.18f);
            }

            ArtSprite art = string.IsNullOrEmpty(icon.IconKey) ? null : _atlas.ByArtKey(icon.IconKey);
            if (art != null)
            {
                Art(art, box, false);
                return;
            }

            Glyph(icon.Glyph, box, C(icon.TintKey));
        }

        public void Progress(Rect box, float value, float from, string fillKey, string fromKey, string trackKey, string text = null)
        {
            float radius = box.Height / 2f;
            Framed(box, radius, 3f, C("plumSoft"), C(trackKey));
            Rect inner = box.Inset(3f);
            float v = Math.Max(0f, Math.Min(1f, value));
            float f = Math.Max(0f, Math.Min(1f, from));
            if (v > 0f)
            {
                RoundedRect(new Rect(inner.X, inner.Y, Math.Max(inner.Height, inner.Width * v), inner.Height), inner.Height / 2f, C(fillKey));
            }

            if (from >= 0f && f > 0f)
            {
                RoundedRect(new Rect(inner.X, inner.Y, Math.Max(inner.Height, inner.Width * Math.Min(f, v)), inner.Height), inner.Height / 2f, C(fromKey));
            }

            if (!string.IsNullOrEmpty(text))
            {
                TextIn(text, box, Math.Min(box.Height * 0.55f, Style.TextSizes.Small), C("ink"), TextAlign.Center);
            }
        }

        private void Tabs(Tabs tabs)
        {
            UiButtonStyle style = Style.Button(tabs.StyleKey);
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                Rect item = tabs.ItemBounds(i).Inset(8f);
                bool selected = i == tabs.Selected;
                if (selected)
                {
                    RoundedRect(item, style.Radius, C(style.SelectedFill));
                }

                Color ink = C(selected ? "plumDeep" : "cream");
                float g = Math.Min(item.Width, item.Height) * 0.46f;
                Glyph(i < tabs.Glyphs.Count ? tabs.Glyphs[i] : null, new Rect(item.Center.X - g / 2f, item.Y + item.Height * 0.12f, g, g), ink);
                _text.DrawCentered(_draw, _text.Fit(tabs.Items[i], style.TextSize, item.Width - 8f), item.Center.X, item.Bottom - item.Height * 0.3f, style.TextSize, ink);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Shapes
        // ------------------------------------------------------------------------------------------

        /// <summary>A filled rounded rectangle (nine-sliced from the disc).</summary>
        public void RoundedRect(Rect box, float radius, Color color)
        {
            if (box.Width <= 0f || box.Height <= 0f || color.A == 0)
            {
                return;
            }

            float r = Math.Max(0f, Math.Min(radius, Math.Min(box.Width, box.Height) / 2f));
            int half = DiscSize / 2;
            Rectangle solid = new Rectangle(half - 4, half - 4, 8, 8);
            if (r < 0.5f)
            {
                _draw.DrawRegion(_disc, solid, new Vector2(box.X, box.Y), new Vector2(box.Width, box.Height), color);
                return;
            }

            _draw.DrawRegion(_disc, new Rectangle(0, 0, half, half), new Vector2(box.X, box.Y), new Vector2(r, r), color);
            _draw.DrawRegion(_disc, new Rectangle(half, 0, half, half), new Vector2(box.Right - r, box.Y), new Vector2(r, r), color);
            _draw.DrawRegion(_disc, new Rectangle(0, half, half, half), new Vector2(box.X, box.Bottom - r), new Vector2(r, r), color);
            _draw.DrawRegion(_disc, new Rectangle(half, half, half, half), new Vector2(box.Right - r, box.Bottom - r), new Vector2(r, r), color);
            _draw.DrawRegion(_disc, solid, new Vector2(box.X + r, box.Y), new Vector2(box.Width - 2f * r, box.Height), color);
            _draw.DrawRegion(_disc, solid, new Vector2(box.X, box.Y + r), new Vector2(r, box.Height - 2f * r), color);
            _draw.DrawRegion(_disc, solid, new Vector2(box.Right - r, box.Y + r), new Vector2(r, box.Height - 2f * r), color);
        }

        /// <summary>A rounded rectangle's outline only, <paramref name="width"/> wide, inside <paramref name="box"/>.</summary>
        public void RoundedOutline(Rect box, float radius, float width, Color color)
        {
            float r = Math.Max(width, Math.Min(radius, Math.Min(box.Width, box.Height) / 2f));
            float h = width / 2f;
            Fill(new Rect(box.X + r, box.Y, box.Width - 2f * r, width), color);
            Fill(new Rect(box.X + r, box.Bottom - width, box.Width - 2f * r, width), color);
            Fill(new Rect(box.X, box.Y + r, width, box.Height - 2f * r), color);
            Fill(new Rect(box.Right - width, box.Y + r, width, box.Height - 2f * r), color);
            Arc(new Vec2(box.X + r, box.Y + r), r - h, width, color, 180f, 270f);
            Arc(new Vec2(box.Right - r, box.Y + r), r - h, width, color, 270f, 360f);
            Arc(new Vec2(box.Right - r, box.Bottom - r), r - h, width, color, 0f, 90f);
            Arc(new Vec2(box.X + r, box.Bottom - r), r - h, width, color, 90f, 180f);
        }

        public void Fill(Rect box, Color color)
        {
            RoundedRect(box, 0f, color);
        }

        public void Disc(Vec2 center, float radius, Color color)
        {
            if (radius <= 0f)
            {
                return;
            }

            _draw.DrawRegion(_disc, new Rectangle(0, 0, DiscSize, DiscSize), new Vector2(center.X - radius, center.Y - radius), new Vector2(2f * radius, 2f * radius), color);
        }

        /// <summary>A soft round glow (alpha falling off to the edge), e.g. the map's painted blobs.</summary>
        public void Soft(Vec2 center, float radius, Color color, float squash = 1f)
        {
            _draw.DrawRegion(_soft, new Rectangle(0, 0, DiscSize, DiscSize), new Vector2(center.X - radius, center.Y - radius * squash),
                             new Vector2(2f * radius, 2f * radius * squash), color);
        }

        /// <summary>A straight stroke with round ends.</summary>
        public void Line(Vec2 a, Vec2 b, float thickness, Color color)
        {
            Vector2 d = new Vector2(b.X - a.X, b.Y - a.Y);
            float length = d.Length();
            if (length > 0f)
            {
                Rectangle solid = new Rectangle(DiscSize / 2 - 4, DiscSize / 2 - 4, 8, 8);
                _draw.Batch(SamplerState.LinearClamp).Draw(_disc, new Vector2(a.X, a.Y), solid, color, (float)Math.Atan2(d.Y, d.X), new Vector2(0f, 4f),
                                                           new Vector2(length / 8f, thickness / 8f), SpriteEffects.None, 0f);
            }

            Disc(a, thickness / 2f, color);
            Disc(b, thickness / 2f, color);
        }

        public void Polyline(IReadOnlyList<Vec2> points, float thickness, Color color, bool closed = false)
        {
            for (int i = 0; i + 1 < points.Count; i++)
            {
                Line(points[i], points[i + 1], thickness, color);
            }

            if (closed && points.Count > 2)
            {
                Line(points[points.Count - 1], points[0], thickness, color);
            }
        }

        /// <summary>An arc of a circle as a stroke, from <paramref name="fromDeg"/> to <paramref name="toDeg"/> (0 = right, clockwise on screen).</summary>
        public void Arc(Vec2 center, float radius, float thickness, Color color, float fromDeg = 0f, float toDeg = 360f)
        {
            int segments = Math.Max(8, (int)(Math.Abs(toDeg - fromDeg) / 10f));
            List<Vec2> points = new List<Vec2>();
            for (int i = 0; i <= segments; i++)
            {
                double a = (fromDeg + (toDeg - fromDeg) * i / segments) * Math.PI / 180.0;
                points.Add(new Vec2(center.X + radius * (float)Math.Cos(a), center.Y + radius * (float)Math.Sin(a)));
            }

            Polyline(points, thickness, color);
        }

        /// <summary>
        /// A sprite (frame 0) fitted into <paramref name="box"/> keeping its aspect, centred on the
        /// box's bottom half (characters stand), mirrored when <paramref name="flip"/>.
        /// </summary>
        public void Art(ArtSprite art, Rect box, bool flip, Color? tint = null)
        {
            if (art == null || art.Data.FrameWidth <= 0 || art.Data.FrameHeight <= 0)
            {
                return;
            }

            float k = Math.Min(box.Width / art.Data.FrameWidth, box.Height / art.Data.FrameHeight);
            float w = art.Data.FrameWidth * k;
            float h = art.Data.FrameHeight * k;
            Vector2 topLeft = new Vector2(box.Center.X - w / 2f, box.Center.Y - h / 2f);
            SpriteBatch batch = _draw.Batch(art.Sampler);
            Color color = tint ?? Color.White;
            Color mixed = new Color(art.Tint.R * color.R / 255, art.Tint.G * color.G / 255, art.Tint.B * color.B / 255, art.Tint.A * color.A / 255);
            batch.Draw(art.Texture, topLeft, art.Frame(0), mixed, 0f, Vector2.Zero, k, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        }

        public ArtSprite Sprite(string artKey)
        {
            return string.IsNullOrEmpty(artKey) ? null : _atlas.ByArtKey(artKey);
        }

        // ------------------------------------------------------------------------------------------
        // Glyphs: small code-drawn icons (placeholders until the UI art lands)
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// A code-drawn icon in <paramref name="box"/>: battle, elite, gate, boss, shop, camp (the
        /// map's locations), map, roster, avatar, inventory (the nav), lock, check, back, close,
        /// star, seal, coin, flag, gear, element, and the points of interest's shrine, lore, cache, vista
        /// and kinship; an art key from the manifest also works.
        /// </summary>
        public void Glyph(string name, Rect box, Color ink)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            ArtSprite art = name.Contains("/") ? _atlas.ByArtKey(name) : null;
            if (art != null)
            {
                Art(art, box, false);
                return;
            }

            float s = Math.Min(box.Width, box.Height);
            float ox = box.Center.X - s / 2f;
            float oy = box.Center.Y - s / 2f;
            Vec2 P(float x, float y) => new Vec2(ox + x * s, oy + y * s);
            float t = Math.Max(2f, s * 0.1f);
            switch (name)
            {
                case "battle":
                    Line(P(0.24f, 0.78f), P(0.8f, 0.2f), t, ink);
                    Line(P(0.76f, 0.78f), P(0.2f, 0.2f), t, ink);
                    Line(P(0.14f, 0.6f), P(0.4f, 0.86f), t * 0.9f, ink);
                    Line(P(0.86f, 0.6f), P(0.6f, 0.86f), t * 0.9f, ink);
                    break;
                case "elite":
                    for (int i = 0; i < 3; i++)
                    {
                        Line(P(0.34f + i * 0.17f, 0.18f), P(0.22f + i * 0.17f, 0.82f), t, ink);
                    }

                    break;
                case "gate":
                    Line(P(0.28f, 0.84f), P(0.28f, 0.46f), t, ink);
                    Line(P(0.72f, 0.84f), P(0.72f, 0.46f), t, ink);
                    Arc(P(0.5f, 0.46f), s * 0.22f, t, ink, 180f, 360f);
                    Line(P(0.16f, 0.86f), P(0.84f, 0.86f), t, ink);
                    break;
                case "boss":
                    Polyline(new[] { P(0.2f, 0.72f), P(0.22f, 0.3f), P(0.36f, 0.52f), P(0.5f, 0.24f), P(0.64f, 0.52f), P(0.78f, 0.3f), P(0.8f, 0.72f) }, t * 0.85f, ink);
                    Line(P(0.2f, 0.74f), P(0.8f, 0.74f), t * 1.3f, ink);
                    Disc(P(0.5f, 0.2f), s * 0.06f, ink);
                    break;
                case "shop":
                case "coin":
                    Disc(P(0.5f, 0.56f), s * 0.3f, ink);
                    Disc(P(0.5f, 0.56f), s * 0.2f, C("plum", 0.35f));
                    if (name == "shop")
                    {
                        Line(P(0.38f, 0.22f), P(0.62f, 0.22f), t, ink);
                        Line(P(0.44f, 0.24f), P(0.4f, 0.32f), t * 0.8f, ink);
                        Line(P(0.56f, 0.24f), P(0.6f, 0.32f), t * 0.8f, ink);
                    }

                    break;
                case "camp":
                    Polyline(new[] { P(0.14f, 0.8f), P(0.5f, 0.2f), P(0.86f, 0.8f) }, t, ink);
                    Line(P(0.12f, 0.82f), P(0.88f, 0.82f), t, ink);
                    Line(P(0.5f, 0.82f), P(0.5f, 0.52f), t * 0.8f, ink);
                    break;
                case "grove":
                    Line(P(0.5f, 0.86f), P(0.5f, 0.5f), t * 1.3f, ink);
                    Disc(P(0.5f, 0.36f), s * 0.2f, ink);
                    Disc(P(0.32f, 0.48f), s * 0.15f, ink);
                    Disc(P(0.68f, 0.48f), s * 0.15f, ink);
                    Line(P(0.24f, 0.88f), P(0.76f, 0.88f), t, ink);
                    break;
                case "hourglass":
                    Line(P(0.26f, 0.16f), P(0.74f, 0.16f), t, ink);
                    Line(P(0.26f, 0.84f), P(0.74f, 0.84f), t, ink);
                    Polyline(new[] { P(0.32f, 0.18f), P(0.5f, 0.5f), P(0.32f, 0.82f) }, t * 0.8f, ink);
                    Polyline(new[] { P(0.68f, 0.18f), P(0.5f, 0.5f), P(0.68f, 0.82f) }, t * 0.8f, ink);
                    Disc(P(0.5f, 0.72f), s * 0.1f, ink);
                    break;
                case "map":
                    Polyline(new[] { P(0.16f, 0.26f), P(0.39f, 0.18f), P(0.61f, 0.26f), P(0.84f, 0.18f), P(0.84f, 0.74f), P(0.61f, 0.82f), P(0.39f, 0.74f), P(0.16f, 0.82f) },
                             t * 0.8f, ink, true);
                    Line(P(0.39f, 0.2f), P(0.39f, 0.74f), t * 0.6f, ink);
                    Line(P(0.61f, 0.26f), P(0.61f, 0.8f), t * 0.6f, ink);
                    break;
                case "roster":
                    Disc(P(0.5f, 0.64f), s * 0.19f, ink);
                    Disc(P(0.26f, 0.42f), s * 0.085f, ink);
                    Disc(P(0.41f, 0.28f), s * 0.085f, ink);
                    Disc(P(0.59f, 0.28f), s * 0.085f, ink);
                    Disc(P(0.74f, 0.42f), s * 0.085f, ink);
                    break;
                case "avatar":
                    Disc(P(0.5f, 0.34f), s * 0.15f, ink);
                    Arc(P(0.5f, 0.86f), s * 0.26f, t * 1.3f, ink, 190f, 350f);
                    break;
                case "inventory":
                    RoundedRect(new Rect(ox + 0.2f * s, oy + 0.4f * s, 0.6f * s, 0.42f * s), s * 0.08f, ink);
                    Arc(P(0.5f, 0.42f), s * 0.15f, t, ink, 180f, 360f);
                    break;
                case "lock":
                    RoundedRect(new Rect(ox + 0.28f * s, oy + 0.46f * s, 0.44f * s, 0.36f * s), s * 0.06f, ink);
                    Arc(P(0.5f, 0.46f), s * 0.14f, t, ink, 180f, 360f);
                    break;
                case "check":
                    Polyline(new[] { P(0.22f, 0.52f), P(0.42f, 0.72f), P(0.8f, 0.3f) }, t * 1.2f, ink);
                    break;
                case "back":
                    Polyline(new[] { P(0.62f, 0.2f), P(0.34f, 0.5f), P(0.62f, 0.8f) }, t * 1.2f, ink);
                    break;
                case "close":
                    Line(P(0.26f, 0.26f), P(0.74f, 0.74f), t * 1.2f, ink);
                    Line(P(0.74f, 0.26f), P(0.26f, 0.74f), t * 1.2f, ink);
                    break;
                case "star":
                    List<Vec2> star = new List<Vec2>();
                    for (int i = 0; i < 10; i++)
                    {
                        double a = (-90 + i * 36) * Math.PI / 180.0;
                        float r = i % 2 == 0 ? 0.42f : 0.18f;
                        star.Add(P(0.5f + r * (float)Math.Cos(a), 0.54f + r * (float)Math.Sin(a)));
                    }

                    Polyline(star, t * 0.8f, ink, true);
                    break;
                case "seal":
                    Disc(P(0.5f, 0.5f), s * 0.36f, ink);
                    Arc(P(0.5f, 0.5f), s * 0.22f, t * 0.7f, C("plum", 0.45f));
                    Disc(P(0.5f, 0.5f), s * 0.07f, C("plum", 0.45f));
                    break;
                case "flag":
                    Line(P(0.3f, 0.86f), P(0.3f, 0.14f), t, ink);
                    Polyline(new[] { P(0.3f, 0.16f), P(0.78f, 0.3f), P(0.3f, 0.46f) }, t, ink);
                    break;
                case "shrine":
                    // A little roofed shrine with a flame inside.
                    Polyline(new[] { P(0.14f, 0.36f), P(0.5f, 0.14f), P(0.86f, 0.36f) }, t, ink);
                    Line(P(0.26f, 0.38f), P(0.26f, 0.84f), t, ink);
                    Line(P(0.74f, 0.38f), P(0.74f, 0.84f), t, ink);
                    Line(P(0.16f, 0.86f), P(0.84f, 0.86f), t, ink);
                    Disc(P(0.5f, 0.62f), s * 0.1f, ink);
                    break;
                case "lore":
                    // A standing stone with carved lines.
                    RoundedRect(new Rect(ox + 0.28f * s, oy + 0.14f * s, 0.44f * s, 0.72f * s), s * 0.16f, ink);
                    Line(P(0.38f, 0.36f), P(0.62f, 0.36f), t * 0.6f, C("plum", 0.55f));
                    Line(P(0.38f, 0.5f), P(0.62f, 0.5f), t * 0.6f, C("plum", 0.55f));
                    Line(P(0.38f, 0.64f), P(0.56f, 0.64f), t * 0.6f, C("plum", 0.55f));
                    break;
                case "cache":
                    // A chest.
                    RoundedRect(new Rect(ox + 0.16f * s, oy + 0.42f * s, 0.68f * s, 0.4f * s), s * 0.06f, ink);
                    Arc(P(0.5f, 0.44f), s * 0.34f, t, ink, 180f, 360f);
                    Line(P(0.16f, 0.46f), P(0.84f, 0.46f), t * 0.7f, C("plum", 0.55f));
                    Disc(P(0.5f, 0.56f), s * 0.06f, C("plum", 0.55f));
                    break;
                case "vista":
                    // Two peaks and a rising sun.
                    Arc(P(0.62f, 0.44f), s * 0.14f, t * 0.8f, ink, 180f, 360f);
                    Polyline(new[] { P(0.1f, 0.82f), P(0.38f, 0.36f), P(0.56f, 0.64f), P(0.7f, 0.46f), P(0.9f, 0.82f) }, t, ink);
                    Line(P(0.1f, 0.84f), P(0.9f, 0.84f), t, ink);
                    break;
                case "kinship":
                    // Two paws' worth of bond: two linked rings.
                    Arc(P(0.38f, 0.52f), s * 0.2f, t, ink);
                    Arc(P(0.62f, 0.52f), s * 0.2f, t, ink);
                    Disc(P(0.5f, 0.52f), s * 0.07f, ink);
                    break;
                case "gear":
                    Arc(P(0.5f, 0.5f), s * 0.24f, t * 1.4f, ink);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4.0;
                        Line(P(0.5f + 0.3f * (float)Math.Cos(a), 0.5f + 0.3f * (float)Math.Sin(a)), P(0.5f + 0.4f * (float)Math.Cos(a), 0.5f + 0.4f * (float)Math.Sin(a)),
                             t * 1.2f, ink);
                    }

                    break;
                default:
                    Disc(P(0.5f, 0.5f), s * 0.3f, ink);
                    break;
            }
        }

        /// <summary>
        /// An element as a tinted disc with its two-letter code (<see cref="Presentation.Screens.ElementChartViewModel.Code"/>:
        /// distinct for all ten, so Light and Lightning never read alike), the element "icon" until the art lands.
        /// </summary>
        public void ElementBadge(Element element, Rect box)
        {
            float r = Math.Min(box.Width, box.Height) / 2f;
            Disc(box.Center, r, C("plum"));
            Disc(box.Center, r - 3f, C(ElementKey(element)));
            string code = Presentation.Screens.ElementChartViewModel.Code(element);
            float size = Math.Max(11f, r * 0.72f);
            _text.DrawCentered(_draw, _text.Fit(code, size, 2f * r - 6f), box.Center.X, box.Center.Y - size / 2f, size, C("white"), C("plum", 0.75f));
        }

        private static Texture2D MakeDisc(GraphicsDevice device, bool soft)
        {
            Texture2D texture = new Texture2D(device, DiscSize, DiscSize, false, SurfaceFormat.Color);
            Color[] data = new Color[DiscSize * DiscSize];
            float c = (DiscSize - 1) / 2f;
            float radius = DiscSize / 2f;
            for (int y = 0; y < DiscSize; y++)
            {
                for (int x = 0; x < DiscSize; x++)
                {
                    float d = (float)Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a;
                    if (soft)
                    {
                        float u = Math.Max(0f, 1f - d / radius);
                        a = u * u * (3f - 2f * u);
                    }
                    else
                    {
                        a = Math.Max(0f, Math.Min(1f, radius - d));
                    }

                    int v = (int)Math.Round(a * 255f);
                    data[y * DiscSize + x] = new Color(v, v, v, v);
                }
            }

            texture.SetData(data);
            return texture;
        }
    }
}
