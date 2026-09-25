using System;
using System.Collections.Generic;
using BeastCraft.Encounters;
using BeastCraft.Skills;

namespace BeastCraft.Presentation.Text
{
    /// <summary>
    /// Holds the glossary and the skill text to each other. <see cref="Validate(GlossaryData)"/>:
    /// the file is well formed (schema version, unique snake_case ids, unique names, a known
    /// category, a definition, forms that are words and belong to one term only).
    /// <see cref="ValidateText"/>: every term used in skill text resolves — each <c>[[...|Term]]</c>
    /// mark in a beast skill's, avatar active's, passive's or enemy-library skill's name or
    /// description names a glossary term and is closed — and every beast skill, avatar active and
    /// passive that applies a status (stun, taunt, shield, burn, poison, knockback) or a cleanse
    /// names it in its description (<see cref="RequiredTermIds"/>). Returns every problem found
    /// (empty = valid); never throws.
    /// </summary>
    public static class GlossaryValidator
    {
        /// <summary>The categories a term may have.</summary>
        public static readonly string[] Categories = { "Status", "Combat", "Stance", "Passive" };

        /// <summary>Checks the glossary file on its own.</summary>
        public static List<string> Validate(GlossaryData data)
        {
            List<string> errors = new List<string>();
            if (data == null)
            {
                errors.Add("No glossary.");
                return errors;
            }

            if (data.SchemaVersion != GlossaryData.CurrentSchemaVersion)
            {
                errors.Add("SchemaVersion is " + data.SchemaVersion + "; this build reads " + GlossaryData.CurrentSchemaVersion + ".");
            }

            if (data.Terms == null || data.Terms.Length == 0)
            {
                errors.Add("No terms.");
                return errors;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> formOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < data.Terms.Length; i++)
            {
                GlossaryTermData term = data.Terms[i];
                if (term == null)
                {
                    errors.Add("Term #" + i + " is null.");
                    continue;
                }

                string at = "Term '" + term.TermId + "'";
                if (!IsSnakeCase(term.TermId))
                {
                    errors.Add(at + ": TermId must be lowercase snake_case.");
                }
                else if (!names.Add(term.TermId))
                {
                    errors.Add(at + ": duplicate TermId.");
                }

                if (string.IsNullOrWhiteSpace(term.Term) || HasMarkup(term.Term))
                {
                    errors.Add(at + ": Term is empty or holds markup.");
                }
                else if (term.Term != term.TermId && !names.Add(term.Term))
                {
                    errors.Add(at + ": Term '" + term.Term + "' is another term's name or id.");
                }

                if (Array.IndexOf(Categories, term.Category) < 0)
                {
                    errors.Add(at + ": Category '" + term.Category + "' is not one of " + string.Join(", ", Categories) + ".");
                }

                if (string.IsNullOrWhiteSpace(term.Definition) || HasMarkup(term.Definition))
                {
                    errors.Add(at + ": Definition is empty or holds markup.");
                }

                if (term.Forms == null || term.Forms.Length == 0)
                {
                    errors.Add(at + ": no Forms.");
                    continue;
                }

                foreach (string form in term.Forms)
                {
                    if (string.IsNullOrWhiteSpace(form) || form.Trim() != form || HasMarkup(form) || !char.IsLetterOrDigit(form[0]) ||
                        !char.IsLetterOrDigit(form[form.Length - 1]))
                    {
                        errors.Add(at + ": form '" + form + "' must be words (starting and ending with a letter or digit), with no markup.");
                        continue;
                    }

                    if (formOwner.TryGetValue(form, out string owner))
                    {
                        errors.Add(at + ": form '" + form + "' also belongs to '" + owner + "' (letter case aside).");
                        continue;
                    }

                    formOwner[form] = term.TermId;
                }
            }

            return errors;
        }

