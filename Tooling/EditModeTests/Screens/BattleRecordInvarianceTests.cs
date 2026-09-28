using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BeastCraft.Battle;
using BeastCraft.Bonds;
using BeastCraft.Campaign;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// The battle log's recording (the damage breakdown on every <see cref="DamageRoll"/>, the amounts
    /// on every <see cref="AppliedEffect"/>) is presentation data only: it must take no rng draws and
    /// change no outcome. These goldens were taken on <c>main</c> before the recording was added
    /// (a4e6395): a fingerprint of whole campaign battles — every turn's actor, movement, skills,
    /// targets, each hit's amount, crit, variance roll and shield soak, damage over time, the
    /// avatar's arts, the passives' and bonds' hits, the final HP of every unit, the outcome and the
    /// elapsed ticks — across map seeds and team levels (so level gaps apply both ways). Any change
    /// to a draw or a number moves the hash.
    /// <para>
    /// Widened (the Roster review's note) to every <see cref="AppliedEffect"/> too — its target, effect
    /// type, stat, status and recorded <see cref="AppliedEffect.Amount"/> — and re-captured on
    /// <c>main</c> (797ed05) before the Kinship and discovery work touched anything.
    /// </para>
    /// <para>
    /// Re-captured twice more for the producer's never-blocked retune (docs/balance/tuning-log.md,
    /// "Never-blocked targets"): first for <see cref="DamageFormula.LevelDifferenceCap"/> and its k/q
    /// steepened (every case here fields a team level away from the node's own level, so the level-gap
    /// term moves every hash) and the shipping difficulty table recalibrated on the new targets (85%
    /// squad/horde, 75% elite/solo/boss); then again for the trimmed early-region easing (r01 only,
    /// fading to nothing by its own last stage; r02 and r03 need none, adaptive assist covers what is
    /// left) and adaptive assist itself, both of which move a campaign battle's stat multiplier for
    /// any of these that land in r01-r03. All legitimate outcome changes; the recording invariant
    /// itself is untouched.
    /// </para>
    /// </summary>
    public class BattleRecordInvarianceTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        [TestCase(11, 1, "A26CCB2D494EDB3C")]
        [TestCase(12, 3, "BCE0BB77DD8DD464")]
        [TestCase(13, 6, "4C5AB52AD8241523")]
        [TestCase(14, 2, "7A34C70929E43DDA")]
        [TestCase(15, 9, "4B2AA0ECD29B149C")]
        public void CampaignBattle_Fingerprint_IsUnchangedByTheLogRecording(int mapSeed, int teamLevel, string expected)
        {
            string text = Fingerprint(mapSeed, teamLevel);
            string hash = Fnv(text);
            TestContext.WriteLine(mapSeed + "/" + teamLevel + ": " + hash + " (" + text.Length + " chars, " + (text.Split(" e ").Length - 1) + " applied effects)");
            Assert.AreEqual(expected, hash);
        }

        /// <summary>One node battle fought headless to the end, written out as text.</summary>
        internal static string Fingerprint(int mapSeed, int teamLevel)
        {
            GameSession session = TestSaves.Started(new GameSession(Content, new MemorySaveStorage(), () => mapSeed));
            foreach (Save.OwnedBeast beast in session.Save.Beasts)
            {
                beast.Progress.Level = teamLevel;
            }

            MapNode node = CampaignRules.Choices(session.Save.Campaign.ActiveRun)[0];
            NodeBattle battle = new EncounterViewModel(session, node.NodeId).Start(out string error);
            Assert.IsNotNull(battle, error);
            BattleSessionRun run = battle.Run;
            StringBuilder text = new StringBuilder();
            foreach (TeamBondActivation bond in run.Battle.BondActivations)
            {
                text.Append("bond ").Append(bond.Bond?.BondId).Append(' ').Append(bond.Tier).Append('\n');
            }

            foreach (PassiveActivation passive in run.Battle.OpeningPassiveActivations)
            {
                Activation(text, "open", passive.Activation);
            }

            while (true)
            {
                BattleTurnResult turn = run.Step();
                if (turn == null)
                {
                    break;
                }

                text.Append("T ").Append(turn.Unit.Id).Append(' ').Append(turn.StartPosition).Append('>').Append(turn.EndPosition).Append(" m")
                    .Append(turn.MovementSpent).Append(" r").Append(turn.RetreatSteps).Append(turn.Stunned ? " stun" : string.Empty).Append(" dot")
                    .Append(turn.StatusDamage).Append('\n');
                foreach (BattleSkillOutcome outcome in turn.SkillOutcomes)
                {
                    text.Append(" s").Append(outcome.SlotIndex).Append(' ').Append(outcome.Skill?.SkillId).Append(' ').Append(outcome.Status).Append('\n');
                    Activation(text, "  a", outcome.Activation);
                }

                foreach (SkillActivation art in turn.AvatarActivations)
                {
                    Activation(text, " art", art);
                }

                foreach (PassiveActivation passive in turn.PassiveActivations)
                {
                    text.Append(" p ").Append(passive.Passive?.PassiveId).Append(' ').Append(passive.Trigger).Append('\n');
                    Activation(text, "  pa", passive.Activation);
                }

                foreach (BondReactionRecord reaction in turn.BondReactions)
                {
                    text.Append(" b ").Append(reaction.Bond?.BondId).Append(' ').Append(reaction.Trigger).Append(' ').Append(reaction.Reactor?.Id).Append('\n');
                    Activation(text, "  ba", reaction.Activation);
                }
            }

            foreach (BattleUnit unit in run.Units)
            {
                text.Append("U ").Append(unit.Id).Append(' ').Append(unit.CurrentHp).Append('/').Append(unit.Stats.Hp).Append('\n');
            }

            BattleSessionResult result = run.Finish();
            text.Append("O ").Append(result.Outcome).Append(' ').Append(run.Battle.ToResult().ElapsedTicks).Append('\n');
            return text.ToString();
        }

        private static void Activation(StringBuilder text, string tag, SkillActivation activation)
        {
            if (activation == null)
            {
                return;
            }

            text.Append(tag).Append(' ').Append(activation.Skill?.SkillId);
            foreach (BattleUnit target in activation.Targets)
            {
                text.Append(' ').Append(target.Id);
            }

            text.Append('\n');
            foreach (DamageHit hit in activation.Hits)
            {
                text.Append(tag).Append(" h ").Append(hit.Target.Id).Append(' ').Append(hit.Roll.Amount).Append(hit.Roll.IsCrit ? "c" : string.Empty).Append(' ')
                    .Append(hit.Roll.VariancePercent).Append(' ').Append(hit.Absorbed).Append('\n');
            }

            foreach (AppliedEffect applied in activation.Applied)
            {
                text.Append(tag).Append(" e ").Append(applied.Target?.Id).Append(' ').Append(applied.Effect?.EffectType).Append(' ').Append(applied.Effect?.AffectedStat)
                    .Append(' ').Append(applied.Effect?.Status).Append(' ').Append(applied.Amount).Append('\n');
            }
        }

        /// <summary>FNV-1a, 64-bit, over the UTF-8 text, as 16 hex digits.</summary>
        internal static string Fnv(string text)
        {
            ulong hash = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= 1099511628211UL;
            }

            return hash.ToString("X16", CultureInfo.InvariantCulture);
        }
    }
}
