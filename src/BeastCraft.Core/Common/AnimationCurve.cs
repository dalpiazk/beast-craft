using System;

namespace BeastCraft
{
    /// <summary>
    /// A single keyframe on an <see cref="AnimationCurve"/>. Tangents are stored (the roster builder
    /// sets linear ones) but, as noted on <see cref="AnimationCurve"/>, not evaluated.
    /// </summary>
    [Serializable]
    public struct Keyframe
    {
        public float time;
        public float value;
        public float inTangent;
        public float outTangent;

        public Keyframe(float time, float value)
        {
            this.time = time;
            this.value = value;
            inTangent = 0f;
            outTangent = 0f;
        }

        public Keyframe(float time, float value, float inTangent, float outTangent)
        {
            this.time = time;
            this.value = value;
            this.inTangent = inTangent;
            this.outTangent = outTangent;
        }
    }

    /// <summary>
    /// A keyframed curve evaluated piecewise-linearly (tangents are not modelled), used by every
    /// growth curve (<c>GrowthRateCurve.GetScaleAtLevel</c>, so every stat at every level).
    /// <para>
    /// Engine-neutral, and a verbatim port of the evaluator the game has always been balanced and
    /// tested on outside Unity: keys sorted by time, times before the first or after the last key
    /// clamp to that key's value, linear interpolation between neighbours. Authored growth curves
    /// are piecewise-linear by construction (<c>GrowthCurveData.ToAnimationCurve</c>), so this is
    /// their exact shape. The golden samples in <c>Tooling/EditModeTests/Goldens</c> pin it.
    /// </para>
    /// </summary>
    [Serializable]
    public class AnimationCurve
    {
        private Keyframe[] _keys;

        public AnimationCurve()
        {
            _keys = new Keyframe[0];
        }

        public AnimationCurve(params Keyframe[] keys)
        {
            _keys = keys ?? new Keyframe[0];
            SortKeys();
        }

        public Keyframe[] keys
        {
            get { return _keys; }
            set
            {
                _keys = value ?? new Keyframe[0];
                SortKeys();
            }
        }

        public int length
        {
            get { return _keys.Length; }
        }

        /// <summary>A straight line from (timeStart, valueStart) to (timeEnd, valueEnd).</summary>
        public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd)
        {
            return new AnimationCurve(
                new Keyframe(timeStart, valueStart),
                new Keyframe(timeEnd, valueEnd));
        }

        /// <summary>A flat curve holding a single value across the whole time range.</summary>
        public static AnimationCurve Constant(float timeStart, float timeEnd, float value)
        {
            return Linear(timeStart, value, timeEnd, value);
        }

        /// <summary>
        /// Samples the curve. Times before the first key or after the last key clamp to that key's
        /// value rather than extrapolating.
        /// </summary>
        public float Evaluate(float time)
        {
            if (_keys.Length == 0)
            {
                return 0f;
            }

            if (_keys.Length == 1 || time <= _keys[0].time)
            {
                return _keys[0].value;
            }

            int lastIndex = _keys.Length - 1;
            if (time >= _keys[lastIndex].time)
            {
                return _keys[lastIndex].value;
            }

            for (int i = 0; i < lastIndex; i++)
            {
                Keyframe from = _keys[i];
                Keyframe to = _keys[i + 1];

                if (time < from.time || time > to.time)
                {
                    continue;
                }

                float span = to.time - from.time;
                if (span <= 0f)
                {
                    return to.value;
                }

                float t = (time - from.time) / span;
                return from.value + ((to.value - from.value) * t);
            }

            return _keys[lastIndex].value;
        }

        public void AddKey(float time, float value)
        {
            Keyframe[] grown = new Keyframe[_keys.Length + 1];
            Array.Copy(_keys, grown, _keys.Length);
            grown[_keys.Length] = new Keyframe(time, value);
            _keys = grown;
            SortKeys();
        }

        private void SortKeys()
        {
            Array.Sort(_keys, (a, b) => a.time.CompareTo(b.time));
        }
    }
}
