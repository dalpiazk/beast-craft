// -----------------------------------------------------------------------------------------------
// Minimal compile-only stand-ins for the UnityEngine APIs that the Beast Craft game scripts use.
//
// This file is NOT shipped and is NOT part of the Unity project. It exists purely so that CI can
// compile BeastCraft/Assets/_Project/Scripts/** without a Unity Editor install or licence.
//
// Rules for extending this file:
//   * Only add API surface that the real game scripts actually reference.
//   * Where a type has real behaviour the game logic depends on (Color's HSV conversions,
//     AnimationCurve's evaluation), implement genuine, correct math -- not a throw/log stub.
//     A compile check is only meaningful if the semantics it compiles against are honest.
// -----------------------------------------------------------------------------------------------

using System;

namespace UnityEngine
{
    /// <summary>
    /// Stand-in for <c>UnityEngine.Object</c>. Only <see cref="name"/> is used by the game scripts
    /// (in log messages), plus its role as the second parameter of <c>Debug.LogError</c>.
    /// </summary>
    public class Object
    {
        // Lower-cased deliberately: the game scripts read `name` exactly as Unity spells it.
        public string name { get; set; }

        /// <summary>
        /// Stand-in for <c>Object.hideFlags</c>. Stored only: the game sets
        /// <see cref="HideFlags.DontUnloadUnusedAsset"/> on in-memory assets it builds at run time,
        /// which matters to Unity's <c>Resources.UnloadUnusedAssets</c> and to nothing outside it.
        /// </summary>
        public HideFlags hideFlags { get; set; }

        /// <summary>
        /// Stand-in for <c>Object.DestroyImmediate</c>, which the EditMode tests call in teardown to
        /// free the ScriptableObjects they create. There is no native object to free outside Unity,
        /// so this is a genuine no-op, not a lie: the managed instance is simply left to the GC.
        /// </summary>
        public static void DestroyImmediate(Object obj)
        {
        }

        public override string ToString()
        {
            return name ?? base.ToString();
        }
    }

