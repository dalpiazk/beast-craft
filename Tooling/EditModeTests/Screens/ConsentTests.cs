using System;
using System.Collections.Generic;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Telemetry;
using BeastCraft.Save;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Analytics and crash-report consent (#62): both off by default, a one-time consent screen, settings rows,
    /// and the rule this enforces: nothing is initialised and nothing is sent unless the player has said yes.
    /// </summary>
    public class ConsentTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        private sealed class SpyAnalytics : IAnalytics
        {
            public int Initialized;
            public int ShutDown;
            public readonly List<string> Events = new List<string>();

            public void Initialize()
            {
                Initialized++;
            }

            public void Track(string name, IReadOnlyDictionary<string, string> fields)
            {
                Events.Add(name);
            }

            public void Shutdown()
            {
                ShutDown++;
            }
        }

        private sealed class SpyCrashes : ICrashReporter
        {
            public int Initialized;
            public int ShutDown;
            public readonly List<string> Reports = new List<string>();

            public void Initialize()
            {
                Initialized++;
            }

            public void Report(Exception error, string context)
            {
                Reports.Add(context);
            }

            public void Shutdown()
            {
                ShutDown++;
            }
        }

        private static GameSession NewSession(SpyAnalytics analytics, SpyCrashes crashes)
        {
            GameSession session = new GameSession(Content, new MemorySaveStorage(), () => 424242, new ManualGameClock(T0, TimeSpan.FromHours(1000)));
            session.Telemetry.Use(analytics, crashes);
            return TestSaves.Started(session);
        }

        [Test]
        public void BothConsents_StartOff_AndTheScreenIsStillToBeAnswered()
        {
            PlayerSettings settings = new PlayerSettings();

            Assert.IsFalse(settings.AnalyticsConsent);
            Assert.IsFalse(settings.CrashReportConsent);
            Assert.IsFalse(settings.ConsentAsked);
            Assert.IsTrue(ConsentViewModel.ShouldAsk(settings));
        }

        [Test]
        public void WithoutConsent_NothingIsInitialised_AndNothingIsSent()
        {
            SpyAnalytics analytics = new SpyAnalytics();
            SpyCrashes crashes = new SpyCrashes();
            GameSession session = NewSession(analytics, crashes);

            session.Telemetry.Track("anything");
            session.Telemetry.Report(new InvalidOperationException("boom"), "test");
            session.Telemetry.Apply();

            Assert.AreEqual(0, analytics.Initialized, "the session's own new_game event must not start analytics");
            Assert.IsEmpty(analytics.Events);
            Assert.AreEqual(0, crashes.Initialized);
            Assert.IsEmpty(crashes.Reports);
            Assert.IsFalse(session.Telemetry.AnalyticsRunning);
            Assert.IsFalse(session.Telemetry.CrashReportsRunning);
        }

        [Test]
        public void TheConsentScreen_AnsweredNo_StaysSilent_AndIsNotAskedAgain()
        {
            SpyAnalytics analytics = new SpyAnalytics();
            SpyCrashes crashes = new SpyCrashes();
            GameSession session = NewSession(analytics, crashes);
            ConsentViewModel consent = new ConsentViewModel(session);
            Assert.IsFalse(consent.Analytics, "both choices start off");
            Assert.IsFalse(consent.CrashReports);

            consent.Confirm();
            session.Telemetry.Track("after");

            Assert.IsTrue(session.Settings.ConsentAsked);
            Assert.IsFalse(ConsentViewModel.ShouldAsk(session.Settings));
            Assert.AreEqual(0, analytics.Initialized);
            Assert.AreEqual(0, crashes.Initialized);
            Assert.IsEmpty(analytics.Events);
        }

        [Test]
        public void EachConsent_StartsOnlyItsOwnProvider()
        {
            SpyAnalytics analytics = new SpyAnalytics();
            SpyCrashes crashes = new SpyCrashes();
            GameSession session = NewSession(analytics, crashes);
            ConsentViewModel consent = new ConsentViewModel(session) { CrashReports = true };

            consent.Confirm();
            session.Telemetry.Track("still_dropped");
            session.Telemetry.Report(new InvalidOperationException("boom"), "sent");

            Assert.AreEqual(0, analytics.Initialized);
            Assert.IsEmpty(analytics.Events);
            Assert.AreEqual(1, crashes.Initialized);
            CollectionAssert.AreEqual(new[] { "sent" }, crashes.Reports);
        }

        [Test]
        public void TheSettingsRows_TurnAnalyticsOnAndOff()
        {
            SpyAnalytics analytics = new SpyAnalytics();
            SpyCrashes crashes = new SpyCrashes();
            GameSession session = NewSession(analytics, crashes);
            SettingsViewModel settings = new SettingsViewModel(session.Settings, Content.Text, null);
            settings.ConsentChanged += () => session.Telemetry.Apply();
            Assert.IsNotNull(settings.Row(SettingsViewModel.Analytics), "on every host");
            Assert.IsNotNull(settings.Row(SettingsViewModel.CrashReports));
            Assert.IsFalse(settings.Row(SettingsViewModel.Analytics).On);

            settings.Change(SettingsViewModel.Analytics);
            session.Telemetry.Track("on");

            Assert.IsTrue(session.Settings.AnalyticsConsent);
            Assert.AreEqual(1, analytics.Initialized);
            CollectionAssert.AreEqual(new[] { "on" }, analytics.Events);
            Assert.AreEqual(0, crashes.Initialized, "crash reports keep their own consent");

            settings.Change(SettingsViewModel.Analytics);
            session.Telemetry.Track("off");

            Assert.IsFalse(session.Settings.AnalyticsConsent);
            Assert.AreEqual(1, analytics.ShutDown);
            CollectionAssert.AreEqual(new[] { "on" }, analytics.Events, "nothing after consent is withdrawn");
        }
    }
}
