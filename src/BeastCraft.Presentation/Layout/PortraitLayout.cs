using System;
using BeastCraft.Presentation.Board;

namespace BeastCraft.Presentation.Layout
{
    /// <summary>An axis-aligned rectangle in canvas (or screen) pixels. Engine-neutral.</summary>
    public readonly struct Rect
    {
        public Rect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public float X { get; }

        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public float Right
        {
            get { return X + Width; }
        }

        public float Bottom
        {
            get { return Y + Height; }
        }

        public Vec2 Center
        {
            get { return new Vec2(X + Width / 2f, Y + Height / 2f); }
        }

        public bool Contains(float x, float y)
        {
            return x >= X && x < Right && y >= Y && y < Bottom;
        }

        /// <summary>This rectangle shrunk by <paramref name="amount"/> on every side.</summary>
        public Rect Inset(float amount)
        {
            return new Rect(X + amount, Y + amount, Math.Max(0f, Width - 2f * amount), Math.Max(0f, Height - 2f * amount));
        }

        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Width + "x" + Height + ")";
        }
    }

    /// <summary>
    /// How far the unobstructed part of a screen sits in from each edge, in screen pixels: a
    /// phone's display cutout (notch, punch-hole) or rounded corners, or a configured margin.
    /// </summary>
    public readonly struct SafeInsets
    {
        public static readonly SafeInsets None = new SafeInsets(0, 0, 0, 0);

        public SafeInsets(int left, int top, int right, int bottom)
        {
            Left = Math.Max(0, left);
            Top = Math.Max(0, top);
            Right = Math.Max(0, right);
            Bottom = Math.Max(0, bottom);
        }

        public int Left { get; }

        public int Top { get; }

        public int Right { get; }

        public int Bottom { get; }
    }

    /// <summary>
    /// Where the logical canvas lands on a screen: uniformly scaled to the largest size that fits
    /// the screen's safe area and centred in it, with bars (letterbox or pillarbox) on the rest.
    /// </summary>
    public readonly struct CanvasFit
    {
        public CanvasFit(float scale, float offsetX, float offsetY)
        {
            Scale = scale;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        /// <summary>Screen pixels per canvas pixel.</summary>
        public float Scale { get; }

        /// <summary>The canvas's top-left corner on the screen.</summary>
        public float OffsetX { get; }

        public float OffsetY { get; }

        /// <summary>The canvas's rectangle on the screen.</summary>
        public Rect Screen(int canvasWidth, int canvasHeight)
        {
            return new Rect(OffsetX, OffsetY, canvasWidth * Scale, canvasHeight * Scale);
        }

        /// <summary>A canvas rectangle in screen pixels (e.g. the board area, to clip the zoomed board to).</summary>
        public Rect ToScreen(Rect canvas)
        {
            return new Rect(OffsetX + canvas.X * Scale, OffsetY + canvas.Y * Scale, canvas.Width * Scale, canvas.Height * Scale);
        }

        /// <summary>A screen point in canvas pixels (the inverse of the fit), for hit-testing taps and clicks.</summary>
        public Vec2 ToCanvas(float screenX, float screenY)
        {
            return new Vec2((screenX - OffsetX) / Scale, (screenY - OffsetY) / Scale);
        }

        /// <summary>
        /// Fits a <paramref name="canvasWidth"/> x <paramref name="canvasHeight"/> canvas into a
        /// <paramref name="screenWidth"/> x <paramref name="screenHeight"/> screen, inside
        /// <paramref name="insets"/>.
        /// </summary>
        public static CanvasFit Of(int canvasWidth, int canvasHeight, int screenWidth, int screenHeight, SafeInsets insets)
        {
            float safeWidth = Math.Max(1, screenWidth - insets.Left - insets.Right);
            float safeHeight = Math.Max(1, screenHeight - insets.Top - insets.Bottom);
            float scale = Math.Min(safeWidth / canvasWidth, safeHeight / canvasHeight);
            float x = insets.Left + (safeWidth - canvasWidth * scale) / 2f;
            float y = insets.Top + (safeHeight - canvasHeight * scale) / 2f;
            return new CanvasFit(scale, x, y);
        }
    }

    /// <summary>
    /// Where the board lands in its area: board space (the <see cref="HexLayout"/>'s pixels, with
    /// tile (0, 0)'s centre at the origin) scaled uniformly and centred so the whole arena, and the
    /// sprites standing on its top row, fit.
    /// </summary>
    public readonly struct BoardFit
    {
        public BoardFit(float scale, float originX, float originY)
        {
            Scale = scale;
            OriginX = originX;
            OriginY = originY;
        }

        /// <summary>Canvas pixels per board pixel.</summary>
        public float Scale { get; }

        /// <summary>Where board point (0, 0), tile (0, 0)'s centre, lands on the canvas.</summary>
        public float OriginX { get; }

        public float OriginY { get; }

        public Vec2 ToCanvas(Vec2 board)
        {
            return new Vec2(OriginX + board.X * Scale, OriginY + board.Y * Scale);
        }

        public Vec2 ToBoard(float canvasX, float canvasY)
        {
            return new Vec2((canvasX - OriginX) / Scale, (canvasY - OriginY) / Scale);
        }
    }

    /// <summary>
    /// The battle screen in portrait, on a fixed logical canvas of <see cref="CanvasWidth"/> x
    /// <see cref="CanvasHeight"/> (9:16): top to bottom, a header (title, turn), the turn-order
    /// portrait bar, the board, a one-line toast for the log, the acting unit's skill strip and the
    /// playback controls (pause/play, x1/x2/x3, skip). Pure layout maths: no engine types, so it is
    /// tested headless and every host (desktop window, phone) places things identically.
    /// <para>
    /// Why 1080x1920 at 9:16: it is the common phone panel in portrait, so on the reference device a
    /// canvas pixel is a screen pixel and illustrated art authored for 1080p lands 1:1; 9:16 is the
    /// narrowest aspect worth designing for (taller 19.5:9 phones get bars top and bottom, which is
    /// where their notch and gesture bar sit anyway; 3:4 tablets get side bars), so nothing on the
    /// canvas is ever cropped. A fixed canvas keeps layout, hit-testing and screenshots
    /// resolution-independent.
    /// </para>
    /// </summary>
    public sealed class PortraitLayout
    {
        public const int CanvasWidth = 1080;
        public const int CanvasHeight = 1920;

        /// <summary>The side margin of every band.</summary>
        public const float Margin = 24f;

        /// <summary>
        /// Board pixels of headroom above the top row and below the bottom one: a sprite stands with
        /// its feet on its tile centre and reaches up about a tile and a half (large units: three).
        /// </summary>
        public const float BoardHeadroom = 48f;

        public const int ControlCount = 5;

        /// <summary>The rows of the settings overlay: effects intensity, screen shake, flashes, close.</summary>
        public const int SettingsRowCount = 4;

        public PortraitLayout()
        {
            Header = new Rect(Margin, 16f, CanvasWidth - 2f * Margin, 56f);
            TurnOrder = new Rect(Margin, 84f, CanvasWidth - 2f * Margin, 176f);
            Board = new Rect(Margin, 276f, CanvasWidth - 2f * Margin, 1150f);
            Toast = new Rect(Margin, 1438f, CanvasWidth - 2f * Margin, 64f);
            SkillStrip = new Rect(Margin, 1514f, CanvasWidth - 2f * Margin, 244f);
            Controls = new Rect(Margin, 1774f, CanvasWidth - 2f * Margin, 120f);
            SettingsButton = new Rect(CanvasWidth - Margin - 64f, 12f, 64f, 64f);
            SkillDetail = new Rect(Margin, 560f, CanvasWidth - 2f * Margin, 856f);
            SkillDetailIcon = new Rect(SkillDetail.X + 28f, SkillDetail.Y + 28f, 120f, 120f);
            SkillDetailDiagram = new Rect(SkillDetail.X + 28f, SkillDetail.Y + 380f, 440f, SkillDetail.Height - 408f);
            SkillDetailText = new Rect(SkillDetailDiagram.Right + 28f, SkillDetailDiagram.Y, SkillDetail.Right - 28f - SkillDetailDiagram.Right - 28f,
                                       SkillDetailDiagram.Height);
            SettingsPanel = new Rect(Margin + 96f, 520f, CanvasWidth - 2f * Margin - 192f, 520f);
        }

        public Rect Canvas
        {
            get { return new Rect(0f, 0f, CanvasWidth, CanvasHeight); }
        }

        public Rect Header { get; }

        public Rect TurnOrder { get; }

        public Rect Board { get; }

        public Rect Toast { get; }

        public Rect SkillStrip { get; }

        public Rect Controls { get; }

        /// <summary>
        /// The skill detail card, over the lower part of the board, opened by tapping a skill in
        /// the strip: icon, name and tags across the top, then its cooldown, range and power lines,
        /// then the hex range diagram (left) beside the description (right).
        /// </summary>
        public Rect SkillDetail { get; }

        /// <summary>The card's icon, top left.</summary>
        public Rect SkillDetailIcon { get; }

        /// <summary>The card's hex range diagram, bottom left.</summary>
        public Rect SkillDetailDiagram { get; }

        /// <summary>The card's description (rich text with glossary terms), bottom right.</summary>
        public Rect SkillDetailText { get; }

        /// <summary>The gear button at the header's right end: opens and closes the settings overlay.</summary>
        public Rect SettingsButton { get; }

        /// <summary>The settings overlay's panel, over the board.</summary>
        public Rect SettingsPanel { get; }

        /// <summary>
        /// Row <paramref name="index"/> of the settings overlay, top to bottom under its title: 0
        /// effects intensity (cycles Full, Reduced, Minimal), 1 screen shake, 2 flashes, 3 close.
        /// </summary>
        public Rect SettingsRow(int index)
        {
            Rect body = new Rect(SettingsPanel.X + 32f, SettingsPanel.Y + 96f, SettingsPanel.Width - 64f, SettingsPanel.Height - 128f);
            const float gap = 20f;
            float height = (body.Height - gap * (SettingsRowCount - 1)) / SettingsRowCount;
            return new Rect(body.X, body.Y + index * (height + gap), body.Width, height);
        }

        /// <summary>The <paramref name="index"/>th of <paramref name="count"/> portrait slots in the turn-order bar (square, left to right, under its label).</summary>
        public Rect TurnOrderSlot(int index, int count)
        {
            return Row(new Rect(TurnOrder.X, TurnOrder.Y + 40f, TurnOrder.Width, TurnOrder.Height - 40f), index, Math.Max(count, 8), 12f, true);
        }

        /// <summary>The <paramref name="index"/>th of <paramref name="count"/> skill cards in the strip (under the acting unit's name).</summary>
        public Rect SkillCard(int index, int count)
        {
            return Row(new Rect(SkillStrip.X, SkillStrip.Y + 44f, SkillStrip.Width, SkillStrip.Height - 44f), index, Math.Max(count, 4), 16f, false);
        }

        /// <summary>The playback controls, left to right: 0 pause/play, 1-3 speed x1/x2/x3, 4 skip.</summary>
        public Rect Control(int index)
        {
            return Row(Controls, index, ControlCount, 16f, false);
        }

        /// <summary>
        /// The fit of a hexagon arena of <paramref name="radius"/> into <see cref="Board"/>: its
        /// tiles (<see cref="HexLayout.BoardSize"/>) plus <see cref="BoardHeadroom"/> above and
        /// below, as large as fits, centred.
        /// </summary>
        public BoardFit FitBoard(int radius)
        {
            return FitBoard(radius, Board);
        }

        /// <summary>The fit of a hexagon arena of <paramref name="radius"/> into <paramref name="area"/>.</summary>
        public static BoardFit FitBoard(int radius, Rect area)
        {
            (int width, int height) = HexLayout.BoardSize(radius);
            float needHeight = height + 2f * BoardHeadroom;
            float scale = Math.Min(area.Width / width, area.Height / needHeight);
            Vec2 center = area.Center;
            return new BoardFit(scale, center.X, center.Y);
        }

        /// <summary>
        /// Where a popup of <paramref name="width"/> x <paramref name="height"/> goes for something
        /// at <paramref name="anchor"/> (a tapped glossary term): above it, a small gap away, when
        /// there is room below the header, else below it; centred on it horizontally; always inside
        /// the canvas's margins.
        /// </summary>
        public Rect PopupNear(Rect anchor, float width, float height)
        {
            const float gap = 12f;
            width = Math.Min(width, CanvasWidth - 2f * Margin);
            float x = anchor.Center.X - width / 2f;
            x = Math.Max(Margin, Math.Min(CanvasWidth - Margin - width, x));
            float y = anchor.Y - gap - height;
            if (y < Header.Bottom)
            {
                y = anchor.Bottom + gap;
            }

            y = Math.Max(Margin, Math.Min(CanvasHeight - Margin - height, y));
            return new Rect(x, y, width, height);
        }

        /// <summary>
        /// Slot <paramref name="index"/> of <paramref name="slots"/> equal slots across
        /// <paramref name="band"/>, <paramref name="gap"/> apart; square slots (as tall as they are
        /// wide, top-aligned) when <paramref name="square"/>.
        /// </summary>
        private static Rect Row(Rect band, int index, int slots, float gap, bool square)
        {
            float width = (band.Width - gap * (slots - 1)) / slots;
            float height = square ? Math.Min(width, band.Height) : band.Height;
            return new Rect(band.X + index * (width + gap), band.Y, width, height);
        }
    }
}