    /// <summary>Stand-in for <c>UnityEngine.HideFlags</c>, with Unity's values.</summary>
    [Flags]
    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
        HideInInspector = 2,
        DontSaveInEditor = 4,
        NotEditable = 8,
        DontSaveInBuild = 16,
        DontUnloadUnusedAsset = 32,
        DontSave = 52,
        HideAndDontSave = 61
    }

    /// <summary>
    /// Stand-in for <c>UnityEngine.ScriptableObject</c>. <see cref="CreateInstance{T}"/> is used
    /// by the Editor roster importer.
    /// </summary>
    public class ScriptableObject : Object
    {
        /// <summary>
        /// Plain construction. Real Unity also registers the native object; nothing CI compiles
        /// depends on that.
        /// </summary>
        public static T CreateInstance<T>() where T : ScriptableObject
        {
            return (T)Activator.CreateInstance(typeof(T));
        }
    }

    /// <summary>Marker stand-in for <c>UnityEngine.Sprite</c>; only used as a field type.</summary>
    public class Sprite : Object
    {
    }

    /// <summary>
    /// Stand-in for <c>UnityEngine.Color</c> with real RGB/HSV conversion math, matching Unity's
    /// own algorithm so that the clamping behaviour in
    /// <c>ColorPickerDefinition.GetDefaultColor()</c> produces the same results here as in-editor.
    /// </summary>
    [Serializable]
    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Color(float r, float g, float b)
            : this(r, g, b, 1f)
        {
        }

        public static readonly Color white = new Color(1f, 1f, 1f, 1f);

        public static readonly Color black = new Color(0f, 0f, 0f, 1f);

        public static readonly Color clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// Converts an RGB colour to hue/saturation/value, all in the 0-1 range. Mirrors Unity's
        /// implementation, including its treatment of a zero-value (black) colour as hue 0,
        /// saturation 0.
        /// </summary>
        public static void RGBToHSV(Color rgbColor, out float H, out float S, out float V)
        {
            if (rgbColor.b > rgbColor.g && rgbColor.b > rgbColor.r)
            {
                RGBToHSVHelper(4f, rgbColor.b, rgbColor.r, rgbColor.g, out H, out S, out V);
            }
            else if (rgbColor.g > rgbColor.r)
            {
                RGBToHSVHelper(2f, rgbColor.g, rgbColor.b, rgbColor.r, out H, out S, out V);
            }
            else
            {
                RGBToHSVHelper(0f, rgbColor.r, rgbColor.g, rgbColor.b, out H, out S, out V);
            }
        }

        /// <summary>
        /// Converts hue/saturation/value (each 0-1) back to an opaque RGB colour. Mirrors Unity's
        /// sector-based implementation.
        /// </summary>
        public static Color HSVToRGB(float H, float S, float V)
        {
            Color result = white;

            if (S == 0f)
            {
                result.r = V;
                result.g = V;
                result.b = V;
                return result;
            }

            if (V == 0f)
            {
                result.r = 0f;
                result.g = 0f;
                result.b = 0f;
                return result;
            }

            result.r = 0f;
            result.g = 0f;
            result.b = 0f;

            float hueSixths = H * 6f;
            int sector = (int)Mathf.Floor(hueSixths);
            float fraction = hueSixths - sector;
            float p = V * (1f - S);
            float q = V * (1f - (S * fraction));
            float t = V * (1f - (S * (1f - fraction)));

            switch (sector)
            {
                case 0:
                    result.r = V;
                    result.g = t;
                    result.b = p;
                    break;
                case 1:
                    result.r = q;
                    result.g = V;
                    result.b = p;
                    break;
                case 2:
                    result.r = p;
                    result.g = V;
                    result.b = t;
                    break;
                case 3:
                    result.r = p;
                    result.g = q;
                    result.b = V;
                    break;
                case 4:
                    result.r = t;
                    result.g = p;
                    result.b = V;
                    break;
                case 5:
                    result.r = V;
                    result.g = p;
                    result.b = q;
                    break;
                case 6:
                    // H == 1 wraps back onto the start of the red sector.
                    result.r = V;
                    result.g = t;
                    result.b = p;
                    break;
                case -1:
                    result.r = V;
                    result.g = p;
                    result.b = q;
                    break;
                default:
                    // Out-of-range hue: Unity yields black here rather than extrapolating.
                    break;
            }

            return result;
        }

        public override string ToString()
        {
            return "RGBA(" + r + ", " + g + ", " + b + ", " + a + ")";
        }

        private static void RGBToHSVHelper(
            float offset,
            float dominantColor,
            float colorOne,
            float colorTwo,
            out float H,
            out float S,
            out float V)
        {
            V = dominantColor;

            if (V == 0f)
            {
                S = 0f;
                H = 0f;
                return;
            }

            float smallest = colorOne > colorTwo ? colorTwo : colorOne;
            float difference = V - smallest;

            if (difference != 0f)
            {
                S = difference / V;
                H = offset + ((colorOne - colorTwo) / difference);
            }
            else
            {
                S = 0f;
                H = offset + (colorOne - colorTwo);
            }

            H /= 6f;

            if (H < 0f)
            {
                H += 1f;
            }
        }
    }

    /// <summary>
    /// Stand-in for <c>UnityEngine.Mathf</c>, limited to the handful of helpers the game scripts
    /// call. Semantics match Unity's (which in turn match <see cref="System.Math"/>).
    /// </summary>
    public static class Mathf
    {
        public static float Min(float a, float b)
        {
            return a < b ? a : b;
        }

        public static float Max(float a, float b)
        {
            return a > b ? a : b;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Floor(float value)
        {
            return (float)Math.Floor(value);
        }

        /// <summary>
        /// Rounds to the nearest integer. Unity uses banker's rounding here (.5 goes to the nearest
        /// even integer), which is <see cref="MidpointRounding.ToEven"/> -- the .NET default.
        /// </summary>
        public static int RoundToInt(float value)
        {
            return (int)Math.Round(value, MidpointRounding.ToEven);
        }
    }

    /// <summary>
    /// A single keyframe on an <see cref="AnimationCurve"/>. Tangents are stored (the roster
    /// importer sets linear ones) but, as noted on <see cref="AnimationCurve"/>, not evaluated.
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
    /// Stand-in for <c>UnityEngine.AnimationCurve</c> with a real (linear) keyframe evaluator.
    /// Tangents are not modelled: curves authored in this stub behave as piecewise-linear, which is
    /// exactly correct for <see cref="Linear"/> and a reasonable approximation otherwise. This type
    /// carries behaviour the game logic depends on (<c>GrowthRateCurve.GetScaleAtLevel</c>), so it
    /// is implemented rather than stubbed out.
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
        /// value (Unity's default wrap mode), matching real behaviour rather than extrapolating.
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

    /// <summary>
    /// Stand-in for <c>UnityEngine.Debug</c>. CI does not need real logging, only for the calls to
    /// compile; output goes to stderr so that anything unexpected is still visible in a CI log.
    /// </summary>
    public static class Debug
    {
        public static void Log(string message)
        {
            Console.Out.WriteLine(message);
        }

        public static void LogWarning(string message)
        {
            Console.Error.WriteLine(message);
        }

        public static void LogError(string message)
        {
            Console.Error.WriteLine(message);
        }

        public static void LogError(string message, Object context)
        {
            Console.Error.WriteLine(message + (context == null ? string.Empty : " (context: " + context.name + ")"));
        }
    }

    /// <summary>
    /// Stand-in for <c>UnityEngine.JsonUtility</c>, used by the Editor data importers, the save
    /// serializer and several EditMode tests.
    /// <para>
    /// By default (CiLint, BalanceSim) this is a compile-only surface and throws if actually called:
    /// netstandard2.1 has no built-in JSON serializer and this project takes no package references.
    /// </para>
    /// <para>
    /// When compiled with <c>UNITYSTUB_SYSTEM_TEXT_JSON</c> defined (only
    /// <c>Tooling/EditModeTests</c>, which compiles these sources directly on a runtime that ships
    /// System.Text.Json), it is a real implementation over System.Text.Json restricted to
    /// JsonUtility's rules: public instance fields only (no properties, no readonly fields, no
    /// <c>[NonSerialized]</c> fields), names exactly as declared, case-sensitive, enums as numbers,
    /// unknown keys ignored.
    /// </para>
    /// </summary>
    public static class JsonUtility
    {
#if UNITYSTUB_SYSTEM_TEXT_JSON
        private static readonly System.Text.Json.JsonSerializerOptions CompactOptions = CreateOptions(false);
        private static readonly System.Text.Json.JsonSerializerOptions PrettyOptions = CreateOptions(true);

        public static T FromJson<T>(string json)
        {
            // Like JsonUtility, malformed or empty input throws (here a JsonException/ArgumentNullException).
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, CompactOptions);
        }

        public static string ToJson(object obj)
        {
            return ToJson(obj, false);
        }

        public static string ToJson(object obj, bool prettyPrint)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            return System.Text.Json.JsonSerializer.Serialize(obj, obj.GetType(), prettyPrint ? PrettyOptions : CompactOptions);
        }

        private static System.Text.Json.JsonSerializerOptions CreateOptions(bool prettyPrint)
        {
            System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver resolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(KeepOnlyJsonUtilityFields);

            return new System.Text.Json.JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = prettyPrint,
                TypeInfoResolver = resolver
            };
        }

        /// <summary>Drops every member JsonUtility would not write: properties, readonly fields and [NonSerialized] fields.</summary>
        private static void KeepOnlyJsonUtilityFields(System.Text.Json.Serialization.Metadata.JsonTypeInfo typeInfo)
        {
            if (typeInfo.Kind != System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Object)
            {
                return;
            }

            for (int i = typeInfo.Properties.Count - 1; i >= 0; i--)
            {
                System.Reflection.FieldInfo field = typeInfo.Properties[i].AttributeProvider as System.Reflection.FieldInfo;

                if (field == null || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    typeInfo.Properties.RemoveAt(i);
                }
            }
        }
