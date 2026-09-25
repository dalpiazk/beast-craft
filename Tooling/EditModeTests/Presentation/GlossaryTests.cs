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
        public void Popup_GoesBelowTheTermAndItsBlock_WhenThereIsRoom()
        {
            Rect bounds = new Rect(0f, 0f, 1000f, 1000f);
            Rect term = new Rect(400f, 200f, 80f, 30f);
            Rect block = new Rect(300f, 180f, 500f, 120f);

            PopupPlacement place = PopupPlacement.Place(term, block, 600f, 200f, bounds);

            Assert.IsTrue(place.Below);
            Assert.IsTrue(place.Fits);
            Assert.AreEqual(block.Bottom + PopupPlacement.DefaultGap, place.Rect.Y);
            Assert.AreEqual(term.Center.X, place.Rect.Center.X, 1e-3f, "centred on the term");
            Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, term));
            Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, block));
            Assert.AreEqual(term.Center.X, place.PointerX, 1e-3f);
        }

        [Test]
        public void Popup_FlipsAbove_WhenBelowWouldLeaveTheBounds()
        {
            Rect bounds = new Rect(0f, 0f, 1000f, 1000f);
            Rect term = new Rect(400f, 800f, 80f, 30f);
            Rect block = new Rect(300f, 760f, 500f, 120f);

            PopupPlacement place = PopupPlacement.Place(term, block, 600f, 200f, bounds);

            Assert.IsFalse(place.Below);
            Assert.IsTrue(place.Fits);
            Assert.AreEqual(block.Y - PopupPlacement.DefaultGap - 200f, place.Rect.Y);
            Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, term));
            Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, block));
        }

        [Test]
        public void Popup_IsClampedIntoTheBounds()
        {
            Rect bounds = new Rect(100f, 100f, 800f, 800f);

            PopupPlacement right = PopupPlacement.Place(new Rect(860f, 300f, 30f, 30f), new Rect(), 600f, 200f, bounds);
            Assert.AreEqual(bounds.Right, right.Rect.Right, 1e-3f, "a term at the right edge");
            Assert.LessOrEqual(right.PointerX, right.Rect.Right - PopupPlacement.PointerHalfWidth);
            Assert.AreEqual(875f, right.PointerX, 1e-3f, "the pointer still points at the term");

            PopupPlacement left = PopupPlacement.Place(new Rect(105f, 300f, 30f, 30f), new Rect(), 600f, 200f, bounds);
            Assert.AreEqual(bounds.X, left.Rect.X);
            Assert.GreaterOrEqual(left.PointerX, left.Rect.X + PopupPlacement.PointerHalfWidth);

            PopupPlacement wide = PopupPlacement.Place(new Rect(400f, 300f, 30f, 30f), new Rect(), 5000f, 200f, bounds);
            Assert.AreEqual(bounds.X, wide.Rect.X);
            Assert.AreEqual(bounds.Width, wide.Rect.Width, "shrunk to the bounds");

            // No room either side: the roomier side, clamped inside the bounds.
            Rect block = new Rect(200f, 300f, 400f, 450f);
            PopupPlacement cramped = PopupPlacement.Place(new Rect(300f, 700f, 40f, 30f), block, 400f, 300f, bounds);
            Assert.IsFalse(cramped.Fits);
            Assert.IsFalse(cramped.Below, "200 px above beats 150 below");
            Assert.AreEqual(bounds.Y, cramped.Rect.Y);
            Assert.LessOrEqual(cramped.Rect.Bottom, bounds.Bottom);
        }

        [Test]
        public void Layout_BreaksAnUnbrokenWordWiderThanTheLine()
        {
            string word = new string('W', 37);
            RichTextLayout layout = RichTextLayout.Of(new[] { new RichSpan("Before " + word + " after", null) }, s => 10f * s.Length, 100f, 20f);

            StringBuilder pieces = new StringBuilder();
            foreach (RichRun run in layout.Runs)
            {
                Assert.LessOrEqual(run.X + run.Width, 100f + 1e-3f, "'" + run.Text + "' fits its line");
                if (run.Text.Contains("W"))
                {
                    pieces.Append(run.Text.Replace(" after", string.Empty).Trim());
                }
            }

            Assert.AreEqual(word, pieces.ToString(), "the word is all there, in order, broken between characters");
            Assert.AreEqual("Before", layout.Runs[0].Text.Trim());
            Assert.AreEqual(1, layout.Runs[1].Line, "the long word starts on a fresh line");
            Assert.AreEqual(6, layout.Lines, "Before / 10 / 10 / 10 / 7 / after");
            Assert.AreEqual(1, RichTextLayout.Of(new[] { new RichSpan("W", null) }, s => 500f, 100f, 20f).Lines, "a single glyph wider than the line still gets one");
        }

        [Test]
        public void CardLayout_WrapsLongPowerLines_WithinTheCard()
        {
            SkillCard card = SkillCard.Of(Content.Battle.GetSkill("ember_shot"), Content.Glossary);
            PortraitLayout screen = new PortraitLayout();
            // A wide face, so the power lines must wrap.
            SkillCardLayout layout = SkillCardLayout.Of(card, screen.SkillDetail, (s, size) => s.Length * size * 1.3f, size => size * 1.5f);

            Assert.Greater(layout.Power.Lines, card.Power.Count, "a power line too long for the card wraps");
            StringBuilder text = new StringBuilder();
            foreach (RichRun run in layout.Power.Runs)
            {
                Assert.LessOrEqual(layout.PowerAt.X + run.X + run.Width, layout.Card.Right - SkillCardLayout.Padding + 1e-3f);
                text.Append(run.Text).Append(' ');
            }

            foreach (string line in card.Power)
            {
                foreach (string word in line.Split(' '))
                {
                    StringAssert.Contains(word, text.ToString(), "nothing is cut");
                }
            }

            Assert.LessOrEqual(layout.ScalingAt.Y + layout.Scaling.Height, layout.Diagram.Y, "power and scaling sit above the split");
            Assert.AreEqual(layout.Diagram.Y, layout.Text.Y);
            Assert.LessOrEqual(layout.Diagram.Right, layout.Text.X);
        }

        [Test]
        public void CardLayout_SitsOnTheBoardsFoot_AsTallAsItsContent()
        {
            PortraitLayout screen = new PortraitLayout();
            foreach (SkillData data in Content.SkillLibrary.BeastSkills)
            {
                SkillCard card = SkillCard.Of(Content.Battle.GetSkill(data.SkillId), Content.Glossary);
                SkillCardLayout layout = SkillCardLayout.Of(card, screen.SkillDetail, (s, size) => s.Length * size * 0.55f, size => size * 1.3f);

                Assert.AreEqual(screen.SkillDetail.Bottom, layout.Card.Bottom, 1e-3f, data.SkillId);
                Assert.GreaterOrEqual(layout.Card.Y, screen.SkillDetail.Y - 1e-3f, data.SkillId);
                Assert.GreaterOrEqual(layout.Diagram.Height, SkillCardLayout.MinSplitHeight - 1e-3f);
                Assert.LessOrEqual(layout.DescriptionBlock.Bottom, layout.Text.Bottom + 1e-3f);
                Assert.Less(layout.Card.Height, screen.SkillDetail.Height, data.SkillId + ": no taller than it needs");
            }
        }

        [Test]
        public void GlossaryPopup_NeverCoversTheTermOrTheDescription_OnAnyRealSkill()
        {
            PortraitLayout screen = new PortraitLayout();
            int below = 0;
            int above = 0;
            foreach (SkillData data in Content.SkillLibrary.BeastSkills)
            {
                SkillCard card = SkillCard.Of(Content.Battle.GetSkill(data.SkillId), Content.Glossary);
                SkillCardLayout layout = SkillCardLayout.Of(card, screen.SkillDetail, (s, size) => s.Length * size * 0.55f, size => size * 1.3f);
                foreach (GlossaryTerm term in Content.Glossary.TermsIn(data.Description))
                {
                    foreach (float height in new[] { 160f, 230f })
                    {
                        PopupPlacement place = layout.PlacePopup(term, 600f, height);
                        Rect termRect = layout.TermRect(term).Value;
                        string at = data.SkillId + " / " + term.TermId + " / " + height;

                        Assert.IsTrue(place.Fits, at);
                        Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, termRect), at + ": the term stays visible");
                        Assert.IsFalse(PopupPlacement.Overlaps(place.Rect, layout.DescriptionBlock), at + ": the description stays visible");
                        Assert.GreaterOrEqual(place.Rect.X, layout.Card.X);
                        Assert.LessOrEqual(place.Rect.Right, layout.Card.Right);
                        Assert.GreaterOrEqual(place.Rect.Y, layout.Card.Y);
                        Assert.LessOrEqual(place.Rect.Bottom, layout.Card.Bottom);
                        Assert.AreEqual(place.Below, place.Rect.Y > termRect.Y, at);
                        if (place.Below)
                        {
                            below++;
                        }
                        else
                        {
                            above++;
                        }
                    }
                }
            }

            Assert.Greater(below, 0, "short descriptions leave room below");
            Assert.Greater(above, 0, "longer ones flip above");
        }

        [Test]
        public void CardLayout_HitTestsTermsInCanvasPixels()
        {
            PortraitLayout screen = new PortraitLayout();
            SkillCard card = SkillCard.Of(Content.Battle.GetSkill("ember_shot"), Content.Glossary);
            SkillCardLayout layout = SkillCardLayout.Of(card, screen.SkillDetail, (s, size) => s.Length * size * 0.55f, size => size * 1.3f);
            GlossaryTerm burn = Content.Glossary.Find("burn");
            Rect rect = layout.TermRect(burn).Value;

            Assert.AreSame(burn, layout.TermAt(rect.Center.X, rect.Center.Y));
            Assert.IsNull(layout.TermAt(layout.Text.X + 1f, layout.Text.Y + 1f), "the first word is plain");
            Assert.IsNull(layout.TermRect(Content.Glossary.Find("stun")));
            Assert.IsTrue(layout.PlacePopup(Content.Glossary.Find("stun"), 600f, 200f).Fits, "a term not in the text anchors on the column");
        }

        [Test]
        public void TheDetailCardRoom_LiesOverTheBoard()
        {
            PortraitLayout layout = new PortraitLayout();
            Rect card = layout.SkillDetail;

            Assert.GreaterOrEqual(card.Y, layout.Board.Y);
            Assert.LessOrEqual(card.Bottom, layout.Board.Bottom);
            Assert.AreEqual(layout.Board.Width, card.Width);
        }
    }
}
