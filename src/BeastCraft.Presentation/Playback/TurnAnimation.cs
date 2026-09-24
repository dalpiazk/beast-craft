using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Vfx;
using BeastCraft.Vfx;

namespace BeastCraft.Presentation.Playback
{
    /// <summary>
    /// An on-apply effect played on top of a beat (<see cref="VfxLibraryData.EffectDefaults"/>): a
    /// status that newly landed on a target (a stun, a shield, a burn...) or a knockback, from the
    /// beat's impact.
    /// </summary>
    public sealed class BeatOverlay
    {
        public BeatOverlay(string key, string unitId, int offsetMs, VfxTimeline timeline)
        {
            Key = key;
            UnitId = unitId;
            OffsetMs = offsetMs;
            Timeline = timeline;
        }

        /// <summary>The effect type (<see cref="VfxEffectKey"/>).</summary>
        public string Key { get; }

        /// <summary>The unit it plays on.</summary>
        public string UnitId { get; }

        /// <summary>When it starts, ms after its beat's start.</summary>
        public int OffsetMs { get; }

        public VfxTimeline Timeline { get; }
    }

    /// <summary>One skill beat of a <see cref="TurnAnimation"/>: when it starts, its effect's timeline and its on-apply overlays.</summary>
    public sealed class ScheduledBeat
    {
        public ScheduledBeat(SkillBeat beat, int startMs, VfxTimeline timeline, IReadOnlyList<BeatOverlay> overlays = null)
        {
            Beat = beat;
            StartMs = startMs;
            Timeline = timeline;
            Overlays = overlays ?? new BeatOverlay[0];
            int duration = timeline.DurationMs;
            foreach (BeatOverlay overlay in Overlays)
            {
                duration = Math.Max(duration, overlay.OffsetMs + overlay.Timeline.DurationMs);
            }

            DurationMs = duration;
        }

        public SkillBeat Beat { get; }

        /// <summary>When the beat starts, ms into the turn.</summary>
        public int StartMs { get; }

        public VfxTimeline Timeline { get; }

        /// <summary>The on-apply effects played from its impact, in target order.</summary>
        public IReadOnlyList<BeatOverlay> Overlays { get; }

        /// <summary>How long the beat plays: its timeline, or its last overlay if that ends later.</summary>
        public int DurationMs { get; }

        /// <summary>When it ends, ms into the turn.</summary>
        public int EndMs
        {
            get { return StartMs + DurationMs; }
        }
    }

    /// <summary>
    /// How one played turn is shown, as a pure function of time into the turn: the acting unit walks
    /// from its start tile to its end tile (<see cref="MoveMs"/>), then each skill beat plays its VFX
    /// timeline in turn, a short gap apart. It answers where a unit stands and how much HP it shows
    /// at any moment — a target's bar drops as each hit lands, and everything settles on the turn's
    /// after-snapshot when the turn ends. Seeded (by turn index) so every replay and screenshot of a
    /// turn is identical.
    /// <para>
    /// A beat's effect resolves skill override, then its primary effect type's default, then its
    /// element's (<see cref="VfxLibrary.Resolve(string, BeastCraft.Creatures.Element, string)"/>);
    /// its area is every tile its targets stand on (<see cref="VfxArea"/>). Each status that newly
    /// lands on a target (and each knockback) adds that effect type's on-apply overlay at the
    /// impact, and a unit's lasting statuses (<see cref="ShownStatusKeys"/>) switch from the
    /// before-snapshot's to the after-snapshot's when the first hit on it lands — so an aura
    /// appears with the blow that applied it and stays exactly as long as the battle keeps the
    /// status.
    /// </para>
    /// </summary>
    public sealed class TurnAnimation
    {
        /// <summary>How long the acting unit takes to walk, when it moved.</summary>
        public const int WalkMs = 240;

        /// <summary>The pause between beats, and after the last one.</summary>
        public const int GapMs = 90;

        /// <summary>How far past the hit-stop a layered effect is shown mid-play (its rings and bursts part-way out).</summary>
        public const int LayeredMidMs = 150;

        private readonly PlayedTurn _turn;
        private readonly HexLayout _layout;
        private readonly List<ScheduledBeat> _beats = new List<ScheduledBeat>();

