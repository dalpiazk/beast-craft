using System;
using System.Collections.Generic;
using BeastCraft.Battle.Grid;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Layout;
using BeastCraft.Presentation.Playback;

namespace BeastCraft.Presentation.Camera
{
    /// <summary>One framing of a <see cref="TurnCamera"/>: from <see cref="StartMs"/> the camera eases to <see cref="View"/>.</summary>
    public readonly struct CameraShot
    {
        public CameraShot(int startMs, CameraView view)
        {
            StartMs = startMs;
            View = view;
        }

        /// <summary>When the camera starts moving to this framing, ms into the turn.</summary>
        public int StartMs { get; }

        public CameraView View { get; }
    }

    /// <summary>
    /// The camera over one played turn, as a pure function of time into the turn (the same clock
    /// as <see cref="TurnAnimation"/>, which runs at the playback speed, so x2 and x3 move the
    /// camera two and three times as fast): starting from the view the turn began with, it eases
    /// to frame the acting unit's walk (when it moved), then — a little before each skill beat —
    /// the caster, every target and the effect's area, with padding, and holds the last framing to
    /// the turn's end. The host eases back toward <see cref="CameraRig.FitAll"/> between turns.
    /// A turn with neither a walk nor a beat (a stun, a wait) frames the acting unit.
    /// </summary>
    public sealed class TurnCamera
    {
        private readonly CameraRig _rig;
        private readonly List<CameraShot> _shots = new List<CameraShot>();
        private readonly List<CameraView> _startViews = new List<CameraView>();

        /// <param name="animation">The turn being shown.</param>
        /// <param name="layout">The hex layout the animation places units with.</param>
        /// <param name="rig">The framing maths for this arena.</param>
        /// <param name="start">Where the camera was when the turn began.</param>
        public TurnCamera(TurnAnimation animation, HexLayout layout, CameraRig rig, CameraView start)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            Start = rig.Clamp(start);
            if (animation == null)
            {
                return;
            }

            PlayedTurn turn = animation.Turn;
            string actor = turn.Turn.Unit.Id;
            if (animation.MoveMs > 0)
            {
                List<Rect> walk = new List<Rect>();
                AddUnit(walk, turn.Before, actor, layout);
                AddUnit(walk, turn.After, actor, layout);
                Add(0, rig.Frame(walk));
            }

            foreach (ScheduledBeat beat in animation.Beats)
            {
                List<Rect> boxes = new List<Rect>();
                if (beat.Beat.CasterId != null && turn.After.ContainsKey(beat.Beat.CasterId))
                {
                    AddUnit(boxes, turn.After, beat.Beat.CasterId, layout);
                }

                foreach (BeatTarget target in beat.Beat.Targets)
                {
                    AddUnit(boxes, turn.After, target.UnitId, layout);
                }

                foreach (BeatOverlay overlay in beat.Overlays)
                {
                    AddUnit(boxes, turn.After, overlay.UnitId, layout);
                }

                if (beat.Timeline.Area != null && beat.Beat.Targets.Count > 0)
                {
                    boxes.Add(CameraRig.CircleBox(beat.Timeline.Area.Center, beat.Timeline.Area.Radius));
                }

                int at = Math.Max(0, beat.StartMs - rig.Settings.LeadMs);
                Add(at, rig.Frame(boxes));
            }

            if (_shots.Count == 0)
            {
                List<Rect> self = new List<Rect>();
                AddUnit(self, turn.After, actor, layout);
                Add(0, rig.Frame(self));
            }
        }

        /// <summary>The view the turn started from.</summary>
        public CameraView Start { get; }

        /// <summary>The framings in time order (never two at the same moment: a later one replaces an earlier).</summary>
        public IReadOnlyList<CameraShot> Shots
        {
            get { return _shots; }
        }

        /// <summary>
        /// The camera <paramref name="ms"/> into the turn: <see cref="Start"/> before the first shot,
        /// then each shot eased from wherever the camera was when it began (so a shot that starts
        /// before the last one arrived continues smoothly from mid-move).
        /// </summary>
        public CameraView Sample(int ms)
        {
            int index = -1;
            for (int i = 0; i < _shots.Count; i++)
            {
                if (_shots[i].StartMs <= ms)
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                return Start;
            }

            CameraShot shot = _shots[index];
            return _rig.Ease(_startViews[index], shot.View, ms - shot.StartMs, _rig.Settings.EaseMs);
        }

        private void Add(int startMs, CameraView view)
        {
            if (_shots.Count > 0 && _shots[_shots.Count - 1].StartMs >= startMs)
            {
                startMs = _shots[_shots.Count - 1].StartMs;
                _shots.RemoveAt(_shots.Count - 1);
                _startViews.RemoveAt(_startViews.Count - 1);
            }

            // Where the camera is when this shot begins: the previous shot part-way (or all the way) there.
            CameraView from = _shots.Count == 0
                                  ? Start
                                  : _rig.Ease(_startViews[_shots.Count - 1], _shots[_shots.Count - 1].View, startMs - _shots[_shots.Count - 1].StartMs,
                                              _rig.Settings.EaseMs);
            _shots.Add(new CameraShot(startMs, view));
            _startViews.Add(from);
        }

        private static void AddUnit(List<Rect> boxes, IReadOnlyDictionary<string, UnitSnapshot> state, string unitId, HexLayout layout)
        {
            if (unitId == null || !state.TryGetValue(unitId, out UnitSnapshot unit))
            {
                return;
            }

            Vec2 feet = layout.FootprintCenter(unit.Position, unit.Footprint);
            boxes.Add(CameraRig.UnitBox(feet, unit.Footprint == UnitFootprint.Single ? 1f : 2f));
        }
    }
}
