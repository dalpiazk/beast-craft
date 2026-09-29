using System;
using System.Collections.Generic;
using BeastCraft.Economy;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The Trader: a Shop map node's stall, or a camp's travelling trader — both push this screen
    /// (<see cref="HomeScreen.TapNode"/>'s <c>MapTapKind.Shop</c> case; the camp modal's "Trade"
    /// button). Two tabs: Stock (every current listing, grouped by category, Buy) and Sell (unworn
    /// gear, the same rule and rows the Inventory Gear tab uses) — built on the shared
    /// <see cref="Components"/> layer.
    /// </summary>
    public sealed class ShopScreen : GameScreen
    {
        private static readonly float ContentTop = TabStrip.ContentTop(HeaderMetrics.Standard);

        private static readonly string[] TabNames = { "Stock", "Sell" };
        private static readonly string[] TabGlyphs = { "shop", "coin" };

        private readonly ShopViewModel _model;
        private readonly ScreenHeader _header;
        private readonly Tabs _tabs;
        private readonly CardList _stock;
        private readonly CardList _sell;

        public ShopScreen(ScreenContext ctx, int nodeId, bool isCamp = false) : base(ctx)
        {
            _model = new ShopViewModel(ctx.Session, nodeId, isCamp);
            Rect page = new Rect(0, ContentTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - ContentTop);
            _stock = new CardList(Ui.Add(new ScrollView { Id = "stock-page", Bounds = page }));
            _sell = new CardList(Ui.Add(new ScrollView { Id = "sell-page", Bounds = page, Visible = false }));

            _header = new ScreenHeader(Ui, HeaderMetrics.Standard, () => Ctx.Stack.Pop());
            _tabs = TabStrip.Build(Ui, "shop-tabs", HeaderMetrics.Standard, TabNames, TabGlyphs, index => SelectTab((ShopTab)index));

            BuildAll();
        }

        public override string Name => "shop";

        public ShopViewModel Model => _model;

        public override void Enter()
        {
            base.Enter();
            _model.Refresh();
            BuildAll();
        }

        public void SelectTab(ShopTab tab)
        {
            _model.Select(tab);
            ShowTab();
        }

        private void ShowTab()
        {
            _tabs.Selected = (int)_model.Tab;
            _stock.Scroll.Visible = _model.Tab == ShopTab.Stock;
            _sell.Scroll.Visible = _model.Tab == ShopTab.Sell;
        }

        private void Build()
        {
            BuildAll();
        }

        private void BuildAll()
        {
            BuildStock();
            BuildSell();
            ShowTab();
        }

        // ------------------------------------------------------------------------------------------
        // Stock
        // ------------------------------------------------------------------------------------------

        /// <summary>A stable display order: one section per category, regardless of the roll order the Trader's own "Gear"/"Skill" draws interleave beast and avatar items in.</summary>
        private static readonly ShopCategory[] CategoryOrder =
        {
            ShopCategory.Material, ShopCategory.BeastGear, ShopCategory.AvatarGear, ShopCategory.BeastSkill, ShopCategory.AvatarSkill, ShopCategory.AvatarPassive, ShopCategory.Consumable,
            ShopCategory.Cosmetic
        };

        private void BuildStock()
        {
            _stock.Begin();
            float y = 10f;
            if (_model.Listings.Count == 0)
            {
                y = _stock.Card(y, 110f, "card", box => Painter.TextIn("Nothing in stock.", new Rect(box.X + 40f, box.Y + 34f, box.Width - 80f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
                                                                        Painter.C("inkSoft"), TextAlign.Left));
            }

            foreach (ShopCategory category in CategoryOrder)
            {
                List<ShopListingRow> rows = _model.Listings.FindAll(l => l.Category == category);
                if (rows.Count == 0)
                {
                    continue;
                }

                y = SectionHeader.Add(Ctx, _stock.Scroll, HeaderMetrics.Pad, y, _stock.Width, CategoryLabel(category));
                foreach (ShopListingRow row in rows)
                {
                    ShopListingRow captured = row;
                    float top = y;
                    y = _stock.Card(y, ItemRow.Height, "panel", box => ItemRow.Draw(Ctx, box, StockRowData(captured), 240f));
                    bool enabled = captured.DisabledReason == null;
                    ActionRow.Build(_stock.Scroll, new Rect(HeaderMetrics.Pad + _stock.Width - 220f, top + 40f, 220f, ItemRow.Height - 60f), 190f, 70f,
                                    new List<ActionButtonData> { new ActionButtonData { Id = "buy-" + captured.Index, Text = "Buy " + captured.Price, Enabled = enabled, OnClick = () => Buy(captured) } });
                }
            }

            _stock.End(y);
        }

        private static string CategoryLabel(ShopCategory category)
        {
            switch (category)
            {
                case ShopCategory.Material:
                    return "Materials";
                case ShopCategory.BeastGear:
                    return "Beast gear";
                case ShopCategory.AvatarGear:
                    return "Avatar gear";
                case ShopCategory.BeastSkill:
                    return "Skill tomes";
                case ShopCategory.AvatarSkill:
                    return "Avatar actives";
                case ShopCategory.AvatarPassive:
                    return "Avatar passives";
                case ShopCategory.Consumable:
                    return "Consumables";
                default:
                    return "Looks";
            }
        }

        private static ItemRowData StockRowData(ShopListingRow row)
        {
            string qty = row.Remaining > 0 ? row.Remaining + " left" : "Sold out";
            return new ItemRowData
            {
                Title = row.Name,
                Subtitle = row.Detail,
                Detail = row.DisabledReason ?? (row.Price + " gold   " + qty),
                DetailColor = row.DisabledReason != null ? "berry" : "goldDeep"
            };
        }

        private void Buy(ShopListingRow row)
        {
            if (row.NeedsBeastTarget)
            {
                List<BeastPickerOption> options = new List<BeastPickerOption>();
                foreach (BeastOptionRow beast in _model.BeastsForTome())
                {
                    options.Add(new BeastPickerOption { Id = beast.BeastId, Name = beast.Name, Subtitle = beast.Subtitle, ArtKey = beast.ArtKey, TintHex = beast.TintHex });
                }

                int index = row.Index;
                Ctx.Stack.PushModal(new BeastPickerModal(Ctx, "Teach " + row.Name, "No beasts owned yet.", options, beastId =>
                {
                    _model.Buy(index, beastId, out string message);
                    Ctx.Game.Toast(message);
                    Build();
                }));
                return;
            }

            _model.Buy(row.Index, null, out string msg);
            Ctx.Game.Toast(msg);
            Build();
        }

        // ------------------------------------------------------------------------------------------
        // Sell
        // ------------------------------------------------------------------------------------------

        private void BuildSell()
        {
            _sell.Begin();
            float y = 10f;
            if (_model.SellableGear.Count == 0)
            {
                y = _sell.Card(y, 110f, "card", box => Painter.TextIn("No spare gear to sell.", new Rect(box.X + 40f, box.Y + 34f, box.Width - 80f, Ctx.Style.TextSizes.Body),
                                                                       Ctx.Style.TextSizes.Body, Painter.C("inkSoft"), TextAlign.Left));
            }

            foreach (InventoryGearRow row in _model.SellableGear)
            {
                InventoryGearRow captured = row;
                float top = y;
                y = _sell.Card(y, ItemRow.Height, "card", box => ItemRow.Draw(Ctx, box, SellRowData(captured), 220f));
                ActionRow.Build(_sell.Scroll, new Rect(HeaderMetrics.Pad + _sell.Width - 220f, top + 40f, 220f, ItemRow.Height - 60f), 190f, 70f,
                                new List<ActionButtonData> { new ActionButtonData { Id = "sell-" + row.InstanceId, Text = "Sell", OnClick = () => Sell(captured) } });
            }

            _sell.End(y);
        }

        private static ItemRowData SellRowData(InventoryGearRow row)
        {
            return new ItemRowData { Title = row.Name, Subtitle = row.Bonuses.Count == 0 ? null : string.Join(", ", row.Bonuses), Detail = "Unworn", DetailColor = "inkSoft" };
        }

        private void Sell(InventoryGearRow row)
        {
            _model.Sell(row.InstanceId, out string message, out int gold);
            Ctx.Game.Toast(message);
            Build();
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            Gradient("cream", "creamDeep", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            _header.Paint(Ctx, Ui, "Trader", _model.Gold + " gold");
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_stock.TryDraw(widget, out Action<Rect> stock))
            {
                stock(widget.Bounds);
                return;
            }

            if (_sell.TryDraw(widget, out Action<Rect> sell))
            {
                sell(widget.Bounds);
            }
        }
    }
}
