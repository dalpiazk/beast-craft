using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Avatar;
using BeastCraft.Creatures;
using BeastCraft.Creatures.Roster;
using NUnit.Framework;
using UnityEngine;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Golden values for the growth-curve math every stat-at-level runs through: every curve in
    /// <c>beast-roster.json</c> sampled densely (raw curve and per level), every species' and an
    /// avatar's stats at every level, and a few hand-built curves covering the evaluator's edge
    /// cases. Captured on the Unity-era <c>AnimationCurve</c>; the engine-neutral port must match
    /// it exactly.
    /// </summary>
    public class GoldenCurveTests
    {
        private const string GoldenPath = "curves.golden.txt";

        [Test]
        public void GrowthCurves_MatchTheGoldenSamples()
        {
            BeastRosterData roster = BeastRosterTests.LoadRoster();
            StringBuilder sb = new StringBuilder();

            foreach (GrowthCurveData data in roster.GrowthCurves)
            {
                AnimationCurve curve = data.ToAnimationCurve();
                sb.Append("curve ").Append(data.CurveId).Append(" max ").Append(data.MaxLevel).Append('\n');

                for (int i = -20; i <= 220; i++)
                {
                    float progress = i / 200f;
                    sb.Append("  t ").Append(F(progress)).Append(' ').Append(F(curve.Evaluate(progress))).Append('\n');
                }

                for (int level = -1; level <= data.MaxLevel + 2; level++)
                {
                    sb.Append("  level ").Append(level).Append(' ').Append(F(GrowthRateCurve.EvaluateScale(curve, data.MaxLevel, level)))
                      .Append(' ').Append(F(BeastRosterValidator.ScaleAtLevel(data, level))).Append('\n');
                }

                GrowthRateCurve asset = new GrowthRateCurve();
                BeastRosterBuilder.ApplyCurve(data, asset);
                AvatarStatsSO avatar = new AvatarStatsSO();
                avatar.BaseStats = new StatBlock(517, 233, 181, 97, 1, 100);
                avatar.Growth = asset;

                for (int level = 0; level <= data.MaxLevel + 1; level++)
                {
                    sb.Append("  avatar ").Append(level).Append(' ').Append(F(asset.GetScaleAtLevel(level)));

                    foreach (StatType type in Enum.GetValues(typeof(StatType)))
                    {
                        sb.Append(' ').Append(avatar.GetStatAtLevel(type, level));
                    }

                    sb.Append('\n');
                }
            }

            List<CreatureSpeciesSO> species = BeastRosterBuilder.BuildAll(roster, out Dictionary<string, GrowthRateCurve> _);

            foreach (CreatureSpeciesSO beast in species)
            {
                sb.Append("species ").Append(beast.SpeciesId).Append('\n');

                for (int level = 0; level <= 101; level++)
                {
                    sb.Append("  ").Append(level);

                    foreach (StatType type in Enum.GetValues(typeof(StatType)))
                    {
                        sb.Append(' ').Append(beast.GetStatAtLevel(type, level));
                    }

                    sb.Append('\n');
                }
            }

            AppendSynthetic(sb, "empty", new AnimationCurve());
            AppendSynthetic(sb, "single", new AnimationCurve(new Keyframe(0.4f, 0.3f)));
            AppendSynthetic(sb, "linear", AnimationCurve.Linear(0f, 0.1f, 1f, 1f));
            AppendSynthetic(sb, "constant", AnimationCurve.Constant(0f, 1f, 0.55f));
            AppendSynthetic(sb, "unsorted", new AnimationCurve(new Keyframe(1f, 1f), new Keyframe(0f, 0.12f), new Keyframe(0.5f, 0.7f, 3f, -2f)));
            AppendSynthetic(sb, "duplicate-time", new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.5f, 0.4f), new Keyframe(0.5f, 0.9f), new Keyframe(1f, 1f)));
            AnimationCurve added = AnimationCurve.Linear(0.2f, 0.2f, 0.8f, 0.6f);
            added.AddKey(0.5f, 0.9f);
            added.AddKey(0f, 0.05f);
            AppendSynthetic(sb, "add-key", added);

            GoldenFiles.AssertMatches(GoldenPath, sb.ToString());
        }

        private static void AppendSynthetic(StringBuilder sb, string label, AnimationCurve curve)
        {
            sb.Append("synthetic ").Append(label).Append(" keys ").Append(curve.length).Append('\n');

            for (int i = -20; i <= 220; i += 3)
            {
                float progress = i / 200f;
                sb.Append("  t ").Append(F(progress)).Append(' ').Append(F(curve.Evaluate(progress))).Append('\n');
            }

            for (int level = 0; level <= 11; level++)
            {
                sb.Append("  level ").Append(level).Append(' ').Append(F(GrowthRateCurve.EvaluateScale(curve, 10, level))).Append('\n');
            }
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
