using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Encounters;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// The desktop spike's battle: a real PvE fight built from the authored content, exactly as the
    /// game would field it — a save holding the chosen species at one level with their default
    /// loadouts, against an encounter template (<see cref="EncounterPlan.FromTemplate"/>, its
    /// calibrated difficulty included) — handed to <see cref="BattleSession.Begin"/> with a fixed
    /// seed. No avatar: it has no tile, and the spike shows beasts.
    /// </summary>
    public static class DemoBattle
    {
        public const string DefaultEncounterId = "boss_r01_hollow_warden";
        public const int DefaultLevel = 10;
        public const int DefaultSeed = 20260924;

        /// <summary>The default team: Phoenix leads (its fire skills are the fully authored VFX).</summary>
        public static readonly string[] DefaultTeam = { "phoenix", "golem", "kirin" };

        /// <summary>
        /// The setup, and which species each battle unit is (unit id to species or enemy id), for
        /// choosing its sprite. Null with <paramref name="error"/> set when a species or the
        /// encounter is unknown.
        /// </summary>
        public static BattleSetup Create(GameContent content, int seed, out Dictionary<string, string> speciesByUnit, out string error,
                                         IReadOnlyList<string> team = null, string encounterId = DefaultEncounterId, int level = DefaultLevel)
        {
            speciesByUnit = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            team = team ?? DefaultTeam;

            PlayerSave save = PlayerSave.CreateNew();
            BattleSetup setup = new BattleSetup { Save = save, Content = content.Battle, Seed = seed, IncludeAvatar = false };

            for (int i = 0; i < team.Count; i++)
            {
                SpeciesKitData kit = Array.Find(content.SkillLibrary.SpeciesKits, k => k.SpeciesId == team[i]);
                if (kit == null || content.Battle.GetSpecies(team[i]) == null)
                {
                    error = "Unknown species '" + team[i] + "'.";
                    return null;
                }

                string beastId = "b" + (i + 1).ToString(CultureInfo.InvariantCulture);
                OwnedBeast beast = OwnedBeast.Create(beastId, kit.SpeciesId, level);
                for (int slot = 0; slot < kit.DefaultLoadout.Length; slot++)
                {
                    beast.Skills.Learn(kit.DefaultLoadout[slot]);
                    beast.Skills.Equip(slot, kit.DefaultLoadout[slot]);
                }

                save.Beasts.Add(beast);
                setup.TeamBeastIds.Add(beastId);
                speciesByUnit[BattleSession.BeastUnitIdPrefix + beastId] = kit.SpeciesId;
            }

            EncounterPlan plan = EncounterPlan.FromTemplate(content.Encounters, content.Enemies, encounterId, level);
            if (plan == null)
            {
                error = "Unknown encounter template '" + encounterId + "'.";
                return null;
            }

            setup.Encounter = plan.ToSetup();
            for (int i = 0; i < setup.Encounter.Enemies.Count; i++)
            {
                EnemySpec spec = setup.Encounter.Enemies[i];
                string unitId = string.IsNullOrEmpty(spec.UnitId) ? "enemy" + (i + 1).ToString(CultureInfo.InvariantCulture) : spec.UnitId;
                speciesByUnit[unitId] = spec.SpeciesId;
            }

            return setup;
        }
    }
}
