using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Screens
{
    /// <summary>One enemy group against the chosen team: the level gap and its damage multiplier either way, and its skills.</summary>
    public sealed class EnemyInsightView
    {
        public string Name;
        public string EnemyId;
        public Element Element;
        public int Level;

        /// <summary>The enemy's level minus the team's (its beasts' mean, rounded).</summary>
        public int Gap;

        /// <summary>The multiplier on the team's hits on it (<see cref="DamageFormula.GetLevelMultiplier"/>(team, enemy)).</summary>
        public double TeamDealt;

        /// <summary>The multiplier on its hits on the team (<see cref="DamageFormula.GetLevelMultiplier"/>(enemy, team)).</summary>
        public double EnemyDealt;

        /// <summary>E.g. "2 levels above your team", "Level with your team".</summary>
        public string GapText;

        /// <summary>Its kit, each card with its targeting rule in words.</summary>
        public List<SkillCard> Skills = new List<SkillCard>();
    }

    /// <summary>One cell of the matchup matrix: a team beast (row) against an enemy group (column).</summary>
    public sealed class MatchupCellView
    {
        /// <summary>The beast's hits on the enemy (its attacking element against the enemy's).</summary>
        public float Dealt;

        /// <summary>The enemy's hits on the beast (the enemy's element against the beast's).</summary>
        public float Taken;

        /// <summary>Good (+1: deals more or takes less), bad (-1), or even (0): the colour.</summary>
        public int Verdict;
    }

    /// <summary>
    /// The encounter preview's Theorycrafter additions for the chosen team: a level-gap indicator per
    /// enemy group with the damage multiplier either way, a colour-coded element matrix (team beasts
    /// by enemy groups), and each enemy's skills with their targeting rules. Read from the Core rules
    /// (<see cref="DamageFormula.GetLevelMultiplier"/>, <see cref="ElementChart"/>); changes nothing.
    /// </summary>
    public sealed class EncounterInsightView
    {
        /// <summary>The team's level the gaps are measured from (its beasts' mean, rounded).</summary>
        public int TeamLevel;

        public List<EnemyInsightView> Enemies = new List<EnemyInsightView>();

        /// <summary>The team beasts (matrix rows).</summary>
        public List<PartyMemberView> Team = new List<PartyMemberView>();

        /// <summary>The team beasts' attacking elements, by row.</summary>
        public List<Element> TeamAttack = new List<Element>();

        /// <summary>[row][column]: team beast by enemy group.</summary>
        public List<List<MatchupCellView>> Matrix = new List<List<MatchupCellView>>();

        /// <summary>The team's elements (for the element chart's highlight).</summary>
        public List<Element> TeamElements = new List<Element>();

        /// <summary>The insight for <paramref name="encounter"/>'s current team.</summary>
        public static EncounterInsightView For(GameSession session, EncounterViewModel encounter)
        {
            EncounterInsightView view = new EncounterInsightView();
            if (session == null || encounter?.Battle == null)
            {
                return view;
            }

            GameContent content = session.Content;
            int total = 0;
            foreach (string id in encounter.Team)
            {
                PartyMemberView member = encounter.Owned.Find(m => m.BeastId == id);
                if (member == null)
                {
                    continue;
                }

                view.Team.Add(member);
                total += member.Level;
                OwnedBeast beast = session.Save.FindBeast(id);
                CreatureSpeciesSO species = content.Battle.GetSpecies(member.SpeciesId);
                view.TeamAttack.Add(AttackElement(content, beast, species));
                foreach (Element element in species?.Elements ?? new Element[0])
                {
                    if (element != Element.None && !view.TeamElements.Contains(element))
                    {
                        view.TeamElements.Add(element);
                    }
                }
            }

            view.TeamLevel = view.Team.Count == 0 ? encounter.Level : (int)Math.Round((double)total / view.Team.Count, MidpointRounding.AwayFromZero);
            foreach (EnemyGroupView group in encounter.Enemies)
            {
                int gap = group.Level - view.TeamLevel;
                EnemyInsightView enemy = new EnemyInsightView
                {
                    Name = group.Name,
                    EnemyId = group.EnemyId,
                    Element = group.Element,
                    Level = group.Level,
                    Gap = gap,
                    TeamDealt = DamageFormula.GetLevelMultiplier(view.TeamLevel, group.Level),
                    EnemyDealt = DamageFormula.GetLevelMultiplier(group.Level, view.TeamLevel),
                    GapText = gap == 0 ? "Level with your team" : Math.Abs(gap) + (Math.Abs(gap) == 1 ? " level " : " levels ") + (gap > 0 ? "above" : "below") + " your team"
                };
                foreach (SkillSO skill in content.Enemies.Kit(group.EnemyId, group.Element) ?? new SkillSO[0])
                {
                    if (skill != null)
                    {
                        enemy.Skills.Add(SkillCard.Of(skill, content.Glossary));
                    }
                }

                view.Enemies.Add(enemy);
            }

            for (int r = 0; r < view.Team.Count; r++)
            {
                CreatureSpeciesSO species = content.Battle.GetSpecies(view.Team[r].SpeciesId);
                IReadOnlyList<Element> defend = species?.Elements ?? new Element[0];
                List<MatchupCellView> row = new List<MatchupCellView>();
                foreach (EnemyInsightView enemy in view.Enemies)
                {
                    float dealt = ElementChart.GetMultiplier(view.TeamAttack[r], enemy.Element);

                    // An enemy's kit carries its own element (EnemyCatalog.Kit).
                    float taken = ElementChart.GetMultiplier(enemy.Element, defend);
                    row.Add(new MatchupCellView { Dealt = dealt, Taken = taken, Verdict = Math.Sign(Math.Sign(dealt - 1f) - Math.Sign(taken - 1f)) });
                }

                view.Matrix.Add(row);
            }

            return view;
        }

        /// <summary>A beast's attacking element: its first equipped damaging skill's, else its species' first.</summary>
        public static Element AttackElement(GameContent content, OwnedBeast beast, CreatureSpeciesSO species)
        {
            for (int slot = 0; beast?.Skills != null && slot < beast.Skills.SlotCount; slot++)
            {
                SkillSO skill = content.Battle.GetSkill(beast.Skills.GetEquipped(slot));
                if (skill == null || skill.Element == Element.None)
                {
                    continue;
                }

                foreach (SkillEffect effect in skill.Effects ?? new List<SkillEffect>())
                {
                    if (effect != null && effect.EffectType == SkillEffectType.Damage)
                    {
                        return skill.Element;
                    }
                }
            }

            return RosterViewModel.PrimaryElement(species);
        }
    }
}
