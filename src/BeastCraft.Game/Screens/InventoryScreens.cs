using System;
using System.Collections.Generic;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Board;
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

        private static readonly string[] TabKeys = { "ui.inventory.tab_gear", "ui.inventory.tab_materials", "ui.inventory.tab_looks" };
        private static readonly string[] TabGlyphs = { "gear", "cache", "coin" };
        private static readonly string[] FilterKeys = { "ui.inventory.filter_all", "ui.inventory.filter_beast", "ui.inventory.filter_avatar" };

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
            _tabs = TabStrip.Build(Ui, "inventory-tabs", HeaderMetrics.Standard, Array.ConvertAll(TabKeys, key => Loc(key)), TabGlyphs, index => SelectTab((InventoryTab)index));

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

        public override void Exit()
        {
            _hub.Gear.SaveSeen();
            base.Exit();
        }

        public void SelectTab(InventoryTab tab)
        {
            _hub.Gear.SaveSeen();
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
            y = ChipRow.Build(Ctx, _gearList.Scroll, HeaderMetrics.Pad, y, _gearList.Width, Array.ConvertAll(FilterKeys, key => Loc(key)), (int)_hub.Gear.Filter, "gear-filter-", i =>
            {
                _hub.Gear.SetFilter((GearOwnerFilter)i);
                Build();
            });
            y += 6f;
            y = ChipRow.Build(Ctx, _gearList.Scroll, HeaderMetrics.Pad, y, _gearList.Width, new[] { Loc("ui.roster.sort", _hub.Gear.Sort) }, -1, "gear-sort-", i =>
            {
                _hub.Gear.CycleSort();
                Build();
            });
            y += 20f;

            if (_hub.Gear.Gear.Count == 0)
            {
                y = _gearList.Card(y, 110f, "card", box => Painter.TextIn(Loc("ui.inventory.no_gear"),
                                                                           new Rect(box.X + 40f, box.Y + 34f, box.Width - 80f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
                                                                           Painter.C("inkSoft"), TextAlign.Left));
            }

            foreach (InventoryGearRow row in _hub.Gear.Gear)
            {
                InventoryGearRow captured = row;
                float top = y;
                y = _gearList.Card(y, ItemRow.Height, "card", box =>
                {
                    ItemRow.Draw(Ctx, box, RowData(captured), 240f);
                    if (captured.IsNew)
                    {
                        Painter.NewDot(new Vec2(box.X + 20f, box.Y + 20f));
                        if (_gearList.OnScreen(box))
                        {
                            _hub.Gear.MarkSeen(captured.InstanceId);
                        }
                    }
                });
                List<ActionButtonData> actions = new List<ActionButtonData>();
                if (row.WornBy == null)
                {
                    actions.Add(new ActionButtonData { Id = "equip-" + row.InstanceId, Text = Loc("ui.beast.equip"), OnClick = () => Equip(captured) });
                    actions.Add(new ActionButtonData { Id = "sell-" + row.InstanceId, Text = Loc("ui.shop.sell"), Style = "secondary", OnClick = () => SellGear(captured) });
                }

                ActionRow.Build(_gearList.Scroll, new Rect(HeaderMetrics.Pad + _gearList.Width - 220f, top + 20f, 220f, ItemRow.Height - 40f), 190f, 70f, actions);
            }

            _gearList.End(y);
        }

        private ItemRowData RowData(InventoryGearRow row)
        {
            return new ItemRowData
            {
                Title = row.MinimumLevel > 1 ? Loc("ui.inventory.gear_title_level", row.Name, row.SlotName, row.MinimumLevel) : Loc("ui.inventory.gear_title", row.Name, row.SlotName),
                Subtitle = row.Bonuses.Count == 0 ? null : string.Join(Loc("ui.common.list_sep"), row.Bonuses),
                Detail = row.WornBy != null ? Loc("ui.inventory.worn_by", row.WornBy) : Loc("ui.shop.unworn"),
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
            Ctx.Stack.PushModal(new BeastPickerModal(Ctx, Loc("ui.inventory.equip_name", row.Name), Loc("ui.shop.no_beasts"), options, beastId =>
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
            y = SectionCard(_materialsList, y, Loc("ui.inventory.materials"), _hub.Materials.Materials);
            y = SectionCard(_materialsList, y, Loc("ui.grove.items"), _hub.Materials.GroveItems);
            y = SectionCard(_materialsList, y, Loc("ui.shop.cat_consumables"), _hub.Materials.Consumables);
            _materialsList.End(y);
        }

        private float SectionCard(CardList list, float y, string title, List<InventoryCountRow> rows)
        {
            y = SectionHeader.Add(Ctx, list.Scroll, HeaderMetrics.Pad, y, list.Width, title);
            if (rows.Count == 0)
            {
                return list.Card(y, 90f, "panel", box => Painter.TextIn(Loc("ui.inventory.none_held"), new Rect(box.X + 40f, box.Y + 30f, box.Width - 80f, Ctx.Style.TextSizes.Body), Ctx.Style.TextSizes.Body,
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

        private ItemRowData CountRowData(InventoryCountRow row)
        {
            return new ItemRowData { Title = Loc("ui.grove.item_count", row.Name, row.Quantity), Subtitle = row.Detail };
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
            y = _looksList.Card(y, 140f, "card", box => SectionHeader.Draw(Ctx, box, Loc("ui.inventory.look_tokens", model.LookTokens)));
            _looksList.Add(new Button { Id = "open-look-shop", Bounds = new Rect(HeaderMetrics.Pad + _looksList.Width - 260f, top + 34f, 220f, 76f), Text = Loc("ui.beast.look_shop"), StyleKey = "primary", Glyph = "coin" })
                      .Clicked += () => Ctx.Stack.Push(new LookTokenShopScreen(Ctx));
            float height = 60f + Math.Max(1, model.Categories.Count) * 56f;
            y = _looksList.Card(y, height, "panel", box => DrawCollection(box, model.Categories));
            _looksList.End(y);
        }

        private void DrawCollection(Rect box, List<WardrobeCollectionRow> categories)
        {
            SectionHeader.Draw(Ctx, box, Loc("ui.inventory.collection"));
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
            _header.Paint(Ctx, Ui, Loc("ui.inventory.title"), Loc("ui.encounter.reward_gold", Ctx.Session.Save.Gold));
            if (_hub.Gear.AnyNew || _hub.Gear.Gear.Exists(row => row.IsNew))
            {
                Rect gearTab = _tabs.ItemBounds((int)InventoryTab.Gear);
                Painter.NewDot(new Vec2(gearTab.Right - 30f, gearTab.Y + 26f));
            }
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
