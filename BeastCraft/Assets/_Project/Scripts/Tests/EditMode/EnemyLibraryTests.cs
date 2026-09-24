using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using BeastCraft.Encounters;
using BeastCraft.Skills;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// <see cref="EnemyLibraryValidator"/>'s rules over small hand-built libraries, and
    /// <see cref="EnemyCatalog"/>: the enemy JSON to species/kit mapping shared by the game and the
    /// balance simulator.
    /// </summary>
    public class EnemyLibraryTests
    {
        private readonly List<ContentAsset> _created = new List<ContentAsset>();

        [TearDown]
        public void TearDown()
        {
            _created.Clear();
        }

        // ----------------------------------------------------------------------------------------
        // Validator.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Validate_AcceptsAMinimalLibrary()
        {
            List<string> errors = EnemyLibraryValidator.Validate(Library(Brute(), Archer()), BeastRosterTests.LoadRoster());

            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Validate_RefusesDuplicateAndMalformedIds()
        {
            EnemyData twin = Brute();
            EnemyData bad = Brute();
            bad.EnemyId = "Bad-Id";

            AssertRejected(Library(Brute(), twin), "duplicate EnemyId");
            AssertRejected(Library(bad), "snake_case");
        }

        [Test]
        public void Validate_RefusesAnIdThatIsARosterSpecies()
        {
            EnemyData enemy = Brute();
            enemy.EnemyId = BeastRosterTests.LoadRoster().Species[0].SpeciesId;

            AssertRejected(Library(enemy), "roster SpeciesId");
        }

        [Test]
        public void Validate_RefusesAnUnknownGrowthCurve()
        {
            EnemyLibraryData library = Library(Brute());
            library.GrowthCurveId = "no_such_curve";

            AssertRejected(library, "GrowthCurveId");
        }

        [Test]
        public void Validate_RefusesOutOfBandNumbers()
        {
            EnemyData noThreat = Brute();
            noThreat.Threat = 0.0;
            EnemyData immobile = Brute();
            immobile.BaseStats.MoveRange = 0;
            EnemyData crit = Brute();
            crit.BaseStats.CritChance = 101;
            EnemyData resist = Brute();
            resist.StatusResist = 101;

            AssertRejected(Library(noThreat), "Threat must be above 0");
            AssertRejected(Library(immobile), "MoveRange >= 1");
            AssertRejected(Library(crit), "CritChance");
            AssertRejected(Library(resist), "StatusResist");
        }

        [Test]
        public void Validate_RefusesUnparsableStanceAndFootprint()
        {
            EnemyData stance = Brute();
            stance.Stance = "vanguard";
            EnemyData footprint = Brute();
            footprint.Footprint = "Hex19";

            AssertRejected(Library(stance), "Stance 'vanguard'");
            AssertRejected(Library(footprint), "Footprint 'Hex19'");
        }

        [Test]
        public void Validate_RefusesAKitThatNeverApproaches()
        {
            EnemyData enemy = Brute();
            enemy.Skills = new[] { Skill("slam", "AreaBurst", 1, 60f) };

            AssertRejected(Library(enemy), "move the unit");
        }

        [Test]
        public void Validate_RefusesARangedEnemyWithoutReach()
        {
            EnemyData enemy = Archer();
            enemy.Skills = new[] { Skill("stab", "SingleTarget", 1, 70f) };

            AssertRejected(Library(enemy), "Ranged enemy needs");
        }

        [Test]
        public void Validate_RefusesRandomTargetingAnElementAndABadEffect()
        {
            EnemyData random = Brute();
            random.Skills[0].TargetingCriterion = "Random";
            EnemyData element = Brute();
            element.Skills[0].Element = "Fire";
            EnemyData effect = Brute();
            effect.Skills[0].Effects[0].Magnitude = 0f;

            AssertRejected(Library(random), "Random is refused");
            AssertRejected(Library(element), "Element must be empty");
            AssertRejected(Library(effect), "damage power");
        }

        // ----------------------------------------------------------------------------------------
        // Catalog.
        // ----------------------------------------------------------------------------------------

        [Test]
        public void Catalog_Species_IsOneInstancePerEnemyAndElement()
        {
            EnemyCatalog catalog = Catalog(Library(Brute(), Archer()));

            CreatureSpeciesSO fire = catalog.Species("brute", Element.Fire);
            Assert.AreSame(fire, catalog.Species("brute", Element.Fire));
            Assert.AreNotSame(fire, catalog.Species("brute", Element.Water));
            Assert.AreNotSame(fire, catalog.Species("archer", Element.Fire));
            Assert.IsNull(catalog.Species("nobody", Element.Fire));
        }

        [Test]
        public void Catalog_Species_MapsTheEnemyWithTheElement()
        {
            EnemyData data = Archer();
            data.Footprint = "Triangle";
            EnemyCatalog catalog = Catalog(Library(data));

            CreatureSpeciesSO species = catalog.Species("archer", Element.Ice);
            CreatureSpeciesSO plain = catalog.Species("archer", Element.None);

            Assert.AreEqual("archer", species.SpeciesId);
            Assert.AreEqual("archer", species.name);
            Assert.AreEqual(data.DisplayName, species.DisplayName);
            Assert.AreEqual(data.BaseStats, species.BaseStats);
            Assert.AreSame(catalog.GrowthRate, species.GrowthRate);
            Assert.AreEqual(new[] { Element.Ice }, species.Elements);
            Assert.AreEqual(0, plain.Elements.Length);
            Assert.AreEqual(CombatStance.Ranged, species.Stance);
            Assert.AreEqual(UnitFootprint.Triangle, species.Footprint);
            Assert.AreEqual(CombatStance.Ranged, catalog.StanceOf("archer"));
            Assert.AreEqual(UnitFootprint.Triangle, catalog.FootprintOf("archer"));
        }

        [Test]
        public void Catalog_Kit_IsTheAuthoredSkillsInTheUnitsElement()
        {
            EnemyData data = Brute();
            data.Skills = new[]
            {
                Skill("smash", "SingleTarget", 1, 83f),
                Skill("slam", "AreaBurst", 2, 40f)
            };
            data.Skills[0].TargetingCriterion = "CurrentHp";
            data.Skills[0].TargetingOrder = "Highest";
            data.Skills[1].Category = "Special";
            data.Skills[1].Cooldown = 3;
            data.Skills[1].Effects = new[]
            {
                new EffectData { Magnitude = 40f, HitCount = 2 },
                new EffectData { EffectType = "DebuffStat", AffectedStat = "Defense", Magnitude = 10f, DurationTurns = 2 }
            };

            EnemyCatalog catalog = Catalog(Library(data));
            IReadOnlyList<SkillSO> kit = catalog.Kit("brute", Element.Lightning);

            Assert.AreSame(kit, catalog.Kit("brute", Element.Lightning));
            Assert.AreNotSame(kit, catalog.Kit("brute", Element.None));
            Assert.AreEqual(2, kit.Count);

            for (int i = 0; i < kit.Count; i++)
            {
                SkillData authored = data.Skills[i];
                SkillSO skill = kit[i];
                Assert.AreEqual(authored.SkillId, skill.SkillId);
                Assert.AreEqual(authored.SkillId, skill.name);
                Assert.AreEqual(Element.Lightning, skill.Element);
                Assert.AreEqual(SkillLibraryValidator.ParseOr(authored.TargetShape, SkillTargetShape.SingleTarget), skill.TargetShape);
                Assert.AreEqual(authored.Range, skill.Range);
                Assert.AreEqual(authored.Cooldown, skill.Cooldown);
                Assert.AreEqual(SkillLibraryValidator.ParseOr(authored.Category, DamageCategory.Physical), skill.Category);
                Assert.AreEqual(SkillTargetSide.Enemy, skill.TargetSide);
                Assert.AreEqual(authored.Effects.Length, skill.Effects.Count);
                for (int e = 0; e < authored.Effects.Length; e++)
                {
                    Assert.AreEqual(authored.Effects[e].Magnitude, skill.Effects[e].Magnitude);
                    Assert.AreEqual(authored.Effects[e].HitCount, skill.Effects[e].HitCount);
                }
            }

            Assert.AreEqual(SkillTargetingCriterion.CurrentHp, kit[0].TargetingCriterion);
            Assert.AreEqual(SkillTargetingOrder.Highest, kit[0].TargetingOrder);
            Assert.AreEqual(SkillTargetingCriterion.Distance, kit[1].TargetingCriterion);
            Assert.AreEqual(SkillEffectType.DebuffStat, kit[1].Effects[1].EffectType);
            Assert.AreEqual(StatType.Defense, kit[1].Effects[1].AffectedStat);
            Assert.AreEqual(Element.None, catalog.Kit("brute", Element.None)[0].Element);
            Assert.IsNull(catalog.Kit("nobody", Element.None));
        }

        [Test]
        public void Catalog_FirstEntryWinsAndNullsAreSkipped()
        {
            EnemyData first = Brute();
            EnemyData second = Brute();
            second.DisplayName = "Second";
            EnemyCatalog catalog = EnemyCatalog.Build(new[] { first, null, second }, null);

            Assert.AreEqual(1, catalog.Enemies.Count);
            Assert.AreSame(first, catalog.Get("brute"));
            Assert.IsTrue(catalog.Contains("brute"));
            Assert.IsFalse(catalog.Contains(null));
        }

        // ----------------------------------------------------------------------------------------
        // Helpers (internal: the encounter tests build on them).
        // ----------------------------------------------------------------------------------------

        internal static EnemyLibraryData Library(params EnemyData[] enemies)
        {
            return new EnemyLibraryData { SchemaVersion = EnemyLibraryData.CurrentSchemaVersion, GrowthCurveId = "medium", Enemies = enemies };
        }

        internal static EnemyData Brute()
        {
            return new EnemyData
            {
                EnemyId = "brute",
                DisplayName = "Brute",
                Threat = 2.75,
                Stance = "Vanguard",
                BaseStats = new StatBlock(190, 105, 125, 40, 95, 95, 3, 4),
                Skills = new[] { Skill("smash", "SingleTarget", 1, 83f) }
            };
        }

        internal static EnemyData Archer()
        {
            return new EnemyData
            {
                EnemyId = "archer",
                DisplayName = "Archer",
                Threat = 2.0,
                Stance = "Ranged",
                BaseStats = new StatBlock(90, 115, 70, 50, 80, 100, 3, 8),
                Skills = new[] { Skill("arrow", "SingleTarget", 3, 71f) }
            };
        }

        internal static EnemyData Giant()
        {
            return new EnemyData
            {
                EnemyId = "giant",
                DisplayName = "Giant",
                Threat = 12.0,
                Stance = "Vanguard",
                Footprint = "Hex7",
                StatusResist = 50,
                BaseStats = new StatBlock(1600, 120, 110, 120, 110, 95, 3, 5),
                Skills = new[] { Skill("crush", "SingleTarget", 1, 113f) }
            };
        }

        internal static SkillData Skill(string id, string shape, int range, float power)
        {
            return new SkillData
            {
                SkillId = id,
                DisplayName = id,
                TargetShape = shape,
                Range = range,
                Cooldown = 1,
                Category = "Physical",
                Effects = new[] { new EffectData { Magnitude = power } }
            };
        }

        private EnemyCatalog Catalog(EnemyLibraryData library)
        {
            GrowthRateCurve curve = new GrowthRateCurve();
            _created.Add(curve);
            EnemyCatalog catalog = EnemyCatalog.Build(library, curve);
            foreach (EnemyData enemy in catalog.Enemies)
            {
                foreach (Element element in new[] { Element.None, Element.Fire, Element.Water, Element.Ice, Element.Lightning })
                {
                    _created.Add(catalog.Species(enemy.EnemyId, element));
                    _created.AddRange(catalog.Kit(enemy.EnemyId, element));
                }
            }

            return catalog;
        }

        private static void AssertRejected(EnemyLibraryData library, string expected)
        {
            List<string> errors = EnemyLibraryValidator.Validate(library, BeastRosterTests.LoadRoster());
            Assert.IsTrue(errors.Exists(e => e.Contains(expected)), "expected an error containing '" + expected + "', got:\n" + string.Join("\n", errors));
        }
    }
}
