using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Creatures;
using BeastCraft.Encounters;
using BeastCraft.Presentation.Cards;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Text;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The skill detail card's text: the glossary data and its validator (every term used in skill
    /// text resolves, and a skill names the statuses it applies), the deterministic, case-aware
    /// term matching and [[markup]], rich-text layout and hit-testing, the card model built from
    /// real skills, and the popup placement.
    /// </summary>
    public class GlossaryTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private static GlossaryData ShippedData()
        {
            return FieldJson.FromJson<GlossaryData>(File.ReadAllText(GameContent.PathOf(GameContent.FindRoot(), GlossaryData.ProjectRelativePath)));
        }

        private static Glossary Small()
        {
            return Glossary.Build(new GlossaryData
            {
                SchemaVersion = 1,
                Terms = new[]
                {
                    new GlossaryTermData { TermId = "stun", Term = "Stun", Category = "Status", Forms = new[] { "stun", "stunned" }, Definition = "Loses a turn." },
                    new GlossaryTermData { TermId = "knockback", Term = "Knockback", Category = "Status", Forms = new[] { "knock back", "knockback" }, Definition = "Pushed away." },
                    new GlossaryTermData { TermId = "crit", Term = "Crit", Category = "Combat", Forms = new[] { "crit", "critical hit", "critical" }, Definition = "x1.5." },
                    new GlossaryTermData { TermId = "vanguard", Term = "Vanguard", Category = "Stance", Forms = new[] { "Vanguard" }, Definition = "Front line." }
                }
            });
        }

        private static string Describe(List<RichSpan> spans)
        {
            StringBuilder text = new StringBuilder();
            foreach (RichSpan span in spans)
            {
                text.Append(span.Term == null ? span.Text : "{" + span.Text + "=" + span.Term.TermId + "}");
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------------------------------
        // The shipped glossary and skill text
        // ------------------------------------------------------------------------------------------

        [Test]
        public void TheShippedGlossary_IsValid_AndEverySkillTextTermResolves()
        {
            GlossaryData data = ShippedData();
            List<string> errors = GlossaryValidator.Validate(data);
            errors.AddRange(GlossaryValidator.ValidateText(Glossary.Build(data), Content.SkillLibrary, Content.EnemyLibrary));

            Assert.IsEmpty(errors, string.Join("\n", errors));
            foreach (string id in new[] { "stun", "shield", "taunt", "burn", "poison", "cleanse", "knockback", "crit", "vanguard", "ranged", "skirmisher" })
            {
                Assert.IsNotNull(Content.Glossary.Find(id), id);
            }
        }

        [Test]
        public void EveryStatusASkillApplies_IsHighlightedInItsDescription()
        {
            int checkedSkills = 0;
            foreach (SkillData skill in Content.SkillLibrary.BeastSkills)
            {
                List<GlossaryTerm> named = Content.Glossary.TermsIn(skill.Description);
                foreach (string id in GlossaryValidator.RequiredTermIds(skill.Effects, skill.Element))
                {
                    Assert.IsTrue(named.Exists(t => t.TermId == id), skill.SkillId + " names " + id);
                    checkedSkills++;
                }
            }

            Assert.Greater(checkedSkills, 25);
            Assert.AreEqual("A bolt of flame that sets the target {burning=burn} (stacks three times).",
                            Describe(Content.Glossary.Parse(Array.Find(Content.SkillLibrary.BeastSkills, s => s.SkillId == "ember_shot").Description)));
            Assert.AreEqual("A glance that may {turn the target to stone=stun} for a turn.",
                            Describe(Content.Glossary.Parse(Array.Find(Content.SkillLibrary.BeastSkills, s => s.SkillId == "petrifying_gaze").Description)));
        }

        [Test]
        public void Validator_ReportsAnUnknownMark_AStrayBracket_AndAnUnnamedStatus()
        {
            SkillLibraryData skills = new SkillLibraryData
            {
                BeastSkills = new[]
                {
                    new SkillData
                    {
                        SkillId = "a", DisplayName = "A", Description = "Hits hard and may [[daze|Dazzle]] it.",
                        Effects = new[] { new EffectData { EffectType = "Damage", Magnitude = 50 } }
                    },
                    new SkillData
                    {
                        SkillId = "b", DisplayName = "B", Description = "A heavy blow that sends it flying.",
                        Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Knockback", Magnitude = 2 } }
                    },
                    new SkillData
                    {
                        SkillId = "c", DisplayName = "C", Description = "A blow that may stun [[ it.",
                        Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Stun", DurationTurns = 1 } }
                    },
                    new SkillData
                    {
                        SkillId = "d", DisplayName = "D", Description = "A shove that may [[knock it over|Knockback]].",
                        Effects = new[] { new EffectData { EffectType = "ApplyStatus", Status = "Knockback", Magnitude = 1 } }
                    }
                }
            };
            EnemyLibraryData enemies = new EnemyLibraryData
            {
                Enemies = new[] { new EnemyData { EnemyId = "e", Skills = new[] { new SkillData { SkillId = "bite", Description = "Bites [[hard|Chomp]]." } } } }
            };

            List<string> errors = GlossaryValidator.ValidateText(Small(), skills, enemies);

            Assert.AreEqual(4, errors.Count, string.Join("\n", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("'a'") && e.Contains("names no glossary term")));
            Assert.IsTrue(errors.Exists(e => e.Contains("'b'") && e.Contains("knockback")), "applies a knockback it never names");
            Assert.IsTrue(errors.Exists(e => e.Contains("'c'") && e.Contains("unclosed")));
            Assert.IsTrue(errors.Exists(e => e.Contains("'bite'") && e.Contains("names no glossary term")), "enemy-library marks are checked too");
            Assert.IsFalse(errors.Exists(e => e.Contains("'d'")));
        }

        [Test]
        public void Validator_ChecksTheGlossaryItself()
        {
            GlossaryData data = new GlossaryData
            {
                SchemaVersion = 2,
                Terms = new[]
                {
                    new GlossaryTermData { TermId = "stun", Term = "Stun", Category = "Status", Forms = new[] { "stun" }, Definition = "x" },
                    new GlossaryTermData { TermId = "stun", Term = "Daze", Category = "Status", Forms = new[] { "daze" }, Definition = "x" },
                    new GlossaryTermData { TermId = "Bad Id", Term = "Bad", Category = "Weird", Forms = new[] { " bad", "Stun" }, Definition = "" },
                    new GlossaryTermData { TermId = "odd", Term = "Stun", Category = "Combat", Forms = new string[0], Definition = "x [[y]]" }
                }
            };

            List<string> errors = GlossaryValidator.Validate(data);

            Assert.IsTrue(errors.Exists(e => e.Contains("SchemaVersion")));
            Assert.IsTrue(errors.Exists(e => e.Contains("duplicate TermId")));
            Assert.IsTrue(errors.Exists(e => e.Contains("snake_case")));
            Assert.IsTrue(errors.Exists(e => e.Contains("Category 'Weird'")));
            Assert.IsTrue(errors.Exists(e => e.Contains("Definition is empty")));
            Assert.IsTrue(errors.Exists(e => e.Contains("form ' bad'")));
            Assert.IsTrue(errors.Exists(e => e.Contains("form 'Stun' also belongs to 'stun'")), "no form in two terms, letter case aside");
            Assert.IsTrue(errors.Exists(e => e.Contains("another term's name")));
            Assert.IsTrue(errors.Exists(e => e.Contains("'odd': no Forms")));
            Assert.IsTrue(errors.Exists(e => e.Contains("'odd': Definition is empty or holds markup")));
        }

        // ------------------------------------------------------------------------------------------
        // Matching
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Matching_IsWholeWord_LongestFirst_AndCaseAware()
        {
            Glossary glossary = Small();

            Assert.AreEqual("A {critical hit=crit}, then a {crit=crit}; {critical=crit}-hit chance.", Describe(glossary.Parse("A critical hit, then a crit; critical-hit chance.")));
            Assert.AreEqual("{Stun=stun} it; {stunned=stun} again. Stunning is not a form.", Describe(glossary.Parse("Stun it; stunned again. Stunning is not a form.")),
                            "a lowercase form matches with a capital first letter too, never inside a longer word");
            Assert.AreEqual("STUN is shouted, stUn is not a form.", Describe(glossary.Parse("STUN is shouted, stUn is not a form.")), "only lowercase or a capital first letter");
            Assert.AreEqual("The {Vanguard=vanguard} holds; a vanguard of one does not.", Describe(glossary.Parse("The Vanguard holds; a vanguard of one does not.")),
                            "a form with a capital (a name) matches only exactly");
            Assert.AreEqual("Crits and critics.", Describe(glossary.Parse("Crits and critics.")), "no partial words");
            Assert.AreEqual("{knock back=knockback} or {knockback=knockback}; knock it back.", Describe(glossary.Parse("knock back or knockback; knock it back.")));
            Assert.AreEqual("{Knock back=knockback}!", Describe(glossary.Parse("Knock back!")));
        }

        [Test]
        public void Markup_NamesATerm_ByIdOrName_AndIsKeptApartFromMatching()
        {
            Glossary glossary = Small();

            List<RichSpan> spans = glossary.Parse("It may [[turn to stone|Stun]], or [[knockback]] foes, or [[shove|Nope]].");

            Assert.AreEqual("It may {turn to stone=stun}, or {knockback=knockback} foes, or shove.", Describe(spans));
            Assert.IsTrue(spans[1].Marked);
            RichSpan broken = spans.Find(s => s.Broken);
            Assert.AreEqual("shove", broken.Text);
            Assert.IsNull(broken.Term);
            Assert.AreEqual("It may turn to stone, or knockback foes, or shove.", glossary.Plain("It may [[turn to stone|Stun]], or [[knockback]] foes, or [[shove|Nope]]."));
            Assert.AreEqual("Open [[ and never closed stun", glossary.Plain("Open [[ and never closed stun"));
            Assert.AreEqual("Open [[ and never closed {stun=stun}", Describe(glossary.Parse("Open [[ and never closed stun")));
            Assert.IsNull(glossary.Find("STUN"), "names are case-sensitive");
            Assert.AreSame(glossary.Find("stun"), glossary.Find("Stun"));
        }

        [Test]
        public void Matching_IsDeterministic_TiesGoToTheEarlierTerm()
        {
            GlossaryData data = new GlossaryData
            {
                SchemaVersion = 1,
                Terms = new[]
                {
                    new GlossaryTermData { TermId = "first", Term = "First", Category = "Combat", Forms = new[] { "mark" }, Definition = "x" },
                    new GlossaryTermData { TermId = "second", Term = "Second", Category = "Combat", Forms = new[] { "mark" }, Definition = "x" }
                }
            };

            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual("A {mark=first}.", Describe(Glossary.Build(data).Parse("A mark.")));
            }

            Assert.IsEmpty(Glossary.Empty.Parse(null));
            Assert.AreEqual("plain stun", Describe(Glossary.Empty.Parse("plain stun")));
        }

        // ------------------------------------------------------------------------------------------
        // Layout and hit-testing
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Layout_WrapsWords_KeepsTermsAcrossLines_AndHitTestsThem()
        {
            Glossary glossary = Small();
            List<RichSpan> spans = glossary.Parse("It may knock back every foe and stun them all for a turn.");
            Func<string, float> measure = s => 10f * s.Length;

            RichTextLayout layout = RichTextLayout.Of(spans, measure, 130f, 20f);

            Assert.Greater(layout.Lines, 2);
            Assert.AreEqual(layout.Lines * 20f, layout.Height);
            foreach (RichRun run in layout.Runs)
            {
                Assert.LessOrEqual(run.X + run.Width, 130f + 1e-3f, "'" + run.Text + "' fits");
                Assert.AreEqual(run.Line * 20f, run.Y);
            }

            List<RichRun> knock = layout.Runs is List<RichRun> list ? list.FindAll(r => r.Term != null && r.Term.TermId == "knockback") : null;
            Assert.IsNotNull(knock);
            Assert.GreaterOrEqual(knock.Count, 1);
            StringBuilder joined = new StringBuilder();
            foreach (RichRun run in knock)
            {
                joined.Append(run.Text).Append(' ');
            }

            Assert.AreEqual("knock back", joined.ToString().Trim().Replace("  ", " "), "the phrase keeps its term, whatever the break");

            RichRun stun = ((List<RichRun>)layout.Runs).Find(r => r.Term != null && r.Term.TermId == "stun");
            Assert.AreEqual("stun", layout.TermAt(stun.X + 5f, stun.Y + 5f).TermId);
            Assert.IsNull(layout.TermAt(0f, 0f), "plain text is not a term");
            Assert.IsNull(layout.TermAt(stun.X + 5f, stun.Y + 25f), "below its line");
            Assert.AreEqual(stun.X, layout.RunOf(glossary.Find("stun")).Value.X);
            Assert.IsFalse(layout.RunOf(glossary.Find("crit")).HasValue);

            RichTextLayout two = RichTextLayout.Of(spans, measure, 130f, 20f, 2);
            Assert.AreEqual(2, two.Lines, "capped");
            Assert.IsTrue(((List<RichRun>)two.Runs).TrueForAll(r => r.Line < 2));
        }

        [Test]
        public void Layout_MergesAdjacentWordsOfOneKind_IntoOneRun()
        {
            RichTextLayout layout = RichTextLayout.Of(Small().Parse("one two stun three"), s => 10f * s.Length, 1000f, 20f);

            Assert.AreEqual(3, layout.Runs.Count);
            Assert.AreEqual("one two ", layout.Runs[0].Text + " ");
            Assert.AreEqual("stun", layout.Runs[1].Text);
            Assert.AreEqual(80f, layout.Runs[1].X, "after 'one two ' (7 letters and a space)");
            Assert.AreEqual(1, layout.Lines);
            Assert.AreEqual(0, RichTextLayout.Of(new RichSpan[0], s => 1f, 100f, 20f).Lines);
        }

        // ------------------------------------------------------------------------------------------
        // The card model and the popup
        // ------------------------------------------------------------------------------------------

        [Test]
        public void SkillCard_ReadsTheSkillsData()
        {
            SkillSO ember = Content.Battle.GetSkill("ember_shot");
            SkillCard card = SkillCard.Of(ember, Content.Glossary);

            Assert.AreEqual("Ember Shot", card.Name);
            Assert.AreEqual("skill/ember_shot", card.ArtKey);
            Assert.AreEqual("Fire", card.Element);
            Assert.AreEqual("Special", card.Category);
            CollectionAssert.AreEqual(new[] { "Enemy" }, card.Targets);
            Assert.AreEqual("Range " + ember.Range, card.Range);
            StringAssert.StartsWith("Damage ", card.Power[0]);
            StringAssert.Contains("% of Special Attack", card.Power[0]);
            StringAssert.StartsWith("Burn ", card.Power[1]);
            StringAssert.Contains("stacks x3", card.Power[1]);
            StringAssert.Contains("per level", card.Scaling);
            Assert.IsTrue(((List<RichSpan>)card.Description).Exists(s => s.Term != null && s.Term.TermId == "burn"));

            SkillCard pounce = SkillCard.Of(Content.Battle.GetSkill("coup_de_grace"), Content.Glossary);
            Assert.AreEqual("Shadow Pounce", pounce.Name);
            StringAssert.Contains("worn-down", pounce.Power[0]);

            SkillCard bulwark = SkillCard.Of(Content.Battle.GetSkill("granite_bulwark"), Content.Glossary);
            CollectionAssert.AreEqual(new[] { "Ally", "Self" }, bulwark.Targets);
            Assert.IsNull(bulwark.Category, "no damage, no category");
            StringAssert.StartsWith("Shield ", bulwark.Power[0]);
            StringAssert.Contains("% of Defense", bulwark.Power[0]);

            SkillCard dive = SkillCard.Of(Content.Battle.GetSkill("storm_dive"), Content.Glossary);
            StringAssert.Contains("Once per battle", dive.Uses);
        }

        [TestCase(SkillTargetShape.Self, SkillTargetSide.Enemy, "Self")]
        [TestCase(SkillTargetShape.AllAllies, SkillTargetSide.Enemy, "Ally,Self")]
        [TestCase(SkillTargetShape.AllEnemies, SkillTargetSide.Ally, "Enemy")]
        [TestCase(SkillTargetShape.SingleTarget, SkillTargetSide.Ally, "Ally")]
        [TestCase(SkillTargetShape.AreaBurst, SkillTargetSide.Ally, "Ally,Self")]
        [TestCase(SkillTargetShape.Line, SkillTargetSide.Enemy, "Enemy")]
        public void TargetTags(SkillTargetShape shape, SkillTargetSide side, string expected)
        {
            Assert.AreEqual(expected, string.Join(",", SkillCard.TargetTags(shape, side)));
        }

        [Test]
        public void PowerLines_SayWhatEachEffectDoes()
        {
            Assert.AreEqual("Damage 32% of Special Attack, 4 hits",
                            SkillCard.PowerLine(new SkillEffect { Magnitude = 32f, HitCount = 4 }, Element.Dark, DamageCategory.Special));
            Assert.AreEqual("Poison 15 power a turn, 3 turns, stacks x3",
                            SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.DamageOverTime, Magnitude = 15f, DurationTurns = 3, MaxStacks = 3 },
                                                Element.Nature, DamageCategory.Special));
            Assert.AreEqual("Stun, 1 turn (45% chance)",
                            SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Stun, DurationTurns = 1, Chance = 45 },
                                                Element.Dark, DamageCategory.Special));
            Assert.AreEqual("Knockback 2 hexes",
                            SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = StatusType.Knockback, Magnitude = 2f, DurationTurns = 1 },
                                                Element.Earth, DamageCategory.Physical));
            Assert.AreEqual("-12% Speed, 3 turns",
                            SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.DebuffStat, AffectedStat = StatType.Speed, Magnitude = 12f, IsPercent = true, DurationTurns = 3 },
                                                Element.None, DamageCategory.Physical));
            Assert.AreEqual("+25 crit chance, 3 turns",
                            SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.BuffStat, AffectedStat = StatType.CritChance, Magnitude = 25f, DurationTurns = 3 },
                                                Element.Lightning, DamageCategory.Physical));
            Assert.AreEqual("Heal 20% of Special Attack", SkillCard.PowerLine(new SkillEffect { EffectType = SkillEffectType.Heal, Magnitude = 20f }, Element.Light, DamageCategory.Physical));
        }

        [Test]
        public void PopupNear_SitsAboveItsAnchor_ElseBelow_AlwaysOnTheCanvas()
        {
            PortraitLayout layout = new PortraitLayout();

            Rect above = layout.PopupNear(new Rect(500f, 1000f, 80f, 30f), 600f, 200f);
            Assert.AreEqual(1000f - 12f - 200f, above.Y);
            Assert.AreEqual(540f, above.Center.X, 1e-3f);

            Rect below = layout.PopupNear(new Rect(500f, 100f, 80f, 30f), 600f, 200f);
            Assert.AreEqual(142f, below.Y, "no room above the header: below it");

            Rect edge = layout.PopupNear(new Rect(1040f, 1000f, 30f, 30f), 600f, 200f);
            Assert.AreEqual(PortraitLayout.CanvasWidth - PortraitLayout.Margin, edge.Right, 1e-3f);
            Rect wide = layout.PopupNear(new Rect(0f, 1000f, 30f, 30f), 5000f, 200f);
            Assert.AreEqual(PortraitLayout.Margin, wide.X);
            Assert.AreEqual(PortraitLayout.CanvasWidth - 2f * PortraitLayout.Margin, wide.Width);
        }

        [Test]
        public void TheDetailCard_FitsOverTheBoard_ItsPartsInside()
        {
            PortraitLayout layout = new PortraitLayout();
            Rect card = layout.SkillDetail;

            Assert.GreaterOrEqual(card.Y, layout.Board.Y);
            Assert.LessOrEqual(card.Bottom, layout.Board.Bottom);
            foreach (Rect part in new[] { layout.SkillDetailIcon, layout.SkillDetailDiagram, layout.SkillDetailText })
            {
                Assert.GreaterOrEqual(part.X, card.X);
                Assert.GreaterOrEqual(part.Y, card.Y);
                Assert.LessOrEqual(part.Right, card.Right);
                Assert.LessOrEqual(part.Bottom, card.Bottom);
            }

            Assert.LessOrEqual(layout.SkillDetailDiagram.Right, layout.SkillDetailText.X, "diagram and text side by side");
            Assert.GreaterOrEqual(layout.SkillDetailText.Width, 400f, "room for a readable line");
        }
    }
}
