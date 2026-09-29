using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Discovery;
using BeastCraft.Game.Ui;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    // BeastCraft.Color (Core's engine-neutral colour) would win over a file-level using here.
    using Color = Microsoft.Xna.Framework.Color;

    /// <summary>Shared drawing for the discovery layer: a point of interest's marker and its glyph and colour.</summary>
    internal static class DiscoveryArt
    {
        public static string Glyph(PoiKind kind)
        {
            switch (kind)
            {
                case PoiKind.Shrine:
                    return "shrine";
                case PoiKind.LoreStone:
                    return "lore";
                case PoiKind.Cache:
                    return "cache";
                case PoiKind.KinshipSite:
                    return "kinship";
                default:
                    return "vista";
            }
        }

        public static string ColorKey(PoiKind kind)
        {
            switch (kind)
            {
                case PoiKind.Shrine:
                    return "poi_shrine";
                case PoiKind.LoreStone:
                    return "poi_lore";
                case PoiKind.Cache:
                    return "poi_cache";
                case PoiKind.KinshipSite:
                    return "poi_kinship";
                default:
                    return "poi_vista";
            }
        }

        /// <summary>
        /// A point of interest's marker: a diamond-ish rounded badge (so it never reads as a trail
        /// location's round disc), its kind's colour and glyph, a soft glow while it waits to be visited,
        /// dimmed with a check once found.
        /// </summary>
        public static void Marker(ScreenContext ctx, PoiKind kind, Vec2 c, float r, bool found, float pulse)
        {
            UiPainter painter = ctx.Painter;
            if (!found)
            {
                painter.Soft(c, r * (1.7f + 0.2f * pulse), painter.C(ColorKey(kind), 0.45f + 0.2f * pulse));
            }

            painter.Soft(new Vec2(c.X, c.Y + r * 0.8f), r, painter.C("plumDeep", 0.3f), 0.3f);
            Rect outer = new Rect(c.X - r, c.Y - r, 2f * r, 2f * r);
            painter.RoundedRect(outer, r * 0.42f, painter.C("plum", found ? 0.7f : 1f));
            painter.RoundedRect(outer.Inset(5f), r * 0.36f, painter.C(ColorKey(kind), found ? 0.55f : 1f));
            float g = r * 1.2f;
            painter.Glyph(Glyph(kind), new Rect(c.X - g / 2f, c.Y - g / 2f, g, g), painter.C("cream", found ? 0.7f : 1f));
            if (found)
            {
                Vec2 badge = new Vec2(c.X + r * 0.8f, c.Y - r * 0.8f);
                painter.Disc(badge, 18f, painter.C("plum"));
                painter.Disc(badge, 14f, painter.C("leaf"));
                painter.Glyph("check", new Rect(badge.X - 12f, badge.Y - 12f, 24f, 24f), painter.C("white"));
            }
        }
    }

    /// <summary>
    /// A point of interest's popup (<see cref="PoiViewModel"/>): its marker, name and kind, what it says,
    /// and its one action (rest at a shrine, read a lore stone, open a cache, look out from a Vista, or
    /// take a Kinship site's offering when no beast is left there); a found point shows what it held.
    /// </summary>
    public sealed class PoiModal : GameModal
    {
        private readonly PoiViewModel _model;
        private readonly Action _done;
        private readonly Rect _card;
        private readonly Button _action;

        public PoiModal(ScreenContext ctx, PoiViewModel model, Action done) : base(ctx)
        {
            _model = model;
            _done = done;
            UiStyle style = ctx.Style;
            float width = PortraitLayout.CanvasWidth - 120f;
            List<string> lines = ctx.Painter.Wrap(model.Body ?? string.Empty, style.TextSizes.Body + 2f, width - 120f);
            float height = 380f + lines.Count * ctx.Text.LineHeight(style.TextSizes.Body + 2f) + 200f;
            _card = new Rect(60f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            Button close = Ui.Add(new Button { Id = "close", Bounds = new Rect(_card.Right - 120f, _card.Y + 30f, 90f, 90f), StyleKey = "ghost", Glyph = "close" });
            close.Clicked += Close;
            _action = Ui.Add(new Button
            {
                Id = "visit",
                Bounds = new Rect(_card.X + 120f, _card.Bottom - 160f, _card.Width - 240f, 120f),
                Text = model.CanVisit ? model.Action : Loc("ui.common.close"),
                StyleKey = model.CanVisit ? "primary" : "secondary"
            });
            _action.Clicked += Act;
        }

        public override string Name
        {
            get { return "poi"; }
        }

        public PoiViewModel Model
        {
            get { return _model; }
        }

        /// <summary>The action (or Close once found).</summary>
        public void Act()
        {
            Close();
            if (_model.CanVisit)
            {
                string message = _model.Visit(out DiscoveryResult _);
                if (!string.IsNullOrEmpty(message))
                {
                    Ctx.Game.Toast(message);
                }
            }

            _done?.Invoke();
        }

        public override void Draw()
        {
            base.Draw();
            UiStyle style = Ctx.Style;
            if (_model.Poi != null)
            {
                DiscoveryArt.Marker(Ctx, _model.Poi.Kind, new Vec2(_card.Center.X, _card.Y + 130f), 70f, _model.State == PoiState.Found, 0.5f);
            }

            Ctx.Painter.TextIn(_model.Title, new Rect(_card.X, _card.Y + 220f, _card.Width, style.TextSizes.Heading + 6f), style.TextSizes.Heading + 6f, Ctx.Painter.C("plum"),
                               TextAlign.Center);
            string sub = _model.State == PoiState.Found ? Loc("ui.discovery.found", _model.KindLabel) : _model.KindLabel;
            Ctx.Painter.TextIn(sub ?? string.Empty, new Rect(_card.X, _card.Y + 290f, _card.Width, 32f), style.TextSizes.Body, Ctx.Painter.C("inkSoft"), TextAlign.Center);
            TutorialArt.Paragraph(Ctx, _model.Body, _card.X + 60f, _card.Y + 350f, _card.Width - 120f, style.TextSizes.Body + 2f, Ctx.Painter.C("ink"));
        }
    }

    /// <summary>
    /// The region progress panel (<see cref="RegionProgressViewModel"/>, the map header's "Explored"
    /// chip): the region's exploration and its 100% reward, then each stage — rows walked, places
    /// found — with Revisit on a stage already reached (a new map of it; the fog stays lifted).
    /// </summary>
    public sealed class RegionProgressModal : GameModal
    {
        private const float RowHeight = 150f;

        private readonly RegionProgressViewModel _model;
        private readonly Action _done;
        private readonly Rect _card;

        public RegionProgressModal(ScreenContext ctx, RegionProgressViewModel model, Action done) : base(ctx)
        {
            _model = model;
            _done = done;
            float height = 420f + model.Stages.Count * (RowHeight + 14f) + 180f;
            _card = new Rect(60f, Math.Max(60f, (PortraitLayout.CanvasHeight - height) / 2f), PortraitLayout.CanvasWidth - 120f, height);
            Ui.Add(new Panel { Bounds = _card, StyleKey = "modal" });
            for (int i = 0; i < model.Stages.Count; i++)
            {
                StageProgressView stage = model.Stages[i];
                if (!stage.CanRevisit)
                {
                    continue;
                }

                int index = stage.Stage;
                Button revisit = Ui.Add(new Button
                {
                    Id = "revisit-" + index.ToString(CultureInfo.InvariantCulture),
                    Bounds = new Rect(_card.Right - 300f, RowY(i) + 36f, 240f, 80f),
                    Text = Loc("ui.discovery.revisit"),
                    StyleKey = "chip"
                });
                revisit.Clicked += () => AskRevisit(index);
            }

            Button close = Ui.Add(new Button { Id = "close", Bounds = new Rect(_card.X + 120f, _card.Bottom - 150f, _card.Width - 240f, 110f), Text = Loc("ui.common.close"), StyleKey = "secondary" });
            close.Clicked += () =>
            {
                Close();
                _done?.Invoke();
            };
        }

        public override string Name
        {
            get { return "region-progress"; }
        }

        public RegionProgressViewModel Model
        {
            get { return _model; }
        }

        private float RowY(int i)
        {
            return _card.Y + 400f + i * (RowHeight + 14f);
        }

        private void AskRevisit(int stage)
        {
            Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.discovery.revisit_title", stage + 1),
                                                 Loc("ui.discovery.revisit_body", stage + 1), Loc("ui.home.stay"), Loc("ui.discovery.revisit"), () =>
                                                 {
                                                     if (!_model.Revisit(stage))
                                                     {
                                                         Ctx.Game.Toast(Loc("ui.discovery.cannot_revisit"));
                                                         return;
                                                     }

                                                     Close();
                                                     _done?.Invoke();
                                                 }));
        }

        public override void Draw()
        {
            base.Draw();
            UiStyle style = Ctx.Style;
            Ctx.Painter.TextIn(_model.Name, new Rect(_card.X, _card.Y + 50f, _card.Width, style.TextSizes.Heading + 8f), style.TextSizes.Heading + 8f, Ctx.Painter.C("plum"),
                               TextAlign.Center);
            RegionCompletion completion = _model.Completion;
            if (completion == null)
            {
                return;
            }

            Rect bar = new Rect(_card.X + 80f, _card.Y + 140f, _card.Width - 160f, 44f);
            Ctx.Painter.Progress(bar, completion.Percent / 100f, -1f, "gold", "gold", "track", Loc("ui.discovery.explored_percent", completion.Percent));
            string reward = Loc(completion.Rewarded ? "ui.discovery.fully_explored" : "ui.discovery.at_100", _model.Reward);
            TutorialArt.Paragraph(Ctx, reward, _card.X + 80f, _card.Y + 210f, _card.Width - 160f, style.TextSizes.Body, Ctx.Painter.C("ink"));
            TutorialArt.Paragraph(Ctx, Loc("ui.discovery.region_counts", completion.LocationsExplored, completion.LocationsTotal, completion.PoisFound, completion.PoisTotal), _card.X + 80f, _card.Y + 290f, _card.Width - 160f, style.TextSizes.Small + 2f, Ctx.Painter.C("inkSoft"));
            for (int i = 0; i < _model.Stages.Count; i++)
            {
                StageProgressView stage = _model.Stages[i];
                Rect row = new Rect(_card.X + 40f, RowY(i), _card.Width - 80f, RowHeight);
                Ctx.Painter.Framed(row, 28f, 3f, Ctx.Painter.C("plum", stage.IsCurrent ? 1f : 0.5f), Ctx.Painter.C(stage.IsCurrent ? "creamDeep" : "cream"));
                Ctx.Painter.TextIn(stage.IsCurrent ? Loc("ui.discovery.here_now", stage.Label) : stage.Cleared ? Loc("ui.discovery.cleared", stage.Label) : stage.Label, new Rect(row.X + 30f, row.Y + 20f, row.Width - 360f, 40f),
                                   style.TextSizes.Body + 4f, Ctx.Painter.C("plum"), TextAlign.Left);
                string detail = Loc("ui.discovery.stage_counts", stage.RowsWalked, stage.Rows, stage.PoisFound, stage.PoisTotal);
                Ctx.Painter.TextIn(detail, new Rect(row.X + 30f, row.Y + 80f, row.Width - 360f, 36f), style.TextSizes.Small + 2f, Ctx.Painter.C("inkSoft"), TextAlign.Left);
            }
        }
    }
}
