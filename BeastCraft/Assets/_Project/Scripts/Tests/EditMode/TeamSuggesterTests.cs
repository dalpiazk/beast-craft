using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Battle.Scouting;
using BeastCraft.Bonds;
using BeastCraft.Creatures;
using BeastCraft.Save;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The pre-fight team suggestion: the element score (the simulator's worked example), the counter
    /// pick, the Vanguard minimum and its fallback, deterministic tie-breaks, bond weights (tiered,
    /// afflict-only, scaling), duplicate species and the level term, small collections, the combination
    /// order, <see cref="TeamSuggester.CanAfflict"/>, and when a suggestion is shown
    /// (<see cref="TeamSuggestionPolicy"/>).
    /// </summary>
    public class TeamSuggesterTests
    {
        private readonly List<ScriptableObject> _created = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
            {
                Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();
        }

        // ---------------------------------------------------------------------------------------
        // Scoring.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void ScoreBeast_MatchesTheWorkedExample()
        {
            // Tooling/BalanceSim README, "Worked example of the heuristic": Fire x1, Water x3, Metal x2.
            EncounterPreview preview = Preview(Enemy("Giant", Element.Fire), Enemy("Archer", Element.Water), Enemy("Archer", Element.Water),
                                               Enemy("Archer", Element.Water), Enemy("Brute", Element.Metal), Enemy("Brute", Element.Metal));

            Assert.AreEqual(6.25 / 6.0, TeamSuggester.ScoreBeast(preview, Species("leviathan", CombatStance.Vanguard, Element.Water)), 1e-9);
            Assert.AreEqual(0.5 / 6.0, TeamSuggester.ScoreBeast(preview, Species("tarasque", CombatStance.Vanguard, Element.Metal)), 1e-9);
            Assert.AreEqual(0.0, TeamSuggester.ScoreBeast(Preview(), Species("leviathan", CombatStance.Vanguard, Element.Water)), "an empty preview scores nothing");
        }

        [Test]
        public void Suggest_CounterPicksTheEncounterElement()
        {
            List<TeamSuggestionCandidate> owned = FireCounterRoster();

            TeamSuggestion suggestion = TeamSuggester.Suggest(Request(FireGiants(), owned, 3));

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, suggestion.Members, "water 1.75, air 1.5 and earth 0.75 beat the rest against Fire");
            Assert.AreEqual(1.75 + 1.5 + 0.75, suggestion.Score, 1e-9);
            Assert.IsTrue(suggestion.MeetsVanguardMin);
            Assert.AreSame(owned[1].Species, suggestion.Species[1]);
        }

        [Test]
        public void Suggest_MeetsTheVanguardMinimum_TiesGoToTheEarlierCandidate()
        {
            // Only nature and ice (both scoring 0) are Vanguards: one must come in for earth.
            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>
            {
                Owned("water", CombatStance.Ranged, Element.Water),
                Owned("air", CombatStance.Skirmisher, Element.Air),
                Owned("earth", CombatStance.Ranged, Element.Earth),
                Owned("nature", CombatStance.Vanguard, Element.Nature),
                Owned("ice", CombatStance.Vanguard, Element.Ice)
            };

            TeamSuggestion suggestion = TeamSuggester.Suggest(Request(FireGiants(), owned, 3));

            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, suggestion.Members, "the earlier of the two tied Vanguards");
            Assert.IsTrue(suggestion.MeetsVanguardMin);
        }

        [Test]
        public void Suggest_WithTooFewVanguards_FieldsTheBestTeamAndSaysSo()
        {
            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate>
            {
                Owned("water", CombatStance.Ranged, Element.Water),
                Owned("air", CombatStance.Skirmisher, Element.Air),
                Owned("metal", CombatStance.Ranged, Element.Metal),
                Owned("earth", CombatStance.Ranged, Element.Earth)
            };

            TeamSuggestion suggestion = TeamSuggester.Suggest(Request(FireGiants(), owned, 3));

            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, suggestion.Members);
            Assert.IsFalse(suggestion.MeetsVanguardMin);
        }

        [Test]
        public void Suggest_IsDeterministic()
        {
            List<TeamSuggestionCandidate> owned = FireCounterRoster();
            TeamSuggestionRequest request = Request(Preview(Enemy("Archer", Element.None), Enemy("Brute", Element.Dark)), owned, 3);

            TeamSuggestion first = TeamSuggester.Suggest(request);
            TeamSuggestion second = TeamSuggester.Suggest(request);

            CollectionAssert.AreEqual(first.Members, second.Members);
            Assert.AreEqual(first.Score, second.Score);
        }

        // ---------------------------------------------------------------------------------------
        // Bonds.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Suggest_ATieredBondTipsAnOtherwiseEvenChoice()
        {
            // Against elementless enemies every beast scores the same, so the bond decides.
            List<TeamSuggestionCandidate> owned = EvenRoster();
            TeamBondSO duo = SpeciesBond("duo_test", "d", "e");
            TeamSuggestionRequest request = Request(Preview(Enemy("Brute", Element.None)), owned, 3);

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, TeamSuggester.Suggest(request).Members, "no bonds: the first combination");

            request.Bonds = new[] { duo };
            TeamSuggestion suggestion = TeamSuggester.Suggest(request);

            CollectionAssert.AreEqual(new[] { 0, 3, 4 }, suggestion.Members, "the bond's pair joins the earliest Vanguard");
            Assert.AreEqual((3 * 0.5) + TeamSuggester.BondWeight, suggestion.Score, 1e-9, "a bond missing from BondWeights weighs BondWeight per tier");
            Assert.AreEqual(1, suggestion.Bonds.Count);
            Assert.AreSame(duo, suggestion.Bonds[0].Bond);
        }

        [Test]
        public void Suggest_AnAfflictOnlyBondCountsOnlyWhenTheEncounterCanAfflict()
        {
            List<TeamSuggestionCandidate> owned = EvenRoster();
            TeamBondSO medics = SpeciesBond("medics_test", "d", "e");
            medics.Tiers[0].Reaction = new BondReaction { Trigger = BondTrigger.AllyTurnStartAfflicted };
            TeamSuggestionRequest request = Request(Preview(Enemy("Brute", Element.None)), owned, 3);
            request.Bonds = new[] { medics };

            request.EncounterCanAfflict = false;
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, TeamSuggester.Suggest(request).Members, "nothing to cleanse: the bond is worth nothing");

            request.EncounterCanAfflict = true;
            CollectionAssert.AreEqual(new[] { 0, 3, 4 }, TeamSuggester.Suggest(request).Members);
        }

        [Test]
        public void BondScore_ByKind()
        {
            TeamBondSO tiered = SpeciesBond("winter_grove", "a", "b");
            tiered.Tiers.Add(new TeamBondTier { MinCount = 3 });
            TeamBondSO scaling = SpeciesBond("scaling_test", "a", "b");
            scaling.PerCount = true;
            scaling.MaxCount = 3;
            scaling.Scope = TeamBondScope.Others;

            Assert.AreEqual(TeamSuggester.BondWeights["winter_grove"] * 2, TeamSuggester.BondScore(new ActiveTeamBond(tiered, 2, 3, new[] { 0, 1, 2 }), 4, true), 1e-12,
                            "a listed bond weighs its BondWeights entry per tier");
            Assert.AreEqual(TeamSuggester.ScalingBondWeight * 2, TeamSuggester.BondScore(new ActiveTeamBond(scaling, 1, 2, new[] { 0, 1 }, 2), 4, true), 1e-12,
                            "a scaling bond weighs ScalingBondWeight per stack");
            Assert.AreEqual(0.0, TeamSuggester.BondScore(new ActiveTeamBond(scaling, 1, 4, new[] { 0, 1, 2, 3 }, 3), 4, true),
                            "an Others bond whose members are the whole team has no recipient");
            Assert.AreEqual(0.0, TeamSuggester.BondScore(null, 4, true));
        }

        // ---------------------------------------------------------------------------------------
        // The collection.
        // ---------------------------------------------------------------------------------------

        [Test]
        public void Suggest_ASpeciesOwnedTwice_CountsOnceAsItsHighestLevelledCopy()
        {
            List<TeamSuggestionCandidate> owned = FireCounterRoster();
            for (int i = 1; i < owned.Count; i++)
            {
                owned[i] = new TeamSuggestionCandidate(owned[i].Species, 30);
            }

            owned.Add(new TeamSuggestionCandidate(owned[0].Species, 30));

            TeamSuggestion suggestion = TeamSuggester.Suggest(Request(FireGiants(), owned, 3));

            CollectionAssert.AreEqual(new[] { 1, 2, owned.Count - 1 }, suggestion.Members, "the level-30 water, not the level-10 one; ascending indices");
            Assert.AreEqual(1.75 + 1.5 + 0.75, suggestion.Score, 1e-9, "every counted copy is level 30: no level term");
            Assert.AreEqual(3, new HashSet<CreatureSpeciesSO>(suggestion.Species).Count);
        }

        [Test]
        public void Suggest_AnUnderLevelledBeastLosesLevelWeightPerLevel()
        {
            List<TeamSuggestionCandidate> owned = FireCounterRoster();
            owned[1] = new TeamSuggestionCandidate(owned[1].Species, 1);

            TeamSuggestion suggestion = TeamSuggester.Suggest(Request(FireGiants(), owned, 3));

            // Air: 1.5 - 9 levels x 0.1 = 0.6, still ahead of nature and ice (0).
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, suggestion.Members);
            Assert.AreEqual(1.75 + (1.5 - (9 * TeamSuggester.LevelWeight)) + 0.75, suggestion.Score, 1e-9);

            owned[1] = new TeamSuggestionCandidate(owned[1].Species, 10);
            owned[2] = new TeamSuggestionCandidate(owned[2].Species, 1);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, TeamSuggester.Suggest(Request(FireGiants(), owned, 3)).Members,
                                      "nine levels under the rest, earth (0.75 - 0.9) drops below nature (0)");
        }

        [Test]
        public void Suggest_SmallOrEmptyCollections()
        {
            List<TeamSuggestionCandidate> owned = new List<TeamSuggestionCandidate> { null, Owned("water", CombatStance.Vanguard, Element.Water), new TeamSuggestionCandidate(null, 5) };

            CollectionAssert.AreEqual(new[] { 1 }, TeamSuggester.Suggest(Request(FireGiants(), owned, 4)).Members, "fewer species than the team size: all of them");
            Assert.IsEmpty(TeamSuggester.Suggest(Request(FireGiants(), new List<TeamSuggestionCandidate>(), 4)).Members);
            Assert.IsEmpty(TeamSuggester.Suggest(Request(FireGiants(), null, 4)).Members);
        }

        [Test]
        public void Combinations_AreLexicographic()
        {
            List<int[]> all = TeamSuggester.Combinations(5, 3);

            Assert.AreEqual(10, all.Count);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, all[0]);
            CollectionAssert.AreEqual(new[] { 0, 1, 3 }, all[1]);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, all[9]);
            Assert.AreEqual(210, TeamSuggester.Combinations(10, 4).Count);
            Assert.IsEmpty(TeamSuggester.Combinations(2, 3));
        }

        [Test]
        public void CanAfflict_OnlyEnemySideStunsAndDamageOverTime()
        {
            SkillSO stun = Skill(SkillTargetSide.Enemy, StatusType.Stun);
            SkillSO burn = Skill(SkillTargetSide.Enemy, StatusType.DamageOverTime);
            SkillSO selfBuff = Skill(SkillTargetSide.Ally, StatusType.Stun);
            SkillSO plain = Skill(SkillTargetSide.Enemy, null);

            Assert.IsTrue(TeamSuggester.CanAfflict(new[] { plain, stun }));
            Assert.IsTrue(TeamSuggester.CanAfflict(new[] { burn }));
            Assert.IsFalse(TeamSuggester.CanAfflict(new[] { plain, selfBuff, null }));
            Assert.IsFalse(TeamSuggester.CanAfflict(null));
        }

        // ---------------------------------------------------------------------------------------
        // Policy.
        // ---------------------------------------------------------------------------------------

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        public void Policy_SuggestsFromTheThirdLoss(int losses, bool expected)
        {
            Assert.AreEqual(3, TeamSuggestionPolicy.MinLossesBeforeSuggestion);
            Assert.AreEqual(expected, TeamSuggestionPolicy.ShouldSuggest(losses, new PlayerSettings()));
            Assert.AreEqual(expected, TeamSuggestionPolicy.ShouldSuggest(losses, null), "null settings are the defaults");
        }

        [TestCase(0)]
        [TestCase(3)]
        [TestCase(50)]
        public void Policy_SuggestionsOff_NeverSuggests(int losses)
        {
            Assert.IsFalse(TeamSuggestionPolicy.ShouldSuggest(losses, new PlayerSettings { TeamSuggestionsEnabled = false }));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------------------

        private sealed class PreviewEnemy : IEncounterPreviewSource
        {
            public PreviewEnemy(string name, Element element)
            {
                DisplayName = name;
                Element = element;
            }

            public Element Element { get; }

            public CombatStance Stance
            {
                get { return CombatStance.Vanguard; }
            }

            public string DisplayName { get; }
        }

        private static IEncounterPreviewSource Enemy(string name, Element element)
        {
            return new PreviewEnemy(name, element);
        }

        private static EncounterPreview Preview(params IEncounterPreviewSource[] enemies)
        {
            return EncounterPreview.Build(enemies, ArenaSize.Medium);
        }

        private static EncounterPreview FireGiants()
        {
            return Preview(Enemy("Giant", Element.Fire), Enemy("Giant", Element.Fire), Enemy("Giant", Element.Fire));
        }

        private static TeamSuggestionRequest Request(EncounterPreview preview, List<TeamSuggestionCandidate> owned, int teamSize)
        {
            return new TeamSuggestionRequest { Preview = preview, Owned = owned, TeamSize = teamSize, MinVanguards = 1 };
        }

        /// <summary>
        /// Against Fire: water 1.75 (Vanguard), air 1.5, earth 0.75 (Vanguard), nature 0, ice 0 (Vanguard),
        /// metal -0.5; all level 10.
        /// </summary>
        private List<TeamSuggestionCandidate> FireCounterRoster()
        {
            return new List<TeamSuggestionCandidate>
            {
                Owned("water", CombatStance.Vanguard, Element.Water),
                Owned("air", CombatStance.Skirmisher, Element.Air),
                Owned("earth", CombatStance.Vanguard, Element.Earth),
                Owned("nature", CombatStance.Ranged, Element.Nature),
                Owned("ice", CombatStance.Vanguard, Element.Ice),
                Owned("metal", CombatStance.Ranged, Element.Metal)
            };
        }

        /// <summary>Five elementless beasts a-e, all level 10; a is the only Vanguard, so every feasible team fields it.</summary>
        private List<TeamSuggestionCandidate> EvenRoster()
        {
            return new List<TeamSuggestionCandidate>
            {
                Owned("a", CombatStance.Vanguard, Element.None),
                Owned("b", CombatStance.Ranged, Element.None),
                Owned("c", CombatStance.Ranged, Element.None),
                Owned("d", CombatStance.Skirmisher, Element.None),
                Owned("e", CombatStance.Ranged, Element.None)
            };
        }

        private TeamSuggestionCandidate Owned(string id, CombatStance stance, Element element, int level = 10)
        {
            return new TeamSuggestionCandidate(Species(id, stance, element), level);
        }

        private CreatureSpeciesSO Species(string id, CombatStance stance, Element element)
        {
            CreatureSpeciesSO species = ScriptableObject.CreateInstance<CreatureSpeciesSO>();
            _created.Add(species);
            species.SpeciesId = id;
            species.Stance = stance;
            species.Elements = element == Element.None ? new Element[0] : new[] { element };
            return species;
        }

        private TeamBondSO SpeciesBond(string id, params string[] species)
        {
            TeamBondSO bond = ScriptableObject.CreateInstance<TeamBondSO>();
            _created.Add(bond);
            bond.BondId = id;
            bond.Condition = TeamBondCondition.Species;
            bond.SpeciesIds = new List<string>(species);
            bond.Tiers = new List<TeamBondTier> { new TeamBondTier { MinCount = 2 } };
            return bond;
        }

        private SkillSO Skill(SkillTargetSide side, StatusType? status)
        {
            SkillSO skill = ScriptableObject.CreateInstance<SkillSO>();
            _created.Add(skill);
            skill.TargetSide = side;
            skill.Effects = new List<SkillEffect> { new SkillEffect { EffectType = SkillEffectType.Damage, Magnitude = 50 } };
            if (status.HasValue)
            {
                skill.Effects.Add(new SkillEffect { EffectType = SkillEffectType.ApplyStatus, Status = status.Value, DurationTurns = 1 });
            }

            return skill;
        }
    }
}
