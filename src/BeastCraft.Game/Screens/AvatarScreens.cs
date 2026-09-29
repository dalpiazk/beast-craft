using System;
using System.Collections.Generic;
using BeastCraft.Game.Screens.Components;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The Avatar screen: level/XP and base-vs-total stats (Overview), the equipped actives/passives
    /// with Change/Take off (Skills), the three gear slots with Equip/Take off (Gear), and the avatar's
    /// wardrobe (Wardrobe) — built on the shared <see cref="Components"/> layer (see
    /// docs/design/screens.md, "Shared components"). Pushed from the Avatar tab
    /// (<see cref="HomeScreen.SelectTab"/>), the same way <see cref="GroveScreen"/> is.
    /// </summary>
    public sealed class AvatarScreen : GameScreen
    {
        private static readonly float ContentTop = TabStrip.ContentTop(HeaderMetrics.Standard);

        private static readonly string[] TabNames = { "Overview", "Skills", "Gear", "Wardrobe" };
        private static readonly string[] TabGlyphs = { "avatar", "star", "gear", "roster" };

        private readonly AvatarHubViewModel _hub;
        private readonly ScreenHeader _header;
        private readonly Tabs _tabs;
        private readonly CardList _overview;
        private readonly CardList _skills;
        private readonly CardList _gear;
        private readonly CardList _wardrobe;

        public AvatarScreen(ScreenContext ctx) : base(ctx)
        {
            _hub = new AvatarHubViewModel(ctx.Session);
            Rect page = new Rect(0, ContentTop, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight - ContentTop);
            _overview = new CardList(Ui.Add(new ScrollView { Id = "overview-page", Bounds = page }));
            _skills = new CardList(Ui.Add(new ScrollView { Id = "skills-page", Bounds = page, Visible = false }));
            _gear = new CardList(Ui.Add(new ScrollView { Id = "gear-page", Bounds = page, Visible = false }));
            _wardrobe = new CardList(Ui.Add(new ScrollView { Id = "wardrobe-page", Bounds = page, Visible = false }));

            _header = new ScreenHeader(Ui, HeaderMetrics.Standard, () => Ctx.Stack.Pop());
            _tabs = TabStrip.Build(Ui, "avatar-tabs", HeaderMetrics.Standard, TabNames, TabGlyphs, index => SelectTab((AvatarTab)index));

            BuildAll();
        }

        public override string Name => "avatar";

        public AvatarHubViewModel Model => _hub;

        public override void Enter()
        {
            base.Enter();
            _hub.RefreshAll();
            BuildAll();
        }

        public void SelectTab(AvatarTab tab)
        {
            _hub.Select(tab);
            ShowTab();
        }

        private void ShowTab()
        {
            _tabs.Selected = (int)_hub.Tab;
            _overview.Scroll.Visible = _hub.Tab == AvatarTab.Overview;
            _skills.Scroll.Visible = _hub.Tab == AvatarTab.Skills;
            _gear.Scroll.Visible = _hub.Tab == AvatarTab.Gear;
            _wardrobe.Scroll.Visible = _hub.Tab == AvatarTab.Wardrobe;
        }

        private void Build()
        {
            BuildAll();
        }

        private void BuildAll()
        {
            BuildOverview();
            BuildSkills();
            BuildGear();
            BuildWardrobe();
            ShowTab();
        }

        // ------------------------------------------------------------------------------------------
        // Overview
        // ------------------------------------------------------------------------------------------

        private void BuildOverview()
        {
            _overview.Begin();
            AvatarOverviewViewModel model = _hub.Overview;
            float y = 10f;
            y = _overview.Card(y, 130f, "card", box => DrawLevelCard(box, model));
            float statsHeight = 100f + model.Stats.Count * StatTable.RowHeight;
            y = _overview.Card(y, statsHeight, "panel", box => StatTable.Draw(Ctx, box, "Base", "Total", ToStatRows(model.Stats)));
            float actionsTop = y;
            const float moreHeight = 310f;
            y = _overview.Card(y, moreHeight, "card", box => SectionHeader.Draw(Ctx, box, "More"));
            float bw = _overview.Width - 40f;
            _overview.Add(new Button { Id = "avatar-achievements", Bounds = new Rect(HeaderMetrics.Pad + 20f, actionsTop + 80f, bw, 90f), Text = "Achievements & titles", StyleKey = "primary", Glyph = "seal" })
                     .Clicked += () => Ctx.Stack.Push(new AchievementsScreen(Ctx));
            _overview.Add(new Button { Id = "avatar-look-shop", Bounds = new Rect(HeaderMetrics.Pad + 20f, actionsTop + 184f, bw, 90f), Text = "Look-token shop", StyleKey = "secondary", Glyph = "coin" })
                     .Clicked += () => Ctx.Stack.Push(new LookTokenShopScreen(Ctx));
            _overview.End(y);
        }

        /// <summary>The level/XP bar, the same shape as the beast detail screen's own (<see cref="Painter.Progress"/> with the label baked in).</summary>
        private void DrawLevelCard(Rect box, AvatarOverviewViewModel model)
        {
            float x = box.X + 40f;
            float w = box.Width - 80f;
            Painter.Progress(new Rect(x, box.Y + 44f, w, 44f), model.XpFraction, -1f, "leaf", "moss", "track",
                             "Lv " + model.Level + "   " + model.Xp + " / " + model.XpToNext + " XP");
        }

        private static List<StatTableRow> ToStatRows(List<AvatarStatRow> stats)
        {
            List<StatTableRow> rows = new List<StatTableRow>();
            foreach (AvatarStatRow stat in stats)
            {
                rows.Add(new StatTableRow { Name = stat.Name, Left = stat.Base.ToString(), Right = stat.Total.ToString() });
            }

            return rows;
        }

        // ------------------------------------------------------------------------------------------
        // Skills
        // ------------------------------------------------------------------------------------------

        private void BuildSkills()
        {
            _skills.Begin();
            float y = 10f;
            y = SectionHeader.Add(Ctx, _skills.Scroll, HeaderMetrics.Pad, y, _skills.Width, "Actives");
            for (int i = 0; i < _hub.Skills.ActiveSlots.Count; i++)
            {
                int slot = i;
                AvatarSkillSlotRow row = _hub.Skills.ActiveSlots[i];
                float height = 260f;
                float top = y;
                y = _skills.Card(y, height, row.Card != null ? "card" : "slot", box => DrawActiveSlot(box, row));
                BuildSlotActions(_skills, top, height, row.SkillId != null, () => ChooseActive(slot), () =>
                {
                    _hub.Skills.UnequipActive(slot, out string message);
                    Ctx.Game.Toast(message);
                    Build();
                });
            }

            y = SectionHeader.Add(Ctx, _skills.Scroll, HeaderMetrics.Pad, y, _skills.Width, "Passives");
            for (int i = 0; i < _hub.Skills.PassiveSlots.Count; i++)
            {
                int slot = i;
                AvatarSkillSlotRow row = _hub.Skills.PassiveSlots[i];
                float height = 240f;
                float top = y;
                y = _skills.Card(y, height, row.PassiveName != null ? "card" : "slot", box => DrawPassiveSlot(box, row));
                BuildSlotActions(_skills, top, height, row.SkillId != null, () => ChoosePassive(slot), () =>
                {
                    _hub.Skills.UnequipPassive(slot, out string message);
                    Ctx.Game.Toast(message);
                    Build();
                });
            }

            _skills.End(y);
        }

        private void BuildSlotActions(CardList list, float top, float height, bool filled, Action change, Action unequip)
        {
            float right = HeaderMetrics.Pad + list.Width;
            List<ActionButtonData> buttons = new List<ActionButtonData> { new ActionButtonData { Id = "change-" + list.Scroll.Id + "-" + (int)top, Text = "Change", OnClick = change } };
            if (filled)
            {
                buttons.Add(new ActionButtonData { Id = "unequip-" + list.Scroll.Id + "-" + (int)top, Text = "Take off", Style = "secondary", OnClick = unequip });
            }

            ActionRow.Build(list.Scroll, new Rect(right - 220f, top + 20f, 220f, height - 40f), 200f, 80f, buttons);
        }

        private void DrawActiveSlot(Rect box, AvatarSkillSlotRow slot)
        {
            if (slot.Card == null)
            {
                SectionHeader.Draw(Ctx, box, "Slot " + (slot.Slot + 1) + ": empty");
                return;
            }

            SkillCard card = slot.Card;
            SectionHeader.Draw(Ctx, box, card.Name);
            float x = box.X + 40f;
            float w = box.Width - 260f;
            float small = Ctx.Style.TextSizes.Small + 1f;
            float y = box.Y + 78f;
            string tags = string.Join(" / ", card.Targets) + "   " + card.Cooldown + "   " + card.Range;
            Painter.TextIn(tags, new Rect(x, y, w, small), small, Painter.C("inkSoft"), TextAlign.Left);
            y += 38f;
            for (int i = 0; i < card.Power.Count && i < 3; i++)
            {
                Painter.TextIn(card.Power[i], new Rect(x, y, w, small), small, Painter.C("ink"), TextAlign.Left);
                y += 34f;
            }

            Painter.TextIn(card.TargetingRule, new Rect(x, box.Bottom - 56f, w, small), small, Painter.C("plum"), TextAlign.Left, false);
        }

        private void DrawPassiveSlot(Rect box, AvatarSkillSlotRow slot)
        {
            if (slot.PassiveName == null)
            {
                SectionHeader.Draw(Ctx, box, "Slot " + (slot.Slot + 1) + ": empty");
                return;
            }

            SectionHeader.Draw(Ctx, box, slot.PassiveName);
            float x = box.X + 40f;
            float w = box.Width - 260f;
            float small = Ctx.Style.TextSizes.Small + 1f;
            float y = box.Y + 78f;
            Painter.TextIn(slot.PassiveTrigger + "   " + slot.PassiveTarget, new Rect(x, y, w, small), small, Painter.C("inkSoft"), TextAlign.Left);
            y += 38f;
            for (int i = 0; i < slot.PassivePower.Count && i < 2; i++)
            {
                Painter.TextIn(slot.PassivePower[i], new Rect(x, y, w, small), small, Painter.C("ink"), TextAlign.Left);
                y += 34f;
            }

            Painter.TextIn(slot.PassiveDescription ?? string.Empty, new Rect(x, box.Bottom - 90f, w, 80f), small, Painter.C("plum"), TextAlign.Left, true);
        }

        private void ChooseActive(int slot)
        {
            List<ChoiceOption> options = new List<ChoiceOption>();
            foreach (AvatarKnownRow known in _hub.Skills.KnownActives)
            {
                if (known.EquippedSlot == slot)
                {
                    continue;
                }

                string label = known.Name + (known.EquippedSlot >= 0 ? "  (swap with slot " + (known.EquippedSlot + 1) + ")" : string.Empty);
                string id = known.Id;
                options.Add(new ChoiceOption(label, true, null, () =>
                {
                    _hub.Skills.EquipActive(slot, id, out string message);
                    Ctx.Game.Toast(message);
                    Build();
                }));
            }

            Ctx.Stack.PushModal(new ChoiceModal(Ctx, "Slot " + (slot + 1), options.Count == 0 ? "No other active known yet: the Trader teaches more." : "Pick an active for this slot.", options));
        }

        private void ChoosePassive(int slot)
        {
            List<ChoiceOption> options = new List<ChoiceOption>();
            foreach (AvatarKnownRow known in _hub.Skills.KnownPassives)
            {
                if (known.EquippedSlot == slot)
                {
                    continue;
                }

                string label = known.Name + (known.EquippedSlot >= 0 ? "  (swap with slot " + (known.EquippedSlot + 1) + ")" : string.Empty);
                string id = known.Id;
                options.Add(new ChoiceOption(label, true, null, () =>
                {
                    _hub.Skills.EquipPassive(slot, id, out string message);
                    Ctx.Game.Toast(message);
                    Build();
                }));
            }

            Ctx.Stack.PushModal(new ChoiceModal(Ctx, "Slot " + (slot + 1), options.Count == 0 ? "No other passive known yet: the Trader teaches more." : "Pick a passive for this slot.", options));
        }

        // ------------------------------------------------------------------------------------------
        // Gear
        // ------------------------------------------------------------------------------------------

        private void BuildGear()
        {
            _gear.Begin();
            float y = 10f;
            foreach (AvatarGearSlotRow slot in _hub.Gear.Slots)
            {
                AvatarGearSlotRow captured = slot;
                float height = 150f + Math.Max(1, slot.Options.Count) * 96f;
                float top = y;
                y = _gear.Card(y, height, "panel", box => DrawGearSlot(box, captured));
                if (slot.Worn != null)
                {
                    _gear.Add(new Button { Id = "unequip-" + slot.Slot, Bounds = new Rect(HeaderMetrics.Pad + _gear.Width - 260f, top + 22f, 230f, 76f), Text = "Take off", StyleKey = "secondary" })
                         .Clicked += () =>
                    {
                        _hub.Gear.UnequipGear(captured.Slot, out string message);
                        Ctx.Game.Toast(message);
                        Build();
                    };
                }

                for (int i = 0; i < slot.Options.Count; i++)
                {
                    AvatarGearOptionRow option = slot.Options[i];
                    if (slot.Worn != null && option.InstanceId == slot.Worn.InstanceId)
                    {
                        continue;
                    }

                    Button equip = _gear.Add(new Button
                    {
                        Id = "equip-" + option.InstanceId,
                        Bounds = new Rect(HeaderMetrics.Pad + _gear.Width - 220f, top + 128f + i * 96f, 190f, 76f),
                        Text = "Equip",
                        StyleKey = "chip"
                    });
                    equip.Enabled = option.Equippable;
                    equip.Clicked += () =>
                    {
                        _hub.Gear.EquipGear(captured.Slot, option.InstanceId, out string message);
                        Ctx.Game.Toast(message);
                        Build();
                    };
                }
            }

            _gear.End(y);
        }

        private void DrawGearSlot(Rect box, AvatarGearSlotRow slot)
        {
            SectionHeader.Draw(Ctx, box, slot.SlotName);
            float x = box.X + 40f;
            float w = box.Width - 300f;
            float body = Ctx.Style.TextSizes.Body;
            float small = Ctx.Style.TextSizes.Small + 1f;
            string worn = slot.Worn == null ? "Nothing worn" : "Wearing " + slot.Worn.Name + ": " + string.Join(", ", slot.Worn.Bonuses) + (slot.Worn.Inactive ? " (inactive)" : string.Empty);
            Painter.TextIn(worn, new Rect(x, box.Y + 80f, w, body), body - 1f, Painter.C(slot.Worn == null ? "inkSoft" : "leafDeep"), TextAlign.Left);
            if (slot.Options.Count == 0)
            {
                Painter.TextIn("No gear for this slot yet: battles and the Trader give it.", new Rect(x, box.Y + 138f, w, body), body - 1f, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            for (int i = 0; i < slot.Options.Count; i++)
            {
                AvatarGearOptionRow option = slot.Options[i];
                float y = box.Y + 128f + i * 96f;
                Painter.TextIn(option.Name + (option.MinimumLevel > 1 ? "  (Lv " + option.MinimumLevel + ")" : string.Empty), new Rect(x, y + 4f, w, body), body, Painter.C("ink"), TextAlign.Left);
                string note = string.Join(", ", option.Bonuses) + (option.Reason != null ? "  -  " + option.Reason : string.Empty);
                Painter.TextIn(note, new Rect(x, y + 44f, w, small), small, Painter.C("inkSoft"), TextAlign.Left);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Wardrobe
        // ------------------------------------------------------------------------------------------

        private void BuildWardrobe()
        {
            _wardrobe.Begin();
            float y = 10f;
            foreach (WardrobeCategoryRow category in _hub.Wardrobe.Categories)
            {
                if (category.IsColor)
                {
                    float top = y;
                    WardrobeCategoryRow captured = category;
                    y = _wardrobe.Card(y, 190f, "card", box => DrawColorCategory(box, captured));
                    for (int i = 0; i < AvatarWardrobeViewModel.Swatches.Length; i++)
                    {
                        string hex = AvatarWardrobeViewModel.Swatches[i];
                        string categoryId = category.CategoryId;
                        Hotspot swatch = _wardrobe.Add(new Hotspot { Id = "swatch-" + category.CategoryId + "-" + i, Bounds = new Rect(HeaderMetrics.Pad + 40f + i * 100f, top + 110f, 84f, 64f) });
                        swatch.Clicked += _ =>
                        {
                            _hub.Wardrobe.SetColor(categoryId, hex);
                            Build();
                        };
                    }
                }
                else
                {
                    float height = 90f + Math.Max(1, category.Options.Count) * 96f;
                    float top = y;
                    WardrobeCategoryRow captured = category;
                    y = _wardrobe.Card(y, height, "card", box => DrawOptionCategory(box, captured));
                    for (int i = 0; i < category.Options.Count; i++)
                    {
                        WardrobeOptionRow option = category.Options[i];
                        if (option.Worn)
                        {
                            continue;
                        }

                        string categoryId = category.CategoryId;
                        string optionId = option.OptionId;
                        Rect rowBounds = new Rect(HeaderMetrics.Pad + _wardrobe.Width - 220f, top + 90f + i * 96f, 190f, 76f);
                        if (option.Owned)
                        {
                            _wardrobe.Add(new Button { Id = "wear-" + option.Key, Bounds = rowBounds, Text = "Wear", StyleKey = "chip" }).Clicked += () =>
                            {
                                _hub.Wardrobe.Wear(categoryId, optionId);
                                Build();
                            };
                        }
                        else if (option.TokenPurchasable)
                        {
                            _wardrobe.Add(new Button { Id = "shop-" + option.Key, Bounds = rowBounds, Text = "Look shop", StyleKey = "secondary" }).Clicked += () => Ctx.Stack.Push(new LookTokenShopScreen(Ctx));
                        }
                    }
                }
            }

            _wardrobe.End(y);
        }

        private void DrawColorCategory(Rect box, WardrobeCategoryRow category)
        {
            SectionHeader.Draw(Ctx, box, category.DisplayName);
            Painter.TextIn("Current: " + category.WornColorHex, new Rect(box.X + 40f, box.Y + 74f, box.Width - 80f, Ctx.Style.TextSizes.Small + 1f), Ctx.Style.TextSizes.Small + 1f,
                           Painter.C("inkSoft"), TextAlign.Left);
            for (int i = 0; i < AvatarWardrobeViewModel.Swatches.Length; i++)
            {
                Rect r = new Rect(box.X + 40f + i * 100f, box.Y + 110f, 84f, 64f);
                Painter.Fill(r, Painter.C(AvatarWardrobeViewModel.Swatches[i]));
                if (string.Equals(category.WornColorHex, AvatarWardrobeViewModel.Swatches[i], StringComparison.OrdinalIgnoreCase))
                {
                    Painter.Framed(r, 8f, 4f, Painter.C("plum"), Painter.C(AvatarWardrobeViewModel.Swatches[i]));
                }
            }
        }

        private void DrawOptionCategory(Rect box, WardrobeCategoryRow category)
        {
            SectionHeader.Draw(Ctx, box, category.DisplayName);
            float x = box.X + 40f;
            float w = box.Width - 300f;
            float body = Ctx.Style.TextSizes.Body;
            float small = Ctx.Style.TextSizes.Small + 1f;
            if (category.Options.Count == 0)
            {
                Painter.TextIn("No looks yet.", new Rect(x, box.Y + 80f, w, body), body, Painter.C("inkSoft"), TextAlign.Left);
                return;
            }

            for (int i = 0; i < category.Options.Count; i++)
            {
                WardrobeOptionRow option = category.Options[i];
                float y = box.Y + 90f + i * 96f;
                Painter.TextIn(option.Name, new Rect(x, y, w, body), body, Painter.C(option.Worn ? "leafDeep" : "ink"), TextAlign.Left);
                string status = option.Worn ? "Worn" : option.Owned ? "Owned" : option.TokenPurchasable ? option.TokenPrice + " look tokens" : "Locked";
                Painter.TextIn(status, new Rect(x, y + 40f, w, small), small, Painter.C(option.Worn ? "leafDeep" : option.Owned ? "goldDeep" : "inkSoft"), TextAlign.Left);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------------------------------

        public override void Draw()
        {
            Gradient("cream", "creamDeep", new Rect(0, 0, PortraitLayout.CanvasWidth, PortraitLayout.CanvasHeight));
            base.Draw();
            AvatarOverviewViewModel overview = _hub.Overview;
            _header.Paint(Ctx, Ui, "Avatar", overview.DisplayName + "   Lv " + overview.Level);
        }

        protected override void DrawCustom(Widget widget)
        {
            if (_overview.TryDraw(widget, out Action<Rect> overview))
            {
                overview(widget.Bounds);
                return;
            }

            if (_skills.TryDraw(widget, out Action<Rect> skills))
            {
                skills(widget.Bounds);
                return;
            }

            if (_gear.TryDraw(widget, out Action<Rect> gear))
            {
                gear(widget.Bounds);
                return;
            }

            if (_wardrobe.TryDraw(widget, out Action<Rect> wardrobe))
            {
                wardrobe(widget.Bounds);
            }
        }
    }
}
