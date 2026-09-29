using System;
using System.Collections.Generic;
using BeastCraft.Localization;
using BeastCraft.Progression;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One row of the achievements list: earned or not, its condition in words, and the title it awards.</summary>
    public sealed class AchievementRow
    {
        public string AchievementId;
        public string DisplayName;
        public string ConditionText;
        public string TitleId;
        public string TitleText;
        public bool Earned;
    }

    /// <summary>One row of the title picker: an owned title, or "No title" ("" clears the equipped one).</summary>
    public sealed class TitleRow
    {
        /// <summary>"" for "no title".</summary>
        public string TitleId;

        public string Text;
        public bool Equipped;
    }

    /// <summary>
    /// The achievements screen (the Collector persona, docs/design/compendium-achievements.md): every
    /// achievement, earned or not, with its condition in words and the title it awards
    /// (<see cref="AchievementLibrary"/>, <see cref="PlayerSave.Achievements"/>), and the title picker —
    /// every owned title plus "no title", one equipped at a time
    /// (<see cref="AchievementProgress.EquippedTitleId"/>, set directly: a title has no unlock/lock
    /// state to check beyond ownership, the same way <c>Economy.CosmeticRules.TrySetOption</c> sets a
    /// look directly). Equipping persists through the normal save flow
    /// (<see cref="GameSession.Autosave"/>). Never touches a stat: titles are display only.
    /// </summary>
    public sealed class AchievementsViewModel
    {
        private readonly GameSession _session;

        public AchievementsViewModel(GameSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            Refresh();
        }

        public List<AchievementRow> Achievements { get; } = new List<AchievementRow>();

        public List<TitleRow> Titles { get; } = new List<TitleRow>();

        /// <summary>The Beastbinder's level, shown at the top of the screen beside its title.</summary>
        public int AvatarLevel { get; private set; }

        /// <summary>The equipped title's text ("" for none).</summary>
        public string EquippedTitleText { get; private set; }

        /// <summary>"&lt;title&gt; Beastbinder" with a title equipped, else plain "Beastbinder".</summary>
        public string AvatarDisplayName { get; private set; }

        /// <summary>How many achievements are earned, of the total.</summary>
        public int EarnedCount { get; private set; }

        /// <summary>Re-reads the save (an achievement was earned, or a title was equipped elsewhere).</summary>
        public void Refresh()
        {
            Achievements.Clear();
            Titles.Clear();
            EarnedCount = 0;
            PlayerSave save = _session.Save;
            save?.EnsureInitialized();
            AchievementLibrary library = _session.Content.Achievements?.Library;
            AvatarLevel = save?.Avatar?.Level ?? 1;
            EquippedTitleText = TitleTextOf(library, save?.Achievements?.EquippedTitleId);
            AvatarDisplayName = TitledName(save, library, CampaignAvatar.DisplayName(_session.Content.Text));
            if (library == null)
            {
                return;
            }

            foreach (AchievementData achievement in library.All)
            {
                if (achievement == null)
                {
                    continue;
                }

                bool earned = save != null && save.Achievements.HasEarned(achievement.AchievementId);
                EarnedCount += earned ? 1 : 0;
                Achievements.Add(new AchievementRow
                {
                    AchievementId = achievement.AchievementId,
                    DisplayName = achievement.DisplayName,
                    ConditionText = ConditionTextFor(_session, achievement),
                    TitleId = achievement.TitleId,
                    TitleText = achievement.TitleText,
                    Earned = earned
                });
            }

            string equipped = save?.Achievements?.EquippedTitleId ?? string.Empty;
            Titles.Add(new TitleRow { TitleId = string.Empty, Text = _session.Content.Text.Get("ui.achievements.no_title"), Equipped = equipped.Length == 0 });
            foreach (string titleId in save?.Achievements?.OwnedTitleIds ?? new List<string>())
            {
                Titles.Add(new TitleRow { TitleId = titleId, Text = TitleTextOf(library, titleId), Equipped = equipped == titleId });
            }
        }

        /// <summary>Equips <paramref name="titleId"/> ("" clears it); false (nothing changes) for a title not owned. Autosaves and refreshes on success.</summary>
        public bool Equip(string titleId)
        {
            PlayerSave save = _session.Save;
            if (save == null)
            {
                return false;
            }

            titleId = titleId ?? string.Empty;
            if (titleId.Length > 0 && !save.Achievements.HasTitle(titleId))
            {
                return false;
            }

            save.Achievements.EquippedTitleId = titleId;
            _session.Autosave(AutosaveReason.PlayerEdit);
            Refresh();
            return true;
        }

        /// <summary>The achievement that awards <paramref name="titleId"/>, or null.</summary>
        private static AchievementData FindByTitle(AchievementLibrary library, string titleId)
        {
            if (library == null || string.IsNullOrEmpty(titleId))
            {
                return null;
            }

            foreach (AchievementData achievement in library.All)
            {
                if (achievement != null && achievement.TitleId == titleId)
                {
                    return achievement;
                }
            }

            return null;
        }

        private static string TitleTextOf(AchievementLibrary library, string titleId)
        {
            return FindByTitle(library, titleId)?.TitleText ?? string.Empty;
        }

        /// <summary>The equipped title's text ("" without one, or without <paramref name="library"/>); the static counterpart of <see cref="EquippedTitleText"/>.</summary>
        public static string EquippedTitleTextOf(PlayerSave save, AchievementLibrary library)
        {
            return TitleTextOf(library, save?.Achievements?.EquippedTitleId);
        }

        /// <summary>"&lt;title&gt; &lt;baseName&gt;" with the equipped title, else plain <paramref name="baseName"/>.</summary>
        public static string TitledName(PlayerSave save, AchievementLibrary library, string baseName)
        {
            string title = EquippedTitleTextOf(save, library);
            return string.IsNullOrEmpty(title) ? baseName : title + " " + baseName;
        }

        /// <summary>An achievement's condition, in words (mirrors <c>AchievementRules.IsMet</c>'s switch).</summary>
        private static string ConditionTextFor(GameSession session, AchievementData def)
        {
            StringTable text = session.Content.Text;
            switch (def.Kind)
            {
                case AchievementKinds.BossCleared:
                    return text.Format("ui.achievements.cond_boss", RegionName(session, def.RegionId));
                case AchievementKinds.AllBossesCleared:
                    return text.Get("ui.achievements.cond_all_bosses");
                case AchievementKinds.RegionExplored:
                    return text.Format("ui.achievements.cond_explore", RegionName(session, def.RegionId));
                case AchievementKinds.AllRegionsExplored:
                    return text.Get("ui.achievements.cond_explore_all");
                case AchievementKinds.KinshipSitesClaimed:
                    return text.Format(def.Threshold == 1 ? "ui.achievements.cond_kinship_one" : "ui.achievements.cond_kinship", def.Threshold);
                case AchievementKinds.AllKinshipClaimed:
                    return text.Get("ui.achievements.cond_kinship_all");
                case AchievementKinds.BeastsOwned:
                    return text.Format("ui.achievements.cond_beasts", def.Threshold);
                case AchievementKinds.RegionLoreComplete:
                    return text.Format("ui.achievements.cond_lore_region", RegionName(session, def.RegionId));
                case AchievementKinds.AllLoreFound:
                    return text.Get("ui.achievements.cond_lore_all");
                case AchievementKinds.CompendiumPercent:
                    return text.Format("ui.achievements.cond_compendium", def.Threshold);
                case AchievementKinds.AvatarLevel:
                    return text.Format("ui.achievements.cond_avatar_level", def.Threshold);
                case AchievementKinds.BeastLevel:
                    return text.Format("ui.achievements.cond_beast_level", def.Threshold);
                case AchievementKinds.SideStoryComplete:
                    return text.Format("ui.achievements.cond_side_story", SideStoryName(session, def.StoryId));
                case AchievementKinds.LocationsSoothed:
                    return text.Format(def.Threshold == 1 ? "ui.achievements.cond_soothe_one" : "ui.achievements.cond_soothe", def.Threshold);
                default:
                    return string.Empty;
            }
        }

        private static string SideStoryName(GameSession session, string storyId)
        {
            return session.Content.Dialogue.SideStory(storyId)?.DisplayName ?? storyId;
        }

        private static string RegionName(GameSession session, string regionId)
        {
            return session.Content.Campaign.GetRegion(regionId)?.DisplayName ?? regionId;
        }
    }
}