        /// <summary>
        /// The glossary term ids the description of a skill with <paramref name="effects"/> of
        /// <paramref name="element"/> must name: each status it applies (a damage over time is a
        /// burn from a Fire skill, a poison otherwise) and a cleanse.
        /// </summary>
        public static List<string> RequiredTermIds(EffectData[] effects, string element)
        {
            List<string> ids = new List<string>();
            foreach (EffectData effect in effects ?? new EffectData[0])
            {
                string id = null;
                if (effect != null && effect.EffectType == "Cleanse")
                {
                    id = "cleanse";
                }
                else if (effect != null && effect.EffectType == "ApplyStatus")
                {
                    switch (effect.Status)
                    {
                        case "Stun":
                            id = "stun";
                            break;
                        case "Taunt":
                            id = "taunt";
                            break;
                        case "Shield":
                            id = "shield";
                            break;
                        case "Knockback":
                            id = "knockback";
                            break;
                        case "DamageOverTime":
                            id = element == "Fire" ? "burn" : "poison";
                            break;
                    }
                }

                if (id != null && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>
        /// Checks the skill text against <paramref name="glossary"/>: every mark resolves and is
        /// closed in <paramref name="skills"/> and <paramref name="enemies"/> (either may be null),
        /// and each skill-library skill and passive names the statuses it applies.
        /// </summary>
        public static List<string> ValidateText(Glossary glossary, SkillLibraryData skills, EnemyLibraryData enemies)
        {
            List<string> errors = new List<string>();
            glossary = glossary ?? Glossary.Empty;
            if (skills != null)
            {
                foreach (SkillData skill in skills.BeastSkills ?? new SkillData[0])
                {
                    CheckSkill(glossary, "Beast skill '" + skill?.SkillId + "'", skill?.DisplayName, skill?.Description, skill?.Effects, skill?.Element, true, errors);
                }

                foreach (SkillData skill in skills.AvatarActives ?? new SkillData[0])
                {
                    CheckSkill(glossary, "Avatar active '" + skill?.SkillId + "'", skill?.DisplayName, skill?.Description, skill?.Effects, skill?.Element, true, errors);
                }

                foreach (PassiveData passive in skills.AvatarPassives ?? new PassiveData[0])
                {
                    CheckSkill(glossary, "Avatar passive '" + passive?.PassiveId + "'", passive?.DisplayName, passive?.Description, passive?.Effects, passive?.Element, true,
                               errors);
                }
            }

            foreach (EnemyData enemy in enemies == null || enemies.Enemies == null ? new EnemyData[0] : enemies.Enemies)
            {
                foreach (SkillData skill in enemy?.Skills ?? new SkillData[0])
                {
                    CheckSkill(glossary, "Enemy '" + enemy.EnemyId + "' skill '" + skill?.SkillId + "'", skill?.DisplayName, skill?.Description, null, null, false, errors);
                }
            }

            return errors;
        }

        /// <summary>The problems with the marks in <paramref name="text"/>: unknown terms and unclosed or stray brackets.</summary>
        public static List<string> CheckMarks(Glossary glossary, string text)
        {
            List<string> problems = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return problems;
            }

            foreach (RichSpan span in glossary.Parse(text))
            {
                if (span.Broken)
                {
                    problems.Add("[[" + span.Text + "]] names no glossary term");
                }
                else if (span.Term == null && (span.Text.Contains(Glossary.MarkOpen) || span.Text.Contains(Glossary.MarkClose)))
                {
                    problems.Add("an unclosed or stray '[[' / ']]'");
                }
            }

            return problems;
        }

        private static void CheckSkill(Glossary glossary, string at, string name, string description, EffectData[] effects, string element, bool requireStatuses,
                                       List<string> errors)
        {
            foreach (string problem in CheckMarks(glossary, name))
            {
                errors.Add(at + " name: " + problem + ".");
            }

            foreach (string problem in CheckMarks(glossary, description))
            {
                errors.Add(at + " description: " + problem + ".");
            }

            if (!requireStatuses)
            {
                return;
            }

            List<GlossaryTerm> named = glossary.TermsIn(description);
            foreach (string id in RequiredTermIds(effects, element))
            {
                if (!named.Exists(t => t.TermId == id))
                {
                    errors.Add(at + ": applies '" + id + "' but its description never names it (use a glossary form, or [[shown words|Term]]).");
                }
            }
        }

        private static bool HasMarkup(string text)
        {
            return text.Contains(Glossary.MarkOpen) || text.Contains(Glossary.MarkClose) || text.Contains("|");
        }

        private static bool IsSnakeCase(string id)
        {
            if (string.IsNullOrEmpty(id) || id[0] == '_' || id[id.Length - 1] == '_')
            {
                return false;
            }

            foreach (char c in id)
            {
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
