using Android.Content;
using Android.OS;
using BeastCraft.Presentation.Audio;

namespace BeastCraft.Android
{
    /// <summary>
    /// The Android side of <see cref="IHaptics"/> (docs/design/audio.md, "Haptics"): API 29+ plays the
    /// system's predefined effects (Selection EFFECT_TICK, Light EFFECT_CLICK, Medium
    /// EFFECT_HEAVY_CLICK), which feel right on every device; API 26-28 a short one-shot; API 23-25 the
    /// legacy millisecond buzz. Needs the VIBRATE permission (AndroidManifest.xml). The game only asks
    /// while the player has haptics on (<c>AudioDirector.Pulse</c>). A device without a vibrator does
    /// nothing.
    /// </summary>
    /// <remarks>
    /// UNVERIFIED: written without the Android workload, so it has never been compiled or run. Build
    /// it and try each pulse on a device (and on an API 23-28 one) before relying on it.
    /// </remarks>
    public sealed class AndroidHaptics : IHaptics
    {
        private readonly Vibrator _vibrator;

        public AndroidHaptics(Context context)
        {
#pragma warning disable CS0618, CA1422 // VibratorService is the pre-31 path; 31+ uses the VibratorManager.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                _vibrator = (context.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator;
            }
            else
            {
                _vibrator = context.GetSystemService(Context.VibratorService) as Vibrator;
            }
#pragma warning restore CS0618, CA1422
        }

        public void Pulse(HapticPulse pulse)
        {
            if (_vibrator == null || !_vibrator.HasVibrator)
            {
                return;
            }

            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                {
                    int effect = pulse == HapticPulse.Selection ? VibrationEffect.EffectTick : pulse == HapticPulse.Medium ? VibrationEffect.EffectHeavyClick : VibrationEffect.EffectClick;
                    _vibrator.Vibrate(VibrationEffect.CreatePredefined(effect));
                }
                else if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    _vibrator.Vibrate(VibrationEffect.CreateOneShot(Millis(pulse), VibrationEffect.DefaultAmplitude));
                }
                else
                {
#pragma warning disable CS0618, CA1422 // The pre-26 path.
                    _vibrator.Vibrate(Millis(pulse));
#pragma warning restore CS0618, CA1422
                }
            }
            catch (Java.Lang.Exception)
            {
                // A vibrator that refuses (a missing permission, a vendor quirk) is no reason to stop the game.
            }
        }

        /// <summary>The fallback's length: a tick 10 ms, a click 20 ms, a firm click 30 ms.</summary>
        private static long Millis(HapticPulse pulse)
        {
            return pulse == HapticPulse.Selection ? 10 : pulse == HapticPulse.Medium ? 30 : 20;
        }
    }
}
