using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Session;
using BeastCraft.Vfx;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The VFX library's schema v2: layered effects composed on a timeline (<see cref="VfxTimeline"/>),
    /// an area sized from the affected hexes (<see cref="VfxArea"/>), per-effect-type defaults and
    /// the resolution order, on-apply overlays, and status auras whose lifetime follows the
    /// statuses in the playback snapshots — all deterministic and engine-free.
    /// </summary>
    public class VfxLayerTests
    {
        private static readonly HexLayout Layout = new HexLayout(0, 0);

        // ------------------------------------------------------------------------------------------
        // Timeline composition
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Layers_RunOnTheirOwnClocks_FromTheHitStopsRelease()
        {
            VfxEffectData effect = new VfxEffectData
            {
                Motion = VfxMotion.Projectile,
                TravelMs = 200,
                HitStopMs = 50,
                Layers = new[]
                {
                    new VfxLayerData { Type = VfxLayerType.Glyphs, Anchor = VfxAnchor.Caster, StartMs = -200, DurationMs = 300, Sheet = "g", Count = 3, StartRadius = 0.5f },
                    new VfxLayerData { Type = VfxLayerType.GroundDecal, StartMs = 0, DurationMs = 500, Sheet = "d", EndRadius = 1f },
                    new VfxLayerData { Type = VfxLayerType.Shockwave, StartMs = 100, DurationMs = 300, Sheet = "r", Blend = VfxBlend.Additive, StartRadius = 0.25f, EndRadius = 2f }
                }
            };
            Vec2 caster = new Vec2(0f, 100f);
            VfxTimeline timeline = new VfxTimeline(effect, caster, new[] { new VfxTarget("t", new Vec2(0f, 0f), 10, false) }, 7);

            Assert.AreEqual(250, timeline.HitStopEndMs);
            Assert.AreEqual(750, timeline.DurationMs, "the decal, released at 250, lasts 500");
            Assert.AreEqual(50, timeline.LayerStart(effect.Layers[0]), "a negative start charges up before the impact");

            List<VfxSprite> charging = timeline.Sample(100).Sprites;
            Assert.AreEqual(3, charging.Count, "three glyphs round the caster while the projectile flies");
            foreach (VfxSprite glyph in charging)
            {
                Assert.AreEqual("g", glyph.Sheet);
                Assert.AreEqual(100f, glyph.Position.Y, 16f);
            }

            List<VfxSprite> decal = timeline.Sample(300).Sprites.FindAll(s => s.Sheet == "d");
            Assert.AreEqual(1, decal.Count);
            Assert.IsTrue(decal[0].Ground, "a decal lies under the units");
            Assert.AreEqual(2f * HexLayout.ColumnStep, decal[0].SizePx, 1e-3f, "sized to its radius: one hex");
            Assert.IsEmpty(timeline.Sample(300).Sprites.FindAll(s => s.Sheet == "r"), "the ring starts 100 ms after the release");

            VfxSprite early = timeline.Sample(360).Sprites.Find(s => s.Sheet == "r");
            VfxSprite late = timeline.Sample(600).Sprites.Find(s => s.Sheet == "r");
            Assert.IsTrue(early.Additive);
            Assert.IsFalse(early.Ground);
            Assert.Less(early.SizePx, late.SizePx, "the ring grows");
            Assert.Greater(early.Alpha, late.Alpha, "and fades");
            Assert.LessOrEqual(late.SizePx, 2f * 2f * HexLayout.ColumnStep + 1e-3f);

            Assert.IsEmpty(timeline.Sample(760).Sprites);
            Assert.IsTrue(timeline.Sample(760).Finished);
        }

        [Test]
        public void Layers_AreDeterministic_AndBurstsDependOnTheSeed()
        {
            VfxEffectData effect = new VfxEffectData
            {
                Layers = new[]
                {
                    new VfxLayerData { Type = VfxLayerType.RadialBurst, DurationMs = 300, Sheet = "ray", Count = 5, StartRadius = 0.2f, EndRadius = 1f },
                    new VfxLayerData
                    {
                        Type = VfxLayerType.Particles, DurationMs = 400,
                        Particles = new VfxParticleData { Sheet = "p", Count = 8, SpeedMin = 10f, SpeedMax = 60f, LifetimeMs = 300, Colors = new[] { "a" } }
                    }
                }
            };
            VfxTarget[] targets = { new VfxTarget("t", new Vec2(10f, 20f), 0, false) };

            string a = Trace(new VfxTimeline(effect, Vec2.Zero, targets, 3));
            string b = Trace(new VfxTimeline(effect, Vec2.Zero, targets, 3));
            string c = Trace(new VfxTimeline(effect, Vec2.Zero, targets, 4));

            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreEqual(5 + 8, new VfxTimeline(effect, Vec2.Zero, targets, 3).Sample(10).Sprites.Count);
        }

        [Test]
        public void Anchors_TargetDrawsOnEach_AreaDrawsOnceAtTheCentre()
        {
            VfxEffectData effect = new VfxEffectData
            {
                Layers = new[]
                {
                    new VfxLayerData { Type = VfxLayerType.Flipbook, Anchor = VfxAnchor.Target, DurationMs = 200, Sheet = "f", Frames = 4, Fps = 20 },
                    new VfxLayerData { Type = VfxLayerType.Flipbook, Anchor = VfxAnchor.Area, DurationMs = 200, Sheet = "a", Frames = 1, Fps = 1 }
                }
            };
            VfxTarget[] targets = { new VfxTarget("x", new Vec2(-32f, 0f), 0, false), new VfxTarget("y", new Vec2(32f, 0f), 0, false) };
            VfxFrame frame = new VfxTimeline(effect, Vec2.Zero, targets, 1).Sample(120);

            Assert.AreEqual(2, frame.Sprites.FindAll(s => s.Sheet == "f").Count);
            Assert.AreEqual(2, frame.Sprites.Find(s => s.Sheet == "f").Frame, "120 ms at 20 fps");
            List<VfxSprite> area = frame.Sprites.FindAll(s => s.Sheet == "a");
            Assert.AreEqual(1, area.Count);
            Assert.AreEqual(0f, area[0].Position.X, 1e-3f);
        }

        // ------------------------------------------------------------------------------------------
        // The affected area
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Area_IsTheCircleAroundTheAffectedHexes()
        {
            VfxArea one = VfxArea.OfTiles(new[] { new HexCoordinate(2, -1) }, Layout);
            Assert.AreEqual(Layout.Center(new HexCoordinate(2, -1)).X, one.Center.X, 1e-3f);
            Assert.AreEqual(0.5f, one.RadiusHexes, 1e-3f, "one tile: half a hex");

            List<HexCoordinate> ring = new List<HexCoordinate> { new HexCoordinate(0, 0) };
            ring.AddRange(HexCoordinate.AxialDirections);
            VfxArea burst = VfxArea.OfTiles(ring, Layout);
            Assert.AreEqual(0f, burst.Center.X, 1e-3f);
            Assert.AreEqual(0f, burst.Center.Y, 1e-3f);
            Assert.AreEqual(1.5f, burst.RadiusHexes, 0.05f, "a radius-1 burst: one hex out plus half a hex");

            VfxArea duplicates = VfxArea.OfTiles(new[] { new HexCoordinate(0, 0), new HexCoordinate(0, 0) }, Layout);
            Assert.AreEqual(0.5f, duplicates.RadiusHexes, 1e-3f);
        }

        [Test]
        public void Shockwave_GrowsToTheAreasRadius_WhenItsEndRadiusIsZero()
        {
            List<HexCoordinate> ring = new List<HexCoordinate> { new HexCoordinate(0, 0) };
            ring.AddRange(HexCoordinate.AxialDirections);
            VfxArea area = VfxArea.OfTiles(ring, Layout);
            VfxEffectData effect = new VfxEffectData
            {
                Layers = new[] { new VfxLayerData { Type = VfxLayerType.Shockwave, Anchor = VfxAnchor.Area, DurationMs = 400, Sheet = "r", EndRadius = 0f } }
            };
            VfxTimeline timeline = new VfxTimeline(effect, Vec2.Zero, new[] { new VfxTarget("t", Vec2.Zero, 0, false) }, 1, area);

            Assert.AreEqual(area.Radius, timeline.RadiusPx(effect.Layers[0], 1f), 1e-3f);
            VfxSprite last = timeline.Sample(399).Sprites[0];
            Assert.AreEqual(2f * area.Radius, last.SizePx, 1f);
        }

        [Test]
        public void TurnAnimation_SizesEachBeatsArea_FromWhereItsTargetsStand()
        {
            foreach (TurnAnimation animation in DemoTurns())
            {
                foreach (ScheduledBeat beat in animation.Beats)
                {
                    VfxArea area = beat.Timeline.Area;
                    foreach (BeatTarget target in beat.Beat.Targets)
                    {
                        UnitSnapshot unit = animation.Turn.After[target.UnitId];
                        foreach (HexCoordinate tile in Footprints.Tiles(unit.Position, unit.Footprint))
                        {
                            Vec2 center = Layout.Center(tile);
                            float dx = center.X - area.Center.X;
                            float dy = center.Y - area.Center.Y;
                            Assert.LessOrEqual(Math.Sqrt(dx * dx + dy * dy), area.Radius + 1e-3f, beat.Beat.SkillId);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // Resolution order and effect-type keys
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Resolve_SkillOverride_ThenEffectTypeDefault_ThenElementDefault()
        {
            VfxEffectData fire = new VfxEffectData();
            VfxEffectData none = new VfxEffectData();
            VfxEffectData heal = new VfxEffectData();
            VfxEffectData own = new VfxEffectData();
            VfxLibrary library = VfxLibrary.Build(new VfxLibraryData
            {
                SchemaVersion = 2,
                ElementDefaults = new[]
                {
                    new VfxElementDefaultData { Element = "Fire", Effect = fire },
                    new VfxElementDefaultData { Element = "None", Effect = none }
                },
                EffectDefaults = new[]
                {
                    new VfxEffectTypeDefaultData { Key = VfxEffectKey.Heal, Effect = heal },
                    new VfxEffectTypeDefaultData { Key = VfxEffectKey.Taunt, Aura = new VfxAuraData { Icon = "i" } }
                },
                Skills = new[] { new VfxSkillEffectData { SkillId = "mine", Effect = own } }
            });

            Assert.AreSame(own, library.Resolve("mine", Element.Fire, VfxEffectKey.Heal), "the skill's own entry wins");
            Assert.AreSame(heal, library.Resolve("other", Element.Fire, VfxEffectKey.Heal), "then its effect type's default");
            Assert.AreSame(fire, library.Resolve("other", Element.Fire, null), "a damage skill looks like its element");
            Assert.AreSame(fire, library.Resolve("other", Element.Fire, VfxEffectKey.Taunt), "a type with only an aura falls through to the element");
            Assert.AreSame(none, library.Resolve("other", Element.Water, VfxEffectKey.Stun));
            Assert.AreEqual("i", library.Aura(VfxEffectKey.Taunt).Icon);
            Assert.IsNull(library.Aura(VfxEffectKey.Heal));
        }

        [TestCase("sacred_spring", VfxEffectKey.Heal)]
        [TestCase("stone_challenge", VfxEffectKey.Taunt)]
        [TestCase("granite_bulwark", VfxEffectKey.Shield)]
        [TestCase("blessing", VfxEffectKey.BuffStat)]
        [TestCase("spore_cloud", VfxEffectKey.Poison)]
        [TestCase("ember_shot", null)]
        [TestCase("deep_freeze", null)]
        public void PrimaryKey_IsTheFirstEffectsType_UnlessTheSkillDealsDamage(string skillId, string key)
        {
            Assert.AreEqual(key, VfxLibrary.PrimaryKey(VfxLibraryTests.Content.Battle.GetSkill(skillId)));
        }

        [Test]
        public void DamageOverTime_IsABurnFromFire_AndAPoisonOtherwise()
        {
            Assert.AreEqual(VfxEffectKey.Burn, VfxLibrary.KeyOf(StatusType.DamageOverTime, Element.Fire));
            Assert.AreEqual(VfxEffectKey.Poison, VfxLibrary.KeyOf(StatusType.DamageOverTime, Element.Nature));
            Assert.AreEqual(VfxEffectKey.Stun, VfxLibrary.KeyOf(StatusType.Stun, Element.Ice));
            Assert.IsNull(VfxLibrary.KeyOf(StatusType.None, Element.Fire));
        }

        [Test]
        public void ShippedLibrary_HasEveryEffectTypeDefault_AndAnAuraForEveryLastingOne()
        {
            VfxLibrary vfx = VfxLibraryTests.Content.Vfx;
            foreach (string key in VfxEffectKey.All)
            {
                Assert.IsNotNull(vfx.OnApply(key), key);
            }

            foreach (string key in VfxEffectKey.Lasting)
            {
                Assert.IsNotNull(vfx.Aura(key), key);
                Assert.IsFalse(string.IsNullOrEmpty(vfx.Aura(key).Icon), key + " has a status icon");
            }
        }

        [TestCase("ember_shot", 6)]
        [TestCase("flame_wave", 6)]
        [TestCase("rebirth_flame", 4)]
        public void PhoenixShowcase_StacksLayerTypes(string skillId, int types)
        {
            VfxEffectData effect = VfxLibraryTests.Content.Vfx.Resolve(skillId, Element.Fire);
            HashSet<string> kinds = new HashSet<string>();
            foreach (VfxLayerData layer in effect.Layers)
            {
                kinds.Add(layer.Type);
            }

            Assert.GreaterOrEqual(kinds.Count, types, skillId + ": " + string.Join(", ", kinds));
            Assert.IsTrue(Array.Exists(effect.Layers, l => l.Blend == VfxBlend.Additive));
            Assert.IsTrue(Array.Exists(effect.Layers, l => l.Blend == VfxBlend.Alpha || l.Type == VfxLayerType.Glyphs));
        }

        // ------------------------------------------------------------------------------------------
        // Overlays and auras in the demo battle
        // ------------------------------------------------------------------------------------------

        [Test]
        public void DemoBattle_ShowsStunShieldBurnHealAndTaunt()
        {
            HashSet<string> shown = new HashSet<string>();
            foreach (TurnAnimation animation in DemoTurns())
            {
                foreach (ScheduledBeat beat in animation.Beats)
                {
                    if (beat.Beat.PrimaryKey != null)
                    {
                        shown.Add(beat.Beat.PrimaryKey);
                    }

                    foreach (BeatOverlay overlay in beat.Overlays)
                    {
                        shown.Add(overlay.Key);
                    }
                }

                foreach (UnitSnapshot unit in animation.Turn.After.Values)
                {
                    shown.UnionWith(unit.StatusKeys());
                }
            }

            foreach (string key in new[] { VfxEffectKey.Stun, VfxEffectKey.Shield, VfxEffectKey.Burn, VfxEffectKey.Heal, VfxEffectKey.Taunt })
            {
                Assert.IsTrue(shown.Contains(key), key + " never shows; shown: " + string.Join(", ", shown));
            }
        }

        [Test]
        public void LandedStatuses_PlayTheirOnApplyOverlay_FromTheImpact()
        {
            bool burn = false;
            bool stun = false;
            foreach (TurnAnimation animation in DemoTurns())
            {
                foreach (ScheduledBeat beat in animation.Beats)
                {
                    foreach (BeatOverlay overlay in beat.Overlays)
                    {
                        Assert.AreEqual(beat.Timeline.ImpactMs, overlay.OffsetMs);
                        Assert.GreaterOrEqual(beat.DurationMs, overlay.OffsetMs + overlay.Timeline.DurationMs);
                        Assert.AreNotEqual(beat.Beat.PrimaryKey, overlay.Key, "the main effect already is the primary key's");
                        CollectionAssert.Contains(beat.Beat.EffectKeys, overlay.Key, "only what the skill can apply");
                        bool landed = false;
                        foreach (BeatApplied applied in beat.Beat.Applied)
                        {
                            landed |= applied.UnitId == overlay.UnitId && applied.Key == overlay.Key;
                        }

                        Assert.IsTrue(landed, "the overlay is a status that landed on that unit");
                        burn |= overlay.Key == VfxEffectKey.Burn && beat.Beat.SkillId == "ember_shot";
                        stun |= overlay.Key == VfxEffectKey.Stun;
                    }
                }
            }

            Assert.IsTrue(burn, "the ember shot's burn plays its overlay");
            Assert.IsTrue(stun, "the deep freeze's stun plays its overlay");
        }

        [Test]
        public void AuraLifetime_FollowsTheStatusDuration()
        {
            List<TurnAnimation> turns = DemoTurns();
            int checkedStatuses = 0;
            foreach (string key in new[] { VfxEffectKey.Taunt, VfxEffectKey.Stun, VfxEffectKey.Shield })
            {
                // The first turn a unit gains the status, and the turn the battle takes it away again.
                for (int i = 0; i < turns.Count; i++)
                {
                    TurnAnimation applied = turns[i];
                    foreach (UnitSnapshot unit in applied.Turn.After.Values)
                    {
                        if (!unit.StatusKeys().Contains(key) || applied.Turn.Before[unit.Id].StatusKeys().Contains(key))
                        {
                            continue;
                        }

                        checkedStatuses++;
                        int landed = FirstImpactOn(applied, unit.Id);
                        if (landed < int.MaxValue)
                        {
                            Assert.IsFalse(applied.ShownStatusKeys(unit.Id, landed - 1).Contains(key), key + " before its blow lands");
                            Assert.IsTrue(applied.ShownStatusKeys(unit.Id, landed).Contains(key), key + " from its blow");
                        }

                        for (int j = i; j < turns.Count; j++)
                        {
                            bool lasts = turns[j].Turn.After[unit.Id].StatusKeys().Contains(key);
                            Assert.AreEqual(lasts, turns[j].ShownStatusKeys(unit.Id, turns[j].DurationMs).Contains(key), key + " at the end of turn " + (j + 1));
                            if (!lasts)
                            {
                                // Gone with the status: run out on the unit's own turn, spent (a broken
                                // shield) or lifted (a taunter felled) -- whatever the battle did.
                                Assert.IsFalse(turns[j].ShownStatusKeys(unit.Id, turns[j].DurationMs + 1000).Contains(key));
                                break;
                            }
                        }

                        i = turns.Count;
                        break;
                    }
                }
            }

            Assert.GreaterOrEqual(checkedStatuses, 3);
        }

        [Test]
        public void Snapshot_StatusKeys_AreOrdered_AndIncludeStatChanges()
        {
            UnitSnapshot unit = new UnitSnapshot("u", BattleTeam.Player, new HexCoordinate(0, 0), UnitFootprint.Single, 10, 10, false,
                                                 new[] { new StatusSnapshot(StatusType.DamageOverTime, 2, 5, Element.Dark), new StatusSnapshot(StatusType.Shield, 1, 30, Element.Earth) },
                                                 new[] { new ModifierSnapshot(StatType.Attack, 5, 2), new ModifierSnapshot(StatType.Speed, -3, 1) });

            CollectionAssert.AreEqual(new[] { VfxEffectKey.Shield, VfxEffectKey.Poison, VfxEffectKey.BuffStat, VfxEffectKey.DebuffStat }, unit.StatusKeys());

            UnitSnapshot down = new UnitSnapshot("u", BattleTeam.Player, new HexCoordinate(0, 0), UnitFootprint.Single, 0, 10, true, unit.Statuses);
            Assert.IsEmpty(down.StatusKeys());
        }

        [Test]
        public void AuraSampler_Pulses_OnTheGroundOrOverTheUnit()
        {
            VfxAuraData aura = new VfxAuraData { Sheet = "ring", Scale = 1f, PulseMs = 1000, Depth = VfxDepth.Ground, Blend = VfxBlend.Additive };
            Vec2 feet = new Vec2(5f, 50f);

            Assert.IsTrue(VfxAuraSampler.TrySample(aura, feet, 1f, 250, out VfxSprite peak));
            Assert.IsTrue(VfxAuraSampler.TrySample(aura, feet, 1f, 750, out VfxSprite trough));
            Assert.Greater(peak.Scale, trough.Scale);
            Assert.Greater(peak.Alpha, trough.Alpha);
            Assert.IsTrue(peak.Ground);
            Assert.AreEqual(50f, peak.Position.Y);
            Assert.IsTrue(VfxAuraSampler.TrySample(aura, feet, 1f, 1250, out VfxSprite again));
            Assert.AreEqual(peak.Scale, again.Scale, 1e-5f, "it loops");

            aura.Depth = VfxDepth.Over;
            Assert.IsTrue(VfxAuraSampler.TrySample(aura, feet, 2f, 0, out VfxSprite over));
            Assert.IsFalse(over.Ground);
            Assert.Less(over.Position.Y, 50f);

            Assert.IsFalse(VfxAuraSampler.TrySample(new VfxAuraData { Icon = "i" }, feet, 1f, 0, out _), "an icon-only aura draws no sprite");
        }

        // ------------------------------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Validator_CoversLayersEffectDefaultsAndAuras()
        {
            VfxLibraryData data = VfxLibraryTests.Minimal();
            data.ElementDefaults[0].Effect.Layers = new[]
            {
                new VfxLayerData { Type = "Laser", DurationMs = 100 },
                new VfxLayerData { Type = VfxLayerType.Flipbook, Sheet = "burst", Frames = 9, Fps = 10, DurationMs = 100, Blend = "Screen", Anchor = "Sky" },
                new VfxLayerData { Type = VfxLayerType.Shockwave, Sheet = "nope", DurationMs = 0, StartMs = -5000, StartRadius = 20f },
                new VfxLayerData { Type = VfxLayerType.Particles, DurationMs = 100 },
                new VfxLayerData { Type = VfxLayerType.Glyphs, Sheet = "dot", DurationMs = 100, Count = 0, Spin = 9f, Depth = "Sideways" }
            };
            data.EffectDefaults = new[]
            {
                new VfxEffectTypeDefaultData { Key = "Frenzy", Effect = new VfxEffectData() },
                new VfxEffectTypeDefaultData { Key = VfxEffectKey.Heal, Aura = new VfxAuraData { Icon = "dot" } },
                new VfxEffectTypeDefaultData { Key = VfxEffectKey.Stun, Aura = new VfxAuraData { PulseMs = 10, Scale = 9f } },
                new VfxEffectTypeDefaultData { Key = VfxEffectKey.Stun, Effect = new VfxEffectData() },
                new VfxEffectTypeDefaultData { Key = VfxEffectKey.Taunt }
            };

            string all = string.Join("\n", VfxLibraryValidator.Validate(data, new[] { "zap" }, VfxLibraryTests.Art()));

            foreach (string fragment in new[]
                     {
                         "Type 'Laser'", "9 frames, but sheet 'burst' has 4", "Blend 'Screen'", "Anchor 'Sky'", "sheet 'nope'", "DurationMs 0 is outside",
                         "StartMs -5000", "StartRadius 20", "no Particles", "Count 0", "Spin 9", "Depth 'Sideways'", "'Frenzy': not an effect key",
                         "an Aura needs a lasting effect", "neither a Sheet nor an Icon", "PulseMs 10", "Scale 9", "'Stun' is listed twice",
                         "'Taunt': has neither an Effect nor an Aura"
                     })
            {
                StringAssert.Contains(fragment, all);
            }
        }

        [Test]
        public void Validator_StillReadsSchemaV1()
        {
            VfxLibraryData data = VfxLibraryTests.Minimal();
            data.SchemaVersion = 1;
            Assert.IsEmpty(VfxLibraryValidator.Validate(data, new[] { "zap" }, VfxLibraryTests.Art()));
        }

        // ------------------------------------------------------------------------------------------

        /// <summary>Every turn of the demo battle, animated.</summary>
        private static List<TurnAnimation> DemoTurns()
        {
            GameContent content = VfxLibraryTests.Content;
            BattleSetup setup = DemoBattle.Create(content, DemoBattle.DefaultSeed, out _, out string error);
            Assert.IsNotNull(setup, error);
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(setup));
            List<TurnAnimation> turns = new List<TurnAnimation>();
            PlayedTurn turn;
            while ((turn = playback.Advance()) != null)
            {
                turns.Add(new TurnAnimation(turn, Layout, content.Vfx, DemoBattle.DefaultSeed));
            }

            return turns;
        }

        private static int FirstImpactOn(TurnAnimation animation, string unitId)
        {
            foreach (ScheduledBeat beat in animation.Beats)
            {
                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    if (target.UnitId == unitId)
                    {
                        return beat.StartMs + beat.Timeline.ImpactMs;
                    }
                }
            }

            return int.MaxValue;
        }

        private static string Trace(VfxTimeline timeline)
        {
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            for (int ms = 0; ms < timeline.DurationMs; ms += 37)
            {
                foreach (VfxSprite sprite in timeline.Sample(ms).Sprites)
                {
                    text.Append(sprite.Sheet).Append(sprite.Frame).Append(sprite.Position).Append(sprite.Rotation.ToString("0.###"))
                        .Append(sprite.Alpha.ToString("0.###")).Append(';');
                }
            }

            return text.ToString();
        }
    }
}