        public TurnAnimation(PlayedTurn turn, HexLayout layout, VfxLibrary vfx, int seed)
        {
            _turn = turn ?? throw new ArgumentNullException(nameof(turn));
            _layout = layout;
            string actor = turn.Turn.Unit.Id;
            MoveMs = turn.Turn.StartPosition != turn.Turn.EndPosition ? WalkMs : 0;

            int clock = MoveMs;
            HashSet<string> claimed = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < turn.Beats.Count; i++)
            {
                SkillBeat beat = turn.Beats[i];
                VfxEffectData effect = vfx == null ? null : vfx.Resolve(beat.SkillId, beat.Element, beat.PrimaryKey);
                List<VfxTarget> targets = new List<VfxTarget>();
                List<HexCoordinate> tiles = new List<HexCoordinate>();
                foreach (BeatTarget target in beat.Targets)
                {
                    targets.Add(new VfxTarget(target.UnitId, CenterAfter(target.UnitId), target.Damage, target.Crit));
                    if (turn.After.TryGetValue(target.UnitId, out UnitSnapshot hit))
                    {
                        tiles.AddRange(Footprints.Tiles(hit.Position, hit.Footprint));
                    }
                }

                Vec2 from = actor == beat.CasterId && turn.After.ContainsKey(actor) ? CenterAfter(actor) : targets.Count > 0 ? targets[0].Position : Vec2.Zero;
                int beatSeed = unchecked(seed * 997 + turn.Index * 31 + i);
                VfxArea area = tiles.Count > 0 ? VfxArea.OfTiles(tiles, layout) : null;
                VfxTimeline timeline = new VfxTimeline(effect, from, targets, beatSeed, area);
                List<BeatOverlay> overlays = Overlays(beat, from, timeline.ImpactMs, vfx, beatSeed, claimed);
                ScheduledBeat scheduled = new ScheduledBeat(beat, clock, timeline, overlays);
                _beats.Add(scheduled);
                clock += scheduled.DurationMs + GapMs;
            }

            DurationMs = Math.Max(clock, MoveMs + GapMs);
        }

        public PlayedTurn Turn
        {
            get { return _turn; }
        }

        /// <summary>The walk's length (0 when the unit stayed put).</summary>
        public int MoveMs { get; }

        /// <summary>When the whole turn has played.</summary>
        public int DurationMs { get; }

        public IReadOnlyList<ScheduledBeat> Beats
        {
            get { return _beats; }
        }

