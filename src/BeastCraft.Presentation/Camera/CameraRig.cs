using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;

namespace BeastCraft.Presentation.Camera
{
    /// <summary>
    /// Where the battle camera looks: the board-space point at the centre of the board area, and
    /// the zoom as a multiple of the fit-all scale (1 = the whole arena fits, as
    /// <see cref="PortraitLayout.FitBoard(int, int)"/> places it; 2 = everything twice as big).
    /// </summary>
    public readonly struct CameraView : IEquatable<CameraView>
    {
        public CameraView(Vec2 center, float zoom)
        {
            Center = center;
            Zoom = zoom;
        }

        /// <summary>The board-space point shown at the centre of the board area.</summary>
        public Vec2 Center { get; }

        /// <summary>The zoom, a multiple of the fit-all scale.</summary>
        public float Zoom { get; }

        /// <summary>
        /// The view <paramref name="t"/> (0-1) of the way from <paramref name="from"/> to
        /// <paramref name="to"/>: centre and zoom interpolated linearly (the caller eases
        /// <paramref name="t"/>).
        /// </summary>
        public static CameraView Lerp(CameraView from, CameraView to, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return new CameraView(Vec2.Lerp(from.Center, to.Center, t), from.Zoom + (to.Zoom - from.Zoom) * t);
        }

        public bool Equals(CameraView other)
        {
            return Center.Equals(other.Center) && Zoom.Equals(other.Zoom);
        }

        public override bool Equals(object obj)
        {
            return obj is CameraView other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Center.GetHashCode() * 397 ^ Zoom.GetHashCode();
        }

        public override string ToString()
        {
            return "(" + Center + " x" + Zoom + ")";
        }
    }

    /// <summary>The camera's tuning. The defaults are the viewer's.</summary>
    public sealed class CameraSettings
    {
        /// <summary>Board pixels of margin kept around what a shot frames.</summary>
        public float Padding = 28f;

        /// <summary>The closest zoom, as a multiple of the fit-all scale.</summary>
        public float MaxZoom = 2f;

        /// <summary>
        /// The closest zoom as an absolute scale (canvas pixels per board pixel), so a small arena,
        /// already shown big when it fits, is not blown up further: the effective limit is the
        /// smaller of this and <see cref="MaxZoom"/> (never below fit-all).
        /// </summary>
        public float MaxScale = 5.5f;

        /// <summary>How long a move from one framing to the next takes, in played ms (so x2 and x3 halve and third it).</summary>
        public int EaseMs = 360;

        /// <summary>How long the camera takes to settle back to the fit-all view when the battle is idle, in played ms.</summary>
        public int ReturnMs = 700;

        /// <summary>How long before a beat starts the camera begins moving to frame it.</summary>
        public int LeadMs = 150;
    }

    /// <summary>
    /// The battle camera's framing maths for one arena in one board area: pure and deterministic,
    /// no engine types (the renderer applies <see cref="Fit(CameraView)"/> as its board transform).
    /// It frames board-space boxes with padding (<see cref="Frame"/>), keeps the zoom between
    /// fit-all and <see cref="MaxZoomFor"/>, clamps the view so it never shows past the arena's
    /// bounds (<see cref="Clamp"/>), and eases between views (<see cref="Ease"/>).
    /// </summary>
    public sealed class CameraRig
    {
        /// <summary>
        /// A rig for a <paramref name="width"/> x <paramref name="height"/> arena (in tiles, as
        /// the <see cref="BeastCraft.Battle.Grid.HexGrid"/> has them) shown in
        /// <paramref name="area"/> (canvas pixels), with <paramref name="settings"/> (null: the
        /// defaults).
        /// </summary>
        public CameraRig(int width, int height, Rect area, CameraSettings settings = null)
        {
            Settings = settings ?? new CameraSettings();
            Area = area;
            BoardFit fit = PortraitLayout.FitBoard(width, height, area);
            FitScale = fit.Scale;
            Bounds = PortraitLayout.BoardFrame(width, height);
            MaxZoomFor = Math.Max(1f, Math.Min(Settings.MaxZoom, Settings.MaxScale / Math.Max(0.0001f, FitScale)));
        }

        public CameraSettings Settings { get; }

        /// <summary>The board area on the canvas.</summary>
        public Rect Area { get; }

        /// <summary>Canvas pixels per board pixel at zoom 1 (fit-all).</summary>
        public float FitScale { get; }

        /// <summary>The arena's bounds in board space (tile (0, 0)'s centre the origin): <see cref="PortraitLayout.BoardFrame"/>, every tile plus the sprite headroom above and the edge margin around.</summary>
        public Rect Bounds { get; }

        /// <summary>The effective closest zoom for this arena (see <see cref="CameraSettings.MaxScale"/>).</summary>
        public float MaxZoomFor { get; }

        /// <summary>The whole arena: its bounds' centre at zoom 1.</summary>
        public CameraView FitAll
        {
            get { return new CameraView(Bounds.Center, 1f); }
        }

