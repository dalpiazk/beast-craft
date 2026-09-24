using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;

namespace BeastCraft.Battle.Scouting
{
    /// <summary>
    /// What the player sees of an encounter before placing a team: the arena, how many enemies
    /// there are, and the enemies grouped into lines, redacted to a <see cref="ScoutingDetail"/>.
    /// Enemy elements are meant to be visible (<see cref="ScoutingDetail.Full"/> is the default) so
    /// the player can counter-pick against the element chart. Pure: a function of the lineup and
    /// the detail level, with no randomness. See the battle-system design doc, "Encounter preview".
    /// <para>
    /// <strong>Grouping.</strong> Enemies with the same element, stance and display name (ordinal)
    /// form one group; groups keep the order their first enemy has in the lineup (front to back).
    /// </para>
    /// <para>
    /// <strong>Redaction.</strong> <see cref="ScoutingDetail.Full"/> keeps every group as built.
    /// <see cref="ScoutingDetail.ElementsOnly"/> hides names and stances and merges the groups that
    /// share an element (hidden fields must not leak through the number of lines), in first-seen
    /// order. <see cref="ScoutingDetail.DominantElementOnly"/> leaves one line: the element carried
    /// by the most enemies (a tie goes to the lowest <see cref="Element"/> value, so
    /// <c>Element.None</c> wins a tie it is part of) with the total enemy count.
    /// </para>
    /// <para>
    /// Non-throwing: a null lineup is an empty encounter, null entries are skipped, and an unknown
    /// detail value is treated as <see cref="ScoutingDetail.Full"/>.
    /// </para>
    /// </summary>
    public sealed class EncounterPreview
    {
        private EncounterPreview(ArenaSize arena, ScoutingDetail detail, int totalEnemies, IReadOnlyList<EncounterPreviewGroup> groups)
        {
            Arena = arena;
            Detail = detail;
            TotalEnemies = totalEnemies;
            Groups = groups;
        }

        public ArenaSize Arena { get; }

        /// <summary>The detail level the preview was built at.</summary>
        public ScoutingDetail Detail { get; }

        /// <summary>Every enemy the preview stands for; always the sum of the groups' counts.</summary>
        public int TotalEnemies { get; }

        /// <summary>The lines, front to back; empty for an empty encounter. Never null.</summary>
        public IReadOnlyList<EncounterPreviewGroup> Groups { get; }

        /// <summary>
        /// The element carried by the most enemies, a tie going to the lowest value; null for an
        /// empty preview. The same answer at every detail level.
        /// </summary>
        public Element? DominantElement
        {
            get { return Groups.Count == 0 ? (Element?)null : Dominant(Groups, TotalEnemies)[0].Element; }
        }

        /// <summary>The preview of <paramref name="enemies"/> (in lineup order) at <paramref name="detail"/>.</summary>
        public static EncounterPreview Build(IReadOnlyList<IEncounterPreviewSource> enemies, ArenaSize arena, ScoutingDetail detail = ScoutingDetail.Full)
        {
            List<EncounterPreviewGroup> full = Group(enemies);
            int total = 0;
            for (int i = 0; i < full.Count; i++)
            {
                total += full[i].Count;
            }

            switch (detail)
            {
                case ScoutingDetail.ElementsOnly:
                    return new EncounterPreview(arena, detail, total, ByElement(full));
                case ScoutingDetail.DominantElementOnly:
                    return new EncounterPreview(arena, detail, total, Dominant(full, total));
                default:
                    return new EncounterPreview(arena, ScoutingDetail.Full, total, full);
            }
        }

        private static List<EncounterPreviewGroup> Group(IReadOnlyList<IEncounterPreviewSource> enemies)
        {
            List<EncounterPreviewGroup> groups = new List<EncounterPreviewGroup>();
            if (enemies == null)
            {
                return groups;
            }

            List<IEncounterPreviewSource> keys = new List<IEncounterPreviewSource>();
            List<int> counts = new List<int>();
            for (int i = 0; i < enemies.Count; i++)
            {
                IEncounterPreviewSource enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                int found = -1;
                for (int g = 0; g < keys.Count && found < 0; g++)
                {
                    IEncounterPreviewSource key = keys[g];
                    if (key.Element == enemy.Element && key.Stance == enemy.Stance && string.Equals(key.DisplayName, enemy.DisplayName, StringComparison.Ordinal))
                    {
                        found = g;
                    }
                }

                if (found < 0)
                {
                    keys.Add(enemy);
                    counts.Add(1);
                }
                else
                {
                    counts[found]++;
                }
            }

            for (int g = 0; g < keys.Count; g++)
            {
                groups.Add(new EncounterPreviewGroup(keys[g].DisplayName, keys[g].Element, keys[g].Stance, counts[g]));
            }

            return groups;
        }

        private static List<EncounterPreviewGroup> ByElement(List<EncounterPreviewGroup> full)
        {
            List<Element> order = new List<Element>();
            List<int> counts = new List<int>();
            for (int i = 0; i < full.Count; i++)
            {
                int at = order.IndexOf(full[i].Element);
                if (at < 0)
                {
                    order.Add(full[i].Element);
                    counts.Add(full[i].Count);
                }
                else
                {
                    counts[at] += full[i].Count;
                }
            }

            List<EncounterPreviewGroup> groups = new List<EncounterPreviewGroup>();
            for (int i = 0; i < order.Count; i++)
            {
                groups.Add(new EncounterPreviewGroup(null, order[i], null, counts[i]));
            }

            return groups;
        }

        private static List<EncounterPreviewGroup> Dominant(IReadOnlyList<EncounterPreviewGroup> groups, int total)
        {
            List<EncounterPreviewGroup> result = new List<EncounterPreviewGroup>();
            if (groups.Count == 0)
            {
                return result;
            }

            SortedDictionary<int, int> byElement = new SortedDictionary<int, int>();
            for (int i = 0; i < groups.Count; i++)
            {
                int key = (int)groups[i].Element;
                byElement.TryGetValue(key, out int count);
                byElement[key] = count + groups[i].Count;
            }

            int best = 0;
            int bestCount = -1;
            foreach (KeyValuePair<int, int> pair in byElement)
            {
                // Ascending keys and a strict comparison: a tie stays with the lowest value.
                if (pair.Value > bestCount)
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }

            result.Add(new EncounterPreviewGroup(null, (Element)best, null, total));
            return result;
        }
    }
}
