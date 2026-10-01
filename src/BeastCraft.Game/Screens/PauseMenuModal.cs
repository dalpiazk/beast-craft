using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;

namespace BeastCraft.Game.Screens
{
    /// <summary>
    /// The in-battle pause menu (menu-screens pass, #67): opened by the battle HUD's pause button, or
    /// Android Back / desktop Esc, over a campaign battle not yet decided (<see cref="BattleScreen.OpenPauseMenu"/>),
    /// freezing the battle clock while it — or anything pushed from it — is open.
    /// <list type="bullet">
    /// <item><b>Resume</b> closes it; the battle picks up exactly where it paused.</item>
    /// <item><b>Settings</b> opens the full settings screen over the paused battle
    /// (<see cref="BattleScreen.OpenSettingsFromPause"/>) and reopens this menu on the way back, since
    /// pushing a screen clears the modal stack.</item>
    /// <item><b>Retreat</b> (hidden for a Kinship trial, <see cref="BattleScreen.OffersRetreat"/>: a
    /// trial has no campaign retry count to forfeit, and the producer's ruling was scoped to the
    /// campaign map) asks first, then forfeits the battle outright through
    /// <see cref="BattleScreen.ConfirmRetreat"/> — producer decision, 2026-09-30: "same as losing the
    /// battle" (no rewards, the location stays open; see
    /// <see cref="BeastCraft.Campaign.CampaignRules.RetreatBattle"/> and
    /// docs/design/battle-system.md, "Adaptive assist and guidance").
    /// </list>
    /// Back closes the menu (resumes the battle), like most modals.
    /// </summary>
    public sealed class PauseMenuModal : GameModal
    {
        private const float RowHeight = 120f;
        private const float RowGap = 24f;

        public PauseMenuModal(ScreenContext ctx, BattleScreen battle) : base(ctx)
        {
            bool offerRetreat = battle.OffersRetreat;
            UiStyle style = ctx.Style;
            float width = 760f;
            int rows = offerRetreat ? 3 : 2;
            float height = 170f + rows * RowHeight + (rows - 1) * RowGap + 60f;
            Rect card = new Rect((PortraitLayout.CanvasWidth - width) / 2f, (PortraitLayout.CanvasHeight - height) / 2f, width, height);
            Panel panel = Ui.Add(new Panel { Bounds = card, StyleKey = "modal" });
            panel.Add(new Label
            {
                Bounds = new Rect(card.X, card.Y + 50f, width, 60f),
                Text = Loc("ui.battle.pause_title"),
                Size = style.TextSizes.Heading,
                ColorKey = "plum",
                Align = TextAlign.Center
            });

            float y = card.Y + 160f;
            Button resume = panel.Add(new Button { Id = "resume", Bounds = new Rect(card.X + 60f, y, width - 120f, RowHeight), Text = Loc("ui.battle.resume"), StyleKey = "primary" });
            resume.Clicked += Close;
            y += RowHeight + RowGap;

            Button settings = panel.Add(new Button
            {
                Id = "settings",
                Bounds = new Rect(card.X + 60f, y, width - 120f, RowHeight),
                Text = Loc("ui.title.settings"),
                StyleKey = "secondary",
                Glyph = "gear"
            });
            settings.Clicked += () =>
            {
                Close();
                battle.OpenSettingsFromPause();
            };
            y += RowHeight + RowGap;

            if (offerRetreat)
            {
                Button retreat = panel.Add(new Button { Id = "retreat", Bounds = new Rect(card.X + 60f, y, width - 120f, RowHeight), Text = Loc("ui.battle.retreat"), StyleKey = "danger" });
                retreat.Clicked += () =>
                {
                    Close();
                    Ctx.Stack.PushModal(new ConfirmModal(Ctx, Loc("ui.battle.retreat_title"), Loc("ui.battle.retreat_body"), Loc("ui.common.cancel"), Loc("ui.battle.retreat"),
                                                         battle.ConfirmRetreat, "danger"));
                };
            }
        }

        public override string Name
        {
            get { return "pause"; }
        }
    }
}