#else
        public static T FromJson<T>(string json)
        {
            throw new NotSupportedException("UnityStub.JsonUtility is compile-only; parse JSON in Unity or with System.Text.Json.");
        }

        public static string ToJson(object obj)
        {
            throw new NotSupportedException("UnityStub.JsonUtility is compile-only; write JSON in Unity or with System.Text.Json.");
        }

        public static string ToJson(object obj, bool prettyPrint)
        {
            throw new NotSupportedException("UnityStub.JsonUtility is compile-only; write JSON in Unity or with System.Text.Json.");
        }
#endif
    }

    /// <summary>
    /// Stand-in for <c>UnityEngine.Application</c>, limited to <see cref="persistentDataPath"/>
    /// (read by <c>UnitySaveLocations</c>). Outside Unity there is no per-app data folder, so it is a
    /// real, writable directory under the system temp path — honest behaviour, not a placeholder.
    /// </summary>
    public static class Application
    {
        // Lower-cased deliberately: the game scripts read it exactly as Unity spells it.
        public static string persistentDataPath
        {
            get { return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BeastCraftUnityStub", "persistentData"); }
        }
    }

    /// <summary>Stand-in base for Unity's inspector-decoration attributes.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public abstract class PropertyAttribute : Attribute
    {
        public int order { get; set; }
    }

    /// <summary>
    /// Stand-in for <c>[TextArea]</c>. Used bare in the codebase, so the parameterless form is what
    /// matters; the (minLines, maxLines) form is kept because that is Unity's real shape.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class TextAreaAttribute : PropertyAttribute
    {
        public readonly int minLines;
        public readonly int maxLines;

        public TextAreaAttribute()
        {
            minLines = 3;
            maxLines = 3;
        }

        public TextAreaAttribute(int minLines, int maxLines)
        {
            this.minLines = minLines;
            this.maxLines = maxLines;
        }
    }

    /// <summary>Stand-in for <c>[Range(min, max)]</c>.</summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class RangeAttribute : PropertyAttribute
    {
        public readonly float min;
        public readonly float max;

        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    /// <summary>
    /// Stand-in for <c>[CreateAssetMenu(...)]</c>. The codebase uses the named-property form with
    /// <c>menuName</c> and <c>fileName</c>; <c>order</c> is included to match Unity's real shape.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }

        public string fileName { get; set; }

        public int order { get; set; }
    }

    /// <summary>
    /// Stand-in for <c>[SerializeField]</c>. Not currently used by the game scripts (they expose
    /// public fields), but included because it is the single most likely next addition and costs
    /// nothing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class SerializeFieldAttribute : Attribute
    {
    }
}
