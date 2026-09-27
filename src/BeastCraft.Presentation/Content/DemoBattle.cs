using System;
using System.Collections.Generic;
using System.Globalization;
using BeastCraft.Battle.Grid;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Save;
using BeastCraft.Session;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Content
{
    /// <summary>
    /// The desktop spike's battle: a real PvE fight built from the authored content, exactly as the
    /// game would field it — a save holding the chosen species at one level with their default
    /// loadouts, against an encounter template at its own level (<see cref="EncounterPlan.FromTemplate"/>, its
    /// calibrated difficulty included) — handed to <see cref="BattleSession.Begin"/> with a fixed
    /// seed. No avatar: it has no tile, and the spike shows beasts.
    /// </summary>
    public static class DemoBattle
    {
        public const string DefaultEncounterId = "boss_r01_hollow_warden";

        /// <summary>The region the demo shows when nothing says otherwise: the Verdant Hollow, the only one with enemy art so far.</summary>
        public const string DefaultRegionId = "r01";

        /// <summary>
        /// The team's level. A small party against a boss tuned for a fuller one needs the edge: at
        /// 20 against the encounter's 10 the default seed is a 21-turn player victory.
        /// </summary>
        public const int DefaultLevel = 20;

        /// <summary>The encounter's level (its calibrated difficulty is for this level).</summary>
        public const int DefaultEncounterLevel = 10;

        /// <summary>
        /// The battle's seed: with <see cref="DefaultTeam"/>, the first seed from 20260924 on at which
        /// the showcase statuses all happen (a burn, a heal, a taunt, a shield and a stun) and the team
        /// wins. The rules are untouched; only which battle the demo shows is chosen.
        /// </summary>
        public const int DefaultSeed = 20260933;

        /// <summary>
        /// The default team: Phoenix leads (its fire skills are the fully authored VFX: burn, and its
        /// rebirth heal and shield); the Golem taunts and shields, the Kirin heals, and the Frost Wyrm's
        /// Deep Freeze stuns, so every status VFX default shows in one battle.
        /// </summary>
        public static readonly string[] DefaultTeam = { "phoenix", "golem", "kirin", "frost_wyrm" };

        /// <summary>
        /// The setup, and which species each battle unit is (unit id to species or enemy id), for
        /// choosing its sprite. Null with <paramref name="error"/> set when a species or the
        /// encounter is unknown. <paramref name="encounterId"/> is a template, else a shape id (one
        /// generated lineup of that shape, drawn with <paramref name="seed"/>);
        /// <paramref name="arena"/>, when set, fights it on that arena instead of its own (the
        /// lineup must seat there, or the battle cannot begin).
        /// <para>
        /// <paramref name="lineup"/>, when set, fields exactly those enemies instead (each
        /// <c>enemy_id:Element</c>, e.g. <c>brute:Fire</c>; no element = none) at the squad's
        /// difficulty, on <paramref name="arena"/> or Medium: for looking at one enemy type's art
        /// across elements. Which battle it is changes; the rules do not.
        /// </para>
        /// </summary>
        public static BattleSetup Create(GameContent content, int seed, out Dictionary<string, string> speciesByUnit, out string error,
                                         IReadOnlyList<string> team = null, string encounterId = DefaultEncounterId, int level = DefaultLevel,
                                         int encounterLevel = DefaultEncounterLevel, ArenaSize? arena = null, IReadOnlyList<string> lineup = null)
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

            if (lineup != null && lineup.Count > 0)
            {
                setup.Encounter = Lineup(content, lineup, encounterLevel, arena ?? ArenaSize.Medium, out error);
                if (setup.Encounter == null)
                {
                    return null;
                }

                for (int i = 0; i < setup.Encounter.Enemies.Count; i++)
                {
                    speciesByUnit["enemy" + (i + 1).ToString(CultureInfo.InvariantCulture)] = setup.Encounter.Enemies[i].SpeciesId;
                }

                return setup;
            }

            // A template, else a shape id: one generated lineup of that shape, drawn with the seed.
            EncounterPlan plan = EncounterPlan.FromTemplate(content.Encounters, content.Enemies, encounterId, encounterLevel) ??
                                 EncounterPlan.Generate(content.Encounters, content.Enemies, encounterId, encounterLevel, seed);
            if (plan == null)
            {
                error = "Unknown encounter template or shape '" + encounterId + "'.";
                return null;
            }

            setup.Encounter = plan.ToSetup();
            if (arena.HasValue)
            {
                setup.Encounter.Arena = arena.Value;
            }

            for (int i = 0; i < setup.Encounter.Enemies.Count; i++)
            {
                EnemySpec spec = setup.Encounter.Enemies[i];
                string unitId = string.IsNullOrEmpty(spec.UnitId) ? "enemy" + (i + 1).ToString(CultureInfo.InvariantCulture) : spec.UnitId;
                speciesByUnit[unitId] = spec.SpeciesId;
            }

            return setup;
        }

        /// <summary>
        /// The region an encounter belongs to, when the data says: the region whose boss (or Hard-mode
        /// boss) template it is. Null for a generated shape, which any region can draw.
        /// </summary>
        public static string RegionOf(GameContent content, string encounterId)
        {
            foreach (RegionData region in content?.Regions?.Regions ?? new RegionData[0])
            {
                if (region != null && !string.IsNullOrEmpty(encounterId) &&
                    (region.BossTemplateId == encounterId || (region.HardMode != null && region.HardMode.BossTemplateId == encounterId)))
                {
                    return region.RegionId;
                }
            }

            return null;
        }

        /// <summary>Whether <paramref name="regionId"/> is a region in regions.json.</summary>
        public static bool IsRegion(GameContent content, string regionId)
        {
            return Array.Exists(content?.Regions?.Regions ?? new RegionData[0], r => r != null && r.RegionId == regionId);
        }

        /// <summary>
        /// The art key a unit of <paramref name="speciesOrEnemyId"/> is drawn with in region
        /// <paramref name="regionId"/>: a roster species' own key, else the enemy's art for that
        /// region (<see cref="EnemyData.ArtKeyFor"/>, falling back to its default). Presentation only.
        /// </summary>
        public static string ArtKeyOf(GameContent content, string speciesOrEnemyId, string regionId)
        {
            string key = content?.Battle?.GetSpecies(speciesOrEnemyId)?.ArtKey;
            return key ?? content?.Enemies?.Get(speciesOrEnemyId)?.ArtKeyFor(regionId);
        }

        /// <summary>The <c>--lineup</c> encounter: each <c>enemy_id[:Element]</c> once, at the squad's calibrated difficulty.</summary>
        private static EncounterSetup Lineup(GameContent content, IReadOnlyList<string> lineup, int level, ArenaSize arena, out string error)
        {
            error = null;
            const string shape = "squad";
            EncounterSetup encounter = new EncounterSetup { Arena = arena, ShapeId = shape, EncounterLevel = level };
            double multiplier = content.Encounters.Multiplier(shape, level);
            foreach (string entry in lineup)
            {
                string[] parts = (entry ?? string.Empty).Split(':');
                EnemyData enemy = content.Enemies.Get(parts[0].Trim());
                Element element = Element.None;
                if (enemy == null || parts.Length > 2 || (parts.Length == 2 && !BeastRosterValidator.TryParseElement(parts[1].Trim(), out element)))
                {
                    error = "Unknown lineup entry '" + entry + "' (enemy_id or enemy_id:Element).";
                    return null;
                }

                encounter.Enemies.Add(new EnemySpec(enemy.EnemyId, level) { Element = element, StatMultiplier = multiplier, StatusResist = enemy.StatusResist });
            }

            return encounter;
        }
    }
}
