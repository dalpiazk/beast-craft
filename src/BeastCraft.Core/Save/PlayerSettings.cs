using System;

namespace BeastCraft.Save
{
    /// <summary>
    /// The player's game settings: preferences, not progress. Kept apart from <see cref="PlayerSave"/>
    /// (its own slot, <see cref="PlayerSettingsStore"/>) so a setting never rides on a save's schema
    /// or migrations, and one device's settings apply to every save slot. Plain serializable data with
    /// public fields, JsonUtility-safe; a key missing from the file keeps the field's default, so a
    /// setting added later reads as its default from an older file.
    /// See the progression-and-saves design doc, "Player settings".
    /// </summary>
    [Serializable]
    public class PlayerSettings
    {
        /// <summary>The settings format this build writes.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>The format the file was written in; stamped by <see cref="PlayerSettingsStore.Save"/>.</summary>
        public int SchemaVersion = CurrentSchemaVersion;

        /// <summary>
        /// Whether the game may suggest a counter team after repeated losses
        /// (<c>BeastCraft.Battle.Scouting.TeamSuggestionPolicy</c>). On by default. The enemy-element
        /// preview is not a setting: it is always shown.
        /// </summary>
        public bool TeamSuggestionsEnabled = true;

        /// <summary>
        /// How much of each skill's visual effect the battle plays: Full (the default), Reduced or
        /// Minimal (see <see cref="Save.EffectsIntensity"/>). Presentation only.
        /// </summary>
        public EffectsIntensity EffectsIntensity = EffectsIntensity.Full;

        /// <summary>Whether hits shake the battle camera. On by default.</summary>
        public bool ScreenShake = true;

        /// <summary>
        /// Whether effects may flash (accessibility): the white hit flash on a struck unit and the
        /// bright additive bursts. On by default; off leaves every effect's other parts playing.
        /// </summary>
        public bool Flashes = true;

        /// <summary>
        /// The battle playback speed a campaign battle starts at: 1, 2 or 3 (x1/x2/x3; anything else
        /// reads as 1). Changed from the battle's speed buttons or the settings. Presentation only.
        /// </summary>
        public int BattleSpeed = 1;

        /// <summary>
        /// After a won battle's results, go straight on to the next recommended location's encounter
        /// instead of stopping at the map. Off by default.
        /// </summary>
        public bool AutoAdvance;

        /// <summary>
        /// A local notification when the idle rewards reach their cap (the Android host only; other
        /// hosts ignore it). Off by default.
        /// </summary>
        public bool IdleNotifications;
    }
}
