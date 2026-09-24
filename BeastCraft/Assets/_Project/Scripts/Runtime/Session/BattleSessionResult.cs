using System;
using System.Collections.Generic;
using BeastCraft.Battle;
using BeastCraft.Battle.Grid;
using BeastCraft.Bonds;

namespace BeastCraft.Session
{
    /// <summary>
    /// What <see cref="BattleSession.Run"/> produced: either a set-up failure
    /// (<see cref="Success"/> false, every reason in <see cref="Errors"/>, no battle was fought), or
    /// the finished <see cref="Battle"/> plus the usage counts rewards are paid from, keyed by the
    /// save's <see cref="Save.OwnedBeast.BeastId"/> rather than battle unit ids.
    /// </summary>
    public class BattleSessionResult
    {
        internal BattleSessionResult()
        {
        }

        /// <summary>Whether the battle was set up and fought.</summary>
        public bool Success
        {
            get { return Errors.Count == 0 && Battle != null; }
        }

        /// <summary>Every set-up problem found; empty on success.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>The errors on one line each, or null on success.</summary>
        public string Error
        {
            get { return Errors.Count == 0 ? null : string.Join("\n", Errors); }
        }

        /// <summary>The seed the battle ran with.</summary>
        public int Seed { get; internal set; }

        /// <summary>The finished battle (outcome, turns, activations), or null on failure.</summary>
        public BattleResult Battle { get; internal set; }

        /// <summary>The battle's outcome; <see cref="BattleOutcome.Stalemate"/> when there was no battle.</summary>
        public BattleOutcome Outcome
        {
            get { return Battle == null ? BattleOutcome.Stalemate : Battle.Outcome; }
        }

        /// <summary>The board, as the battle left it.</summary>
        public HexGrid Grid { get; internal set; }

        /// <summary>The roster (enemies, then the player's beasts in team order), in their end-of-battle state. Not the avatar.</summary>
        public IReadOnlyList<BattleUnit> Units { get; internal set; } = new List<BattleUnit>();

        /// <summary>The avatar's unit, or null when it did not take part.</summary>
        public BattleUnit Avatar { get; internal set; }

        /// <summary>The team bonds that were active, in application order.</summary>
        public IReadOnlyList<ActiveTeamBond> ActiveBonds { get; internal set; } = new List<ActiveTeamBond>();

        /// <summary>Team beast id to its battle unit id, in team order.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> TeamUnitIds { get; internal set; } = new List<KeyValuePair<string, string>>();

        /// <summary>Beast id to (skill id to fires), for the team's beasts (<see cref="BattleSkillUsage.CountFiredSkillsFor"/>). Every team beast has an entry.</summary>
        public Dictionary<string, Dictionary<string, int>> SkillUsesByBeastId { get; } = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        /// <summary>The avatar's active-skill fires by skill id (<see cref="BattleSkillUsage.CountAvatarActiveUses"/>).</summary>
        public Dictionary<string, int> AvatarActiveUses { get; internal set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The avatar's passive firings by passive id (<see cref="BattleSkillUsage.CountPassiveTriggers"/>).</summary>
        public Dictionary<string, int> PassiveTriggers { get; internal set; } = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Whether <see cref="BattleSession.ApplyRewards"/> has already paid this battle out.</summary>
        public bool RewardsApplied { get; internal set; }

        /// <summary>The unit id <paramref name="beastId"/> fought as, or null.</summary>
        public string UnitIdFor(string beastId)
        {
            foreach (KeyValuePair<string, string> pair in TeamUnitIds)
            {
                if (string.Equals(pair.Key, beastId, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            return null;
        }
    }
}
