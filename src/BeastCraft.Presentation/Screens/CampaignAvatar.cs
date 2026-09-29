using BeastCraft.Avatar;
using BeastCraft.Creatures;
using BeastCraft.Localization;
using BeastCraft.Presentation.Content;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>
    /// The Beastbinder (the player's avatar) as a campaign battle fields it, mirroring the balance
    /// simulator's <c>library</c> avatar exactly, since the encounter difficulty is calibrated with
    /// it present (<c>Tooling/BalanceSim/AvatarPresets.cs</c>): its equipped default actives and
    /// passives from the save, at the save's avatar level, with the simulator's stat fixture — 100 in
    /// every combat stat and <see cref="AvatarStatsSO.DefaultSpeed"/> Speed at level 100 on the
    /// roster's growth curve, Hp 1 (it is never targeted). It has no tile: it fills its own turn
    /// gauge and casts from off the board (<c>BattleAvatar</c>). A fixture until the avatar's stats
    /// are authored content.
    /// </summary>
    public static class CampaignAvatar
    {
        /// <summary>The fixture's combat stats at level 100 (the simulator's <c>AvatarPresets.StatAtMaxLevel</c>).</summary>
        public const int StatAtMaxLevel = 100;

        /// <summary>The unit id the avatar fights under (<c>BattleAvatar.DefaultId</c>).</summary>
        public const string UnitId = Battle.BattleAvatar.DefaultId;

        /// <summary>The avatar's name, as a text key.</summary>
        public const string DisplayNameKey = "ui.avatar.name";

        /// <summary>The avatar's name (<see cref="DisplayNameKey"/>).</summary>
        public static string DisplayName(StringTable text)
        {
            return text.Get(DisplayNameKey);
        }

        /// <summary>The avatar's stat profile: the simulator's fixture on the roster's curve (its first species', as the simulator reads it).</summary>
        public static AvatarStatsSO Profile(GameContent content)
        {
            AvatarStatsSO stats = new AvatarStatsSO();
            stats.BaseStats = new StatBlock(1, StatAtMaxLevel, StatAtMaxLevel, StatAtMaxLevel, StatAtMaxLevel, AvatarStatsSO.DefaultSpeed);
            string first = content?.Roster?.Species != null && content.Roster.Species.Length > 0 ? content.Roster.Species[0].SpeciesId : null;
            stats.Growth = content?.Battle?.GetSpecies(first)?.GrowthRate;
            return stats;
        }
    }
}