        /// <summary>
        /// The view that frames <paramref name="boxes"/> (board-space rectangles, e.g. units and an
        /// effect's area) with <see cref="CameraSettings.Padding"/> around them: as close as fits,
        /// zoom clamped to [1, <see cref="MaxZoomFor"/>], centred on them, then clamped to the
        /// arena. No boxes: <see cref="FitAll"/>.
        /// </summary>
        public CameraView Frame(IReadOnlyList<Rect> boxes)
        {
            if (boxes == null || boxes.Count == 0)
            {
                return FitAll;
            }

            float left = float.MaxValue;
            float top = float.MaxValue;
            float right = float.MinValue;
            float bottom = float.MinValue;
            foreach (Rect box in boxes)
            {
                left = Math.Min(left, box.X);
                top = Math.Min(top, box.Y);
                right = Math.Max(right, box.Right);
                bottom = Math.Max(bottom, box.Bottom);
            }

            float pad = Settings.Padding;
            float width = Math.Max(1f, right - left + 2f * pad);
            float height = Math.Max(1f, bottom - top + 2f * pad);
            float zoom = Math.Min(Area.Width / width, Area.Height / height) / FitScale;
            zoom = Math.Max(1f, Math.Min(MaxZoomFor, zoom));
            return Clamp(new CameraView(new Vec2((left + right) / 2f, (top + bottom) / 2f), zoom));
        }

        /// <summary>
        /// <paramref name="view"/> kept inside the arena: its zoom within [1, <see cref="MaxZoomFor"/>]
        /// and its centre moved so what it shows stays within <see cref="Bounds"/> on each axis (or,
        /// where it shows more than the bounds, centred on them).
        /// </summary>
        public CameraView Clamp(CameraView view)
        {
            float zoom = Math.Max(1f, Math.Min(MaxZoomFor, float.IsNaN(view.Zoom) ? 1f : view.Zoom));
            Rect seen = Visible(new CameraView(view.Center, zoom));
            float x = ClampAxis(view.Center.X, seen.Width / 2f, Bounds.X, Bounds.Right);
            float y = ClampAxis(view.Center.Y, seen.Height / 2f, Bounds.Y, Bounds.Bottom);
            return new CameraView(new Vec2(x, y), zoom);
        }

        /// <summary>The board-space rectangle <paramref name="view"/> shows in the board area.</summary>
        public Rect Visible(CameraView view)
        {
            float scale = FitScale * view.Zoom;
            float width = Area.Width / scale;
            float height = Area.Height / scale;
            return new Rect(view.Center.X - width / 2f, view.Center.Y - height / 2f, width, height);
        }

        /// <summary>The board transform for <paramref name="view"/>: board point (0, 0)'s canvas position and the scale.</summary>
        public BoardFit Fit(CameraView view)
        {
            float scale = FitScale * view.Zoom;
            Vec2 center = Area.Center;
            return new BoardFit(scale, center.X - view.Center.X * scale, center.Y - view.Center.Y * scale);
        }

        /// <summary>
        /// The view <paramref name="elapsedMs"/> into a move from <paramref name="from"/> to
        /// <paramref name="to"/> lasting <paramref name="durationMs"/>: smoothstep-eased, clamped to
        /// the arena; <paramref name="to"/> itself from the end on (or at once for a duration of 0).
        /// </summary>
        public CameraView Ease(CameraView from, CameraView to, int elapsedMs, int durationMs)
        {
            if (durationMs <= 0 || elapsedMs >= durationMs)
            {
                return Clamp(to);
            }

            if (elapsedMs <= 0)
            {
                return Clamp(from);
            }

            return Clamp(CameraView.Lerp(from, to, Smoothstep(elapsedMs / (float)durationMs)));
        }

        /// <summary>
        /// The box a unit takes up on screen, feet at <paramref name="feet"/>: its sprite (a hex
        /// wide, reaching about a tile and a half up) with its HP bar and status icons over it, at
        /// <paramref name="scale"/> (1 for a one-tile unit, 2 for a large one).
        /// </summary>
        public static Rect UnitBox(Vec2 feet, float scale)
        {
            float half = HexLayout.ColumnStep / 2f * scale;
            float up = 36f * scale;
            float down = 14f * scale;
            return new Rect(feet.X - half, feet.Y - up, 2f * half, up + down);
        }

        /// <summary>The box around a circle (an effect's area).</summary>
        public static Rect CircleBox(Vec2 center, float radius)
        {
            float r = Math.Max(0f, radius);
            return new Rect(center.X - r, center.Y - r, 2f * r, 2f * r);
        }

        /// <summary>3t^2 - 2t^3: starts and stops gently.</summary>
        public static float Smoothstep(float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return t * t * (3f - 2f * t);
        }

        private static float ClampAxis(float center, float half, float min, float max)
        {
            if (2f * half >= max - min)
            {
                return (min + max) / 2f;
            }

            return Math.Max(min + half, Math.Min(max - half, center));
        }
    }
}
