using System;
using System.Collections.Generic;
using BeastCraft.Progression;

namespace BeastCraft.Tooling.BalanceSim
{
    /// <summary>
    /// The skill levels the typical-team calibration fields (<see cref="TypicalCalibration"/>), read off the
    /// pacing model (<see cref="PacingSimulator"/>: <c>--mode pacing</c>'s campaigns, the seed and runs of the
    /// options) by encounter level. The fielded team has <see cref="TeamSkills"/> skills (three beasts, three
    /// default skills each); the model's player feeds one focus skill, spills the rest to one secondary skill,
    /// and the other seven level by practice alone, so they wait at their first gate (level 5).
    /// <list type="bullet">
    /// <item><strong>Typical</strong> (<see cref="Typical"/>): the mean of the nine, the focus and the secondary at
    /// their p50 and the seven at the practice-only level, rounded; every beast and avatar skill is fielded at it
    /// (the simulator fields one skill level per battle).</item>
    /// <item><strong>Upgraded</strong> (<see cref="Upgraded"/>): the focus skill's p50, for every skill: the player
    /// who invests in the whole kit (the spread check's strong pick).</item>
    /// </list>
    /// Level <c>L</c> is read after battle <see cref="BattleAt"/>(L), the middle of the model's five battles at L.
    /// </summary>
    public sealed class SkillCurve
    {
        /// <summary>Skills a fielded team carries: three beasts with three default skills each.</summary>
        public const int TeamSkills = 9;

        private readonly int[] _focus = new int[SimOptions.MaxLevel + 1];
        private readonly int[] _secondary = new int[SimOptions.MaxLevel + 1];
        private readonly int[] _kit = new int[SimOptions.MaxLevel + 1];

        private SkillCurve()
        {
        }

        /// <summary>The model's battle (0-based) whose after-state stands for encounter level <paramref name="level"/>.</summary>
        public static int BattleAt(int level, int battles)
        {
            int clamped = Math.Max(1, Math.Min(SimOptions.MaxLevel, level));
            return Math.Min(battles - 1, (PacingSimulator.BattlesPerLevel * (clamped - 1)) + (PacingSimulator.BattlesPerLevel / 2));
        }

        /// <summary>The curve over <paramref name="campaigns"/> (each of <paramref name="battles"/> battles).</summary>
        public static SkillCurve FromCampaigns(List<PacingSimulator.Campaign> campaigns, int battles)
        {
            SkillCurve curve = new SkillCurve();
            for (int level = 1; level <= SimOptions.MaxLevel; level++)
            {
                int b = BattleAt(level, battles);
                curve._focus[level] = Median(campaigns.ConvertAll(c => c.FocusLevelAfter[b]));
                curve._secondary[level] = Median(campaigns.ConvertAll(c => c.SecondaryLevelAfter[b]));
                curve._kit[level] = campaigns.Count == 0 ? 1 : campaigns[0].KitLevelAfter[b];
            }

            return curve;
        }

        /// <summary>
        /// The pacing model's campaigns for <paramref name="options"/>' seed (<c>--runs</c> of <c>--battles</c>), as
        /// the curve; null with <paramref name="error"/> set when the skill library or drop tables cannot be read.
        /// </summary>
        public static SkillCurve Build(SimOptions options, out string error)
        {
            PacingSimulator.Model model = PacingSimulator.LoadModel(options, out error);
            if (model == null)
            {
                return null;
            }

            List<PacingSimulator.Campaign> campaigns = new List<PacingSimulator.Campaign>();
            for (int r = 0; r < options.PacingRuns; r++)
            {
                campaigns.Add(PacingSimulator.Play(model, LootRoller.DeriveSeed(options.Seed, r), options.PacingBattles));
            }

            return FromCampaigns(campaigns, options.PacingBattles);
        }

        public int Focus(int level)
        {
            return _focus[Clamp(level)];
        }

        public int Secondary(int level)
        {
            return _secondary[Clamp(level)];
        }

        /// <summary>A fielded skill the policy never feeds (practice alone; it waits at its first gate).</summary>
        public int PracticeOnly(int level)
        {
            return _kit[Clamp(level)];
        }

        /// <summary>The mean level of the fielded team's <see cref="TeamSkills"/> skills.</summary>
        public double TeamMean(int level)
        {
            return (Focus(level) + Secondary(level) + ((TeamSkills - 2) * PracticeOnly(level))) / (double)TeamSkills;
        }

        /// <summary>The typical team's skill level: <see cref="TeamMean"/> rounded (half away from zero).</summary>
        public int Typical(int level)
        {
            return (int)Math.Round(TeamMean(level), MidpointRounding.AwayFromZero);
        }

        /// <summary>The strong pick's skill level: the focus skill's, for every skill.</summary>
        public int Upgraded(int level)
        {
            return Focus(level);
        }

        private static int Clamp(int level)
        {
            return Math.Max(1, Math.Min(SimOptions.MaxLevel, level));
        }

        private static int Median(List<int> values)
        {
            List<int> sorted = new List<int>(values);
            sorted.Sort();
            return sorted.Count == 0 ? 1 : sorted[(sorted.Count - 1) / 2];
        }
    }
}
