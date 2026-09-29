using System;
using System.Collections.Generic;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Playback;

namespace BeastCraft.Presentation.Audio
{
    /// <summary>One sound of a played turn: when it plays (ms into the turn) and which cue.</summary>
    public readonly struct TimedCue
    {
        public TimedCue(int ms, string cueId)
        {
            Ms = ms;
            CueId = cueId;
        }

        public int Ms { get; }

        public string CueId { get; }

        public override string ToString()
        {
            return Ms + ":" + CueId;
        }
    }

    /// <summary>
    /// A played turn's sounds, read off the same timeline the effects play on (<see cref="TurnAnimation"/>),
    /// so a sound lands with its flash whatever the speed: each beat's cast as it starts
    /// (<see cref="Cast"/>), one hit in the beat's element at its impact when it damaged anyone
    /// (<see cref="HitOf"/>) plus <see cref="Crit"/> when a hit crit, and <see cref="Knockout"/> for each unit
    /// the turn defeated, at the impact of the last beat that struck it (at the turn's start for a
    /// unit burn or poison finished). The cues' haptics come with them (<see cref="AudioCue.Haptic"/>).
    /// </summary>
    public static class BattleAudioCues
    {
        public const string Cast = "sfx.skill.cast";
        public const string Crit = "sfx.hit.crit";
        public const string Knockout = "sfx.ko";

        /// <summary>The hit cue for <paramref name="element"/>: <c>sfx.hit.fire</c> ... <c>sfx.hit.neutral</c> for none.</summary>
        public static string HitOf(Element element)
        {
            return element == Element.None ? "sfx.hit.neutral" : "sfx.hit." + element.ToString().ToLowerInvariant();
        }

        /// <summary>The turn's cues in time order (a cast before its hit; ties keep this order).</summary>
        public static List<TimedCue> For(TurnAnimation animation)
        {
            List<TimedCue> cues = new List<TimedCue>();
            if (animation == null)
            {
                return cues;
            }

            Dictionary<string, int> lastStrike = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ScheduledBeat scheduled in animation.Beats)
            {
                SkillBeat beat = scheduled.Beat;
                int impact = scheduled.StartMs + scheduled.Timeline.ImpactMs;
                cues.Add(new TimedCue(scheduled.StartMs, Cast));
                bool damaged = false;
                bool crit = false;
                foreach (BeatTarget target in beat.Targets)
                {
                    damaged |= target.Damage > 0;
                    crit |= target.Crit && target.Damage > 0;
                    lastStrike[target.UnitId] = impact;
                }

                if (damaged)
                {
                    cues.Add(new TimedCue(impact, HitOf(beat.Element)));
                }

                if (crit)
                {
                    cues.Add(new TimedCue(impact, Crit));
                }
            }

            PlayedTurn turn = animation.Turn;
            foreach (KeyValuePair<string, UnitSnapshot> after in turn.After)
            {
                if (!after.Value.Defeated || !turn.Before.TryGetValue(after.Key, out UnitSnapshot before) || before.Defeated)
                {
                    continue;
                }

                cues.Add(new TimedCue(lastStrike.TryGetValue(after.Key, out int at) ? at : 0, Knockout));
            }

            List<TimedCue> ordered = new List<TimedCue>(cues.Count);
            for (int i = 0; i < cues.Count; i++)
            {
                int at = ordered.Count;
                while (at > 0 && ordered[at - 1].Ms > cues[i].Ms)
                {
                    at--;
                }

                ordered.Insert(at, cues[i]);
            }

            return ordered;
        }

        /// <summary>The cues due as the turn's clock moves from <paramref name="fromMs"/> (exclusive; -1 at a turn's start) to <paramref name="toMs"/> (inclusive).</summary>
        public static List<string> Between(IReadOnlyList<TimedCue> cues, int fromMs, int toMs)
        {
            List<string> due = new List<string>();
            foreach (TimedCue cue in cues ?? new TimedCue[0])
            {
                if (cue.Ms > fromMs && cue.Ms <= toMs)
                {
                    due.Add(cue.CueId);
                }
            }

            return due;
        }
    }
}
