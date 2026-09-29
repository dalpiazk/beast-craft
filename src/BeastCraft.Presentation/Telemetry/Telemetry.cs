using System;
using System.Collections.Generic;
using BeastCraft.Save;

namespace BeastCraft.Presentation.Telemetry
{
    /// <summary>
    /// Anonymous gameplay events (#62). Engine-neutral: a provider (still to be chosen, see
    /// docs/design/decisions.md) implements it in a host. The game never calls one directly; it goes
    /// through <see cref="TelemetryGate"/>, which only starts it, and only sends to it, while the player
    /// has said yes (<see cref="PlayerSettings.AnalyticsConsent"/>).
    /// </summary>
    public interface IAnalytics
    {
        /// <summary>Starts the provider. Called once consent is given, never before.</summary>
        void Initialize();

        /// <summary>Records event <paramref name="name"/> with optional <paramref name="fields"/> (no personal data).</summary>
        void Track(string name, IReadOnlyDictionary<string, string> fields);

        /// <summary>Stops the provider when consent is withdrawn; nothing more is sent.</summary>
        void Shutdown();
    }

    /// <summary>
    /// Crash reports (#62), engine-neutral like <see cref="IAnalytics"/>, and gated the same way by
    /// <see cref="PlayerSettings.CrashReportConsent"/>.
    /// </summary>
    public interface ICrashReporter
    {
        /// <summary>Starts the reporter (a real one installs its own crash hooks here). Called once consent is given, never before.</summary>
        void Initialize();

        /// <summary>Reports <paramref name="error"/>, caught where the game noticed it (<paramref name="context"/>).</summary>
        void Report(Exception error, string context);

        /// <summary>Stops the reporter when consent is withdrawn; nothing more is sent.</summary>
        void Shutdown();
    }

    /// <summary>The analytics provider until one is chosen: does nothing.</summary>
    public sealed class NullAnalytics : IAnalytics
    {
        public static readonly NullAnalytics Instance = new NullAnalytics();

        public void Initialize()
        {
        }

        public void Track(string name, IReadOnlyDictionary<string, string> fields)
        {
        }

        public void Shutdown()
        {
        }
    }

    /// <summary>The crash reporter until one is chosen: does nothing.</summary>
    public sealed class NullCrashReporter : ICrashReporter
    {
        public static readonly NullCrashReporter Instance = new NullCrashReporter();

        public void Initialize()
        {
        }

        public void Report(Exception error, string context)
        {
        }

        public void Shutdown()
        {
        }
    }

    /// <summary>
    /// The one way the game reaches its analytics and crash reporter: each is initialised only while its
    /// consent setting is on, and nothing is passed on otherwise. Turning a consent off shuts its provider
    /// down. <see cref="Apply"/> brings the providers in line with the settings (after the consent screen
    /// or a settings change); <see cref="Track"/> and <see cref="Report"/> check again on every call.
    /// </summary>
    public sealed class TelemetryGate
    {
        private readonly Func<PlayerSettings> _settings;
        private IAnalytics _analytics = NullAnalytics.Instance;
        private ICrashReporter _crashes = NullCrashReporter.Instance;

        /// <param name="settings">Where the consent settings are read, each time (the session's current settings).</param>
        public TelemetryGate(Func<PlayerSettings> settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Whether the analytics provider has been initialised (and not shut down since).</summary>
        public bool AnalyticsRunning { get; private set; }

        /// <summary>Whether the crash reporter has been initialised (and not shut down since).</summary>
        public bool CrashReportsRunning { get; private set; }

        private bool AnalyticsAllowed
        {
            get { return _settings()?.AnalyticsConsent == true; }
        }

        private bool CrashReportsAllowed
        {
            get { return _settings()?.CrashReportConsent == true; }
        }

        /// <summary>
        /// The host's providers (null keeps the do-nothing one). Any running provider is shut down first;
        /// the new ones start only if their consent is on.
        /// </summary>
        public void Use(IAnalytics analytics, ICrashReporter crashes)
        {
            StopAnalytics();
            StopCrashReports();
            _analytics = analytics ?? NullAnalytics.Instance;
            _crashes = crashes ?? NullCrashReporter.Instance;
            Apply();
        }

        /// <summary>Starts what the player has agreed to and stops what they have not.</summary>
        public void Apply()
        {
            if (AnalyticsAllowed && !AnalyticsRunning)
            {
                _analytics.Initialize();
                AnalyticsRunning = true;
            }
            else if (!AnalyticsAllowed)
            {
                StopAnalytics();
            }

            if (CrashReportsAllowed && !CrashReportsRunning)
            {
                _crashes.Initialize();
                CrashReportsRunning = true;
            }
            else if (!CrashReportsAllowed)
            {
                StopCrashReports();
            }
        }

        /// <summary>Sends event <paramref name="name"/> when the player has agreed to analytics; otherwise drops it.</summary>
        public void Track(string name, IReadOnlyDictionary<string, string> fields = null)
        {
            Apply();
            if (AnalyticsRunning && !string.IsNullOrEmpty(name))
            {
                _analytics.Track(name, fields ?? new Dictionary<string, string>());
            }
        }

        /// <summary>Reports <paramref name="error"/> when the player has agreed to crash reports; otherwise drops it.</summary>
        public void Report(Exception error, string context)
        {
            Apply();
            if (CrashReportsRunning && error != null)
            {
                _crashes.Report(error, context ?? string.Empty);
            }
        }

        private void StopAnalytics()
        {
            if (AnalyticsRunning)
            {
                _analytics.Shutdown();
                AnalyticsRunning = false;
            }
        }

        private void StopCrashReports()
        {
            if (CrashReportsRunning)
            {
                _crashes.Shutdown();
                CrashReportsRunning = false;
            }
        }
    }
}
