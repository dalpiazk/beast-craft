using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Session;

namespace BeastCraft.Encounters
{
    /// <summary>
    /// One PvE encounter the game is about to field: its enemies, arena, level and difficulty
    /// multiplier, from a generated shape (<see cref="Generate"/>) or an authored template
    /// (<see cref="FromTemplate"/>). <see cref="Preview"/> is what scouting shows before the fight;
    /// <see cref="ToSetup"/> is what <see cref="BattleSession"/> fights, carrying the shape and level
    /// through to the rewards (the <c>BattleSession.ApplyRewards</c> overload that reads them from the result).
    /// <para>
    /// Which shape and level a map node offers is not decided here (a producer decision); the caller
    /// passes them, and a seed it derives itself — for example
    /// <c>LootRoller.DeriveSeed(saveSeed, nodeIndex)</c> — kept separate from the battle's own seed.
    /// </para>
    /// </summary>
    public sealed class EncounterPlan
    {
        private EncounterPlan(string shapeId, string dropShapeId, string encounterId, int level, double multiplier, ArenaSize arena, ElementScheme? scheme,
                              IReadOnlyList<EncounterLineupEnemy> enemies, EnemyCatalog catalog)
        {
            ShapeId = shapeId;
            DropShapeId = dropShapeId;
            EncounterId = encounterId;
            Level = level;
            Multiplier = multiplier;
            Arena = arena;
            ElementScheme = scheme;
            Enemies = enemies;
            Catalog = catalog;
        }

        /// <summary>The shape: the difficulty row (and, for a mainline shape, the drop-table cell).</summary>
        public string ShapeId { get; }

        /// <summary>
        /// The drop-table shape a clear pays out from (<see cref="EncounterLibrary.DropShapeOf"/>): the
        /// shape itself, or for a post-game shape the mainline shape it pays as. What
        /// <see cref="ToSetup"/> hands the rewards.
        /// </summary>
        public string DropShapeId { get; }

        /// <summary>The template's id, or null for a generated encounter.</summary>
        public string EncounterId { get; }

        /// <summary>The encounter level every enemy fights at (1-100).</summary>
        public int Level { get; }

        /// <summary>The stat multiplier every enemy is fielded with (<see cref="EnemyScaling"/>).</summary>
        public double Multiplier { get; }

        public ArenaSize Arena { get; }

        /// <summary>How a generated encounter's elements were drawn; null for a template (authored elements).</summary>
        public ElementScheme? ElementScheme { get; }

        /// <summary>Every enemy, front to back.</summary>
        public IReadOnlyList<EncounterLineupEnemy> Enemies { get; }

        /// <summary>The enemy catalog the plan was drawn from.</summary>
        public EnemyCatalog Catalog { get; }

        /// <summary>
        /// A generated encounter of <paramref name="shapeId"/> at <paramref name="level"/> (clamped to
        /// 1-100): one <see cref="EncounterGenerator"/> draw with <paramref name="seed"/>, fielded at
        /// <see cref="EncounterLibrary.Multiplier"/>. Null when the shape is unknown or cannot be drawn.
        /// </summary>
        public static EncounterPlan Generate(EncounterLibrary library, EnemyCatalog enemies, string shapeId, int level, int seed)
        {
            if (library == null || enemies == null)
            {
                return null;
            }

            EncounterLineup lineup = new EncounterGenerator(library, enemies, seed).Draw(shapeId);
            if (lineup == null)
            {
                return null;
            }

            int clamped = ClampLevel(level);
            return new EncounterPlan(lineup.ShapeId, library.DropShapeOf(lineup.ShapeId), null, clamped, library.Multiplier(lineup.ShapeId, clamped), lineup.Arena, lineup.ElementScheme,
                                     lineup.Enemies, enemies);
        }

        /// <summary>
        /// The authored template <paramref name="templateId"/> at <paramref name="level"/> (clamped to
        /// 1-100): its groups in order, each unit's element cycled from the group's list, fielded at
        /// <see cref="EncounterLibrary.TemplateMultiplier"/>. Null when the template is unknown or
        /// names an enemy the catalog lacks.
        /// </summary>
        public static EncounterPlan FromTemplate(EncounterLibrary library, EnemyCatalog enemies, string templateId, int level)
        {
            EncounterTemplateData template = library == null ? null : library.GetTemplate(templateId);
            if (template == null || enemies == null)
            {
                return null;
            }

            List<EncounterLineupEnemy> lineup = new List<EncounterLineupEnemy>();
            foreach (EncounterGroupData group in template.Groups ?? new EncounterGroupData[0])
            {
                EnemyData enemy = group == null ? null : enemies.Get(group.EnemyId);
                if (enemy == null)
                {
                    return null;
                }

                string[] elements = group.Elements ?? new string[0];
                for (int i = 0; i < group.Count; i++)
                {
                    Element element = Element.None;
                    if (elements.Length > 0)
                    {
                        BeastRosterValidator.TryParseElement(elements[i % elements.Length], out element);
                    }

                    lineup.Add(new EncounterLineupEnemy(enemy.EnemyId, enemy.DisplayName, element, enemies.StanceOf(enemy.EnemyId)));
                }
            }

            int clamped = ClampLevel(level);
            return new EncounterPlan(template.ShapeId, library.DropShapeOf(template.ShapeId), template.EncounterId, clamped, library.TemplateMultiplier(template, clamped),
                                     EncounterLibrary.ParseArena(template.Arena), null, lineup, enemies);
        }

        /// <summary>
        /// What <see cref="BattleSession.Run"/> fights: the arena, the drop shape (<see cref="DropShapeId"/>)
        /// and level (copied into the result for the rewards), and one <see cref="EnemySpec"/> per enemy — the enemy id as its
        /// species (resolved through <see cref="BattleContent.Enemies"/>), the plan's level, the unit's
        /// element, the plan's multiplier, the enemy's status resist and its catalog kit — auto-placed
        /// front to back in plan order. The content must carry the same enemy catalog.
        /// </summary>
        public EncounterSetup ToSetup()
        {
            EncounterSetup setup = new EncounterSetup { Arena = Arena, ShapeId = DropShapeId, EncounterLevel = Level };
            foreach (EncounterLineupEnemy enemy in Enemies)
            {
                EnemyData data = Catalog.Get(enemy.EnemyId);
                setup.Enemies.Add(new EnemySpec(enemy.EnemyId, Level)
                {
                    Element = enemy.Element,
                    StatMultiplier = Multiplier,
                    StatusResist = data == null ? 0 : data.StatusResist
                });
            }

            return setup;
        }

        /// <summary>The scouting preview of this encounter at <paramref name="detail"/> (<see cref="EncounterPreview.Build"/>).</summary>
        public EncounterPreview Preview(ScoutingDetail detail = ScoutingDetail.Full)
        {
            List<IEncounterPreviewSource> sources = new List<IEncounterPreviewSource>();
            foreach (EncounterLineupEnemy enemy in Enemies)
            {
                sources.Add(enemy);
            }

            return EncounterPreview.Build(sources, Arena, detail);
        }

        private static int ClampLevel(int level)
        {
            return level < EncounterDifficultyTable.MinLevel ? EncounterDifficultyTable.MinLevel : level > EncounterDifficultyTable.MaxLevel ? EncounterDifficultyTable.MaxLevel : level;
        }
    }
}