        /// <summary>
        /// The lasting effect keys (auras, icons) <paramref name="unitId"/> shows at
        /// <paramref name="ms"/>: the before-snapshot's until the first hit on it lands (the acting
        /// unit's own ticks happen as its turn begins, so it shows the after-snapshot's throughout),
        /// the after-snapshot's from then on; a unit no beat touches changes at the turn's end.
        /// </summary>
        public List<string> ShownStatusKeys(string unitId, int ms)
        {
            if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after))
            {
                return new List<string>();
            }

            if (!_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                return after.StatusKeys();
            }

            int switchMs = FirstImpactOn(unitId);
            if (switchMs == int.MaxValue)
            {
                switchMs = unitId == _turn.Turn.Unit.Id ? 0 : DurationMs;
            }

            return ms >= switchMs ? after.StatusKeys() : before.StatusKeys();
        }

        /// <summary>When the first beat that targets <paramref name="unitId"/> lands (int.MaxValue when none does).</summary>
        private int FirstImpactOn(string unitId)
        {
            foreach (ScheduledBeat beat in _beats)
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

        /// <summary>
        /// The on-apply overlays of <paramref name="beat"/>: for each of its targets, every lasting
        /// effect key the target has after the turn and not before that this beat's skill can apply
        /// (the first such beat claims it), and a knockback when the skill knocks back and the target
        /// moved — each an overlay of that key's on-apply effect from the beat's impact. The beat's
        /// own primary key is not repeated (its main effect already is that key's).
        /// </summary>
        private List<BeatOverlay> Overlays(SkillBeat beat, Vec2 from, int impactMs, VfxLibrary vfx, int seed, HashSet<string> claimed)
        {
            List<BeatOverlay> overlays = new List<BeatOverlay>();
            if (vfx == null)
            {
                return overlays;
            }

            for (int t = 0; t < beat.Targets.Count; t++)
            {
                string unitId = beat.Targets[t].UnitId;
                if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after) || !_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
                {
                    continue;
                }

                List<string> added = after.StatusKeys();
                foreach (string had in before.StatusKeys())
                {
                    added.Remove(had);
                }

                if (unitId != _turn.Turn.Unit.Id && before.Position != after.Position && !after.Defeated)
                {
                    added.Add(VfxEffectKey.Knockback);
                }

                foreach (string key in added)
                {
                    VfxEffectData effect = vfx.OnApply(key);
                    bool fromThisSkill = false;
                    foreach (string own in beat.EffectKeys)
                    {
                        fromThisSkill |= own == key;
                    }

                    if (effect == null || !fromThisSkill || key == beat.PrimaryKey || !claimed.Add(unitId + "|" + key))
                    {
                        continue;
                    }

                    VfxTarget target = new VfxTarget(unitId, CenterAfter(unitId), 0, false);
                    VfxTimeline timeline = new VfxTimeline(effect, from, new[] { target }, unchecked(seed * 7 + overlays.Count + 1),
                                                           VfxArea.OfTiles(Footprints.Tiles(after.Position, after.Footprint), _layout));
                    overlays.Add(new BeatOverlay(key, unitId, impactMs, timeline));
                }
            }

            return overlays;
        }

        /// <summary>The beat playing at <paramref name="ms"/> (null in a walk or a gap).</summary>
        public ScheduledBeat BeatAt(int ms)
        {
            foreach (ScheduledBeat beat in _beats)
            {
                if (ms >= beat.StartMs && ms < beat.EndMs)
                {
                    return beat;
                }
            }

            return null;
        }

        /// <summary>
        /// A moment to show a beat mid-play (for screenshots): after the hit-stop, part-way through
        /// its flipbook, while its particles fly. <paramref name="beatIndex"/> out of range gives the turn's end.
        /// </summary>
        public int MidVfxMs(int beatIndex)
        {
            if (beatIndex < 0 || beatIndex >= _beats.Count)
            {
                return DurationMs;
            }

            ScheduledBeat beat = _beats[beatIndex];
            return beat.StartMs + beat.Timeline.HitStopEndMs + Math.Max(beat.Timeline.Effect.Layers != null && beat.Timeline.Effect.Layers.Length > 0 ? LayeredMidMs : 1, beat.Timeline.FlipbookDurationMs / 2);
        }

        /// <summary>Where <paramref name="unitId"/>'s footprint centre is drawn at <paramref name="ms"/>.</summary>
        public Vec2 UnitCenter(string unitId, int ms)
        {
            if (unitId == _turn.Turn.Unit.Id && MoveMs > 0 && ms < MoveMs && _turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                Vec2 from = _layout.FootprintCenter(before.Position, before.Footprint);
                return Vec2.Lerp(from, CenterAfter(unitId), Math.Max(0, ms) / (float)MoveMs);
            }

            return CenterAfter(unitId);
        }

        /// <summary>
        /// The HP <paramref name="unitId"/> shows at <paramref name="ms"/>: its before-HP less every
        /// hit that has landed, never below its after-HP (heals and damage over time settle at the
        /// end), and exactly its after-HP once the turn is over.
        /// </summary>
        public int ShownHp(string unitId, int ms)
        {
            if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after))
            {
                return 0;
            }

            if (ms >= DurationMs || !_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                return after.Hp;
            }

            int landed = 0;
            foreach (ScheduledBeat beat in _beats)
            {
                if (ms < beat.StartMs + beat.Timeline.ImpactMs)
                {
                    continue;
                }

                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    if (target.UnitId == unitId)
                    {
                        landed += target.Damage;
                    }
                }
            }

            return Math.Max(after.Hp, before.Hp - landed);
        }

        /// <summary>
        /// Whether <paramref name="unitId"/> is still drawn at <paramref name="ms"/>: a unit knocked
        /// out this turn stays (fading, see <see cref="ShownFading"/>) until the beat that felled it
        /// has finished playing, or until the turn ends when no hit accounts for it (damage over
        /// time); one already down before the turn is not drawn.
        /// </summary>
        public bool ShownStanding(string unitId, int ms)
        {
            if (!_turn.After.TryGetValue(unitId, out UnitSnapshot after))
            {
                return false;
            }

            if (!after.Defeated)
            {
                return true;
            }

            bool wasDown = _turn.Before.TryGetValue(unitId, out UnitSnapshot before) && before.Defeated;
            return !wasDown && ms < Knockout(unitId, out _);
        }

        /// <summary>Whether <paramref name="unitId"/> has taken its fatal hit by <paramref name="ms"/> but is still drawn (the host fades it).</summary>
        public bool ShownFading(string unitId, int ms)
        {
            if (!ShownStanding(unitId, ms) || !_turn.After.TryGetValue(unitId, out UnitSnapshot after) || !after.Defeated)
            {
                return false;
            }

            Knockout(unitId, out int impactMs);
            return ms >= impactMs;
        }

        /// <summary>When a unit felled this turn stops being drawn; <paramref name="impactMs"/> is when its fatal hit landed.</summary>
        private int Knockout(string unitId, out int impactMs)
        {
            impactMs = DurationMs;
            if (!_turn.Before.TryGetValue(unitId, out UnitSnapshot before))
            {
                return DurationMs;
            }

            int landed = 0;
            foreach (ScheduledBeat beat in _beats)
            {
                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    if (target.UnitId == unitId)
                    {
                        landed += target.Damage;
                    }
                }

                if (landed >= before.Hp)
                {
                    impactMs = beat.StartMs + beat.Timeline.ImpactMs;
                    return beat.EndMs;
                }
            }

            return DurationMs;
        }

        private Vec2 CenterAfter(string unitId)
        {
            return _turn.After.TryGetValue(unitId, out UnitSnapshot snapshot) ? _layout.FootprintCenter(snapshot.Position, snapshot.Footprint) : Vec2.Zero;
        }
    }
}
