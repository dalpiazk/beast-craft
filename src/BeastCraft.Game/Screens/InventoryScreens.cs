using System;
using System.Collections.Generic;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The Inventory screen: owned gear (filter/sort, Equip via a <see cref="BeastPickerModal"/> for
    /// beast gear or directly for avatar gear, Sell unworn gear), materials/Grove items/consumables
    /// (read-only counts), and a look-token/collection summary — built on the shared
    /// <see cref="Components"/> layer. Pushed from the Inventory tab (<see cref="HomeScreen.SelectTab"/>).
    /// </summary>
    public sealed class InventoryScreen : GameScreen
    {
        private static readonly float ContentTop = TabStrip.ContentTop(HeaderMetrics.Standard);

        private static readonly string[] TabNames = { "Gear", "Materials", "Looks" };
        private static readonly string[] TabGlyphs = { "gear", "cache", "coin" };
        private static readonly string[] FilterLabels = { "All", "Beast", "Avatar" };

        private readonly InventoryHubViewModel _hub;
        private readonly ScreenHeader _header;
        private readonly Tabs _tabs;
        private readonly CardList _gearList;
        private readonly CardList _materialsList;
        private readonly CardList _looksList;

        public InventoryScreen(ScreenContext ctx) : base(ctx)
        {
            _hub = new InventoryHubViewModel(ctx.Session);
            Rect page = new Rect(0, ContentTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - ContentTop);
            _gearList = new CardList(Ui.Add(new ScrollView { Id = "gear-page", Bounds = page }));
            _materialsList = new CardList(Ui.Add(new ScrollView { Id = "materials-page", Bounds = page, Visible = false }));
            _looksList = new CardList(Ui.Add(new ScrollView { Id = "looks-page", Bounds = page, Visible = false }));

            _header = new ScreenHeader(Ui, HeaderMetrics.Standard, () => Ctx.Stack.Pop());
            _tabs = TabStrip.Build(Ui, "inventory-tabs", HeaderMetrics.Standard, TabNames, TabGlyphs, index => SelectTab((InventoryTab)index));

            BuildAll();
        }

        public override string Name => "inventory";

        public InventoryHubViewModel Model => _hub;

        public override void Enter()
        {
            base.Enter();
            _hub.RefreshAll();
            BuildAll();
        }

        public void SelectTab(InventoryTab tab)
        {
            _hub.Select(tab);
            ShowTab();
        }

        private void ShowTab()
        {
            _tabs.Selected = (int)_hub.Tab;
            _gearList.Scroll.Visible = _hub.Tab == InventoryTab.Gear;
            _materialsList.Scroll.Visible = _hub.Tab == InventoryTab.Materials;
            _looksList.Scroll.Visible = _hub.Tab == InventoryTab.Looks;
        }

        private void Build()
        {
            BuildAll();
        }

        private void BuildAll()
        {
            BuildGear();
            BuildMaterials();
            BuildLooks();
            ShowTab();
        }

        // ------------------------------------------------------------------------------------------
        // Gear
        // ------------------------------------------------------------------------------------------

        private void BuildGear()
        {
            _gearList.Begin();
            float y = 10f;
            y = ChipRow.Build(Ctx, _gearList.Scroll, HeaderMetrics.Pad, y, _gearList.Width, FilterLabels, (int)_hub.Gear.Filter, "gear-filter-", i =>
            {
                _hub.Gear.SetFilter((GearOwnerFilter)i);
                Build();
            });
            y += 6f;
            y = ChipRow.Build(Ctx, _gearList.Scroll, HeaderMetrics.Pad, y, _gearList.Width, new[] { "Sort: " + _hub.Gear.Sort }, -1, "gear-sort-", i =>
            {
                _hub.Gear.CycleSort();
                Build();
            });
            y += 20f;

            if (_hub.Gear.Gear.Count == 0)
            {
                y = _gearList.Card(y, 110f, "card", box => Painter.TextIn("No gear yet: battles, first clears and the Trader give it.",
                                                                           new Rect(box.X + 40f, box.Y + 34f, box.Width - 80f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
                                                                           Painter.C("inkSoft"), TextAlign.Left));
            }

            foreach (InventoryGearRow row in _hub.Gear.Gear)
            {
                InventoryGearRow captured = row;
                float top = y;
                y = _gearList.Card(y, ItemRow.Height, "card", box => ItemRow.Draw(Ctx, box, RowData(captured), 240f));
                List<ActionButtonData> actions = new List<ActionButtonData>();
                if (row.WornBy == null)
                {
                    actions.Add(new ActionButtonData { Id = "equip-" + row.InstanceId, Text = "Equip", OnClick = () => Equip(captured) });
                    actions.Add(new ActionButtonData { Id = "sell-" + row.InstanceId, Text = "Sell", Style = "secondary", OnClick = () => SellGear(captured) });
                }

                ActionRow.Build(_gearList.Scroll, new Rect(HeaderMetrics.Pad + _gearList.Width - 220f, top + 20f, 220f, ItemRow.Height - 40f), 190f, 70f, actions);
            }

            _gearList.End(y);
        }

        private static ItemRowData RowData(InventoryGearRow row)
        {
            return new ItemRowData
            {
                Title = row.Name + "  (" + row.SlotName + (row.MinimumLevel > 1 ? ", Lv " + row.MinimumLevel : string.Empty) + ")",
                Subtitle = row.Bonuses.Count == 0 ? null : string.Join(", ", row.Bonuses),
                Detail = row.WornBy != null ? "Worn by " + row.WornBy : "Unworn",
                DetailColor = row.WornBy != null ? "leafDeep" : "inkSoft"
            };
        }

        private void Equip(InventoryGearRow row)
        {
            if (row.IsAvatarGear)
            {
                _hub.Gear.EquipToAvatar(row.InstanceId, out string message);
                Ctx.Game.Toast(message);
                Build();
                return;
            }

            List<BeastPickerOption> options = new List<BeastPickerOption>();
            foreach (BeastOptionRow beast in _hub.Gear.BeastsFor(row.InstanceId))
            {
                options.Add(new BeastPickerOption { Id = beast.BeastId, Name = beast.Name, Subtitle = beast.Subtitle, ArtKey = beast.ArtKey, TintHex = beast.TintHex });
            }

            string instanceId = row.InstanceId;
            Ctx.Stack.PushModal(new BeastPickerModal(Ctx, "Equip " + row.Name, "No beasts owned yet.", options, beastId =>
            {
                _hub.Gear.EquipToBeast(instanceId, beastId, out string message);
                Ctx.Game.Toast(message);
                Build();
            }));
        }

        private void SellGear(InventoryGearRow row)
        {
            _hub.Gear.Sell(row.InstanceId, out string message, out int gold);
            Ctx.Game.Toast(message);
            Build();
        }

        // ------------------------------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------------------------------

        private void BuildMaterials()
        {
            _materialsList.Begin();
            float y = 10f;
            y = SectionCard(_materialsList, y, "Materials", _hub.Materials.Materials);
            y = SectionCard(_materialsList, y, "Grove items", _hub.Materials.GroveItems);
            y = SectionCard(_materialsList, y, "Consumables", _hub.Materials.Consumables);
            _materialsList.End(y);
        }

        private float SectionCard(CardList list, float y, string title, List<InventoryCountRow> rows)
        {
            y = SectionHeader.Add(Ctx, list.Scroll, HeaderMetrics.Pad, y, list.Width, title);
            if (rows.Count == 0)
            {
                return list.Card(y, 90f, "panel", box => Painter.TextIn("None held.", new Rect(box.X + 40f, box.Y + 30f, box.Width - 80f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
                                                                         Painter.C("inkSoft"), TextAlign.Left));
            }

            // One ItemRow per held item (its Subtitle wraps to two lines with an ellipsis rather than
            // clipping mid-word — a consumable's description is the reason this needs the shared row,
            // not a flat multi-line panel).
            foreach (InventoryCountRow row in rows)
            {
                InventoryCountRow captured = row;
                y = list.Card(y, ItemRow.Height, "panel", box => ItemRow.Draw(Ctx, box, CountRowData(captured)));
            }

            return y;
        }

        private static ItemRowData CountRowData(InventoryCountRow row)
        {
            return new ItemRowData { Title = row.Name + "  x" + row.Quantity, Subtitle = row.Detail };
        }

        // ------------------------------------------------------------------------------------------
        // Looks
        // ------------------------------------------------------------------------------------------

        private void BuildLooks()
        {
            _looksList.Begin();
            InventoryLooksViewModel model = _hub.Looks;
            float y = 10f;
            float top = y;
            y = _looksList.Card(y, 140f, "card", box => SectionHeader.Draw(Ctx, box, "Look tokens: " + model.LookTokens));
            _looksList.Add(new Button { Id = "open-look-shop", Bounds = new Rect(HeaderMetrics.Pad + _looksList.Width - 260f, top + 34f, 220f, 76f), Text = "Look shop", StyleKey = "primary", Glyph = "coin" })
                      .Clicked += () => Ctx.Stack.Push(new LookTokenShopScreen(Ctx));
            float height = 60f + Math.Max(1, model.Categories.Count) * 56f;
            y = _looksList.Card(y, height, "panel", box => DrawCollection(box, model.Categories));
            _looksList.End(y);
        }

        private void DrawCollection(Rect box, List<WardrobeCollectionRow> categories)
        {
            SectionHeader.Draw(Ctx, box, "Collection");
            float x = box.X + 40f;
            float w = box.Width - 80f;
            float y = box.Y + 80f;
            foreach (WardrobeCollectionRow row in categories)
            {
                Painter.TextIn(row.DisplayName + "   " + row.Owned + " / " + row.Total, new Rect(x, y, w, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
                               Painter.C(row.Owned >= row.Total ? "leafDeep" : "ink"), TextAlign.Left);
                y += 56f;
            }
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            Gradient("cream", "creamDeep", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            _header.Paint(Ctx, Ui, "Inventory", Ctx.Session.Save.Gold + " gold");
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_gearList.TryDraw(widget, out Action<Rect> gear))
            {
                gear(widget.Bounds);
                return;
            }

            if (_materialsList.TryDraw(widget, out Action<Rect> materials))
            {
                materials(widget.Bounds);
                return;
            }

            if (_looksList.TryDraw(widget, out Action<Rect> looks))
            {
                looks(widget.Bounds);
            }
        }
    }
}
