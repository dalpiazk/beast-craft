using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BeastCraft.Battle;
using BeastCraft.Campaign;
using BeastCraft.Creatures;
using BeastCraft.Presentation.Audio;
using BeastCraft.Presentation.Board;
using BeastCraft.Presentation.Content;
using BeastCraft.Presentation.Playback;
using BeastCraft.Presentation.Screens;
using BeastCraft.Presentation.Ui;
using BeastCraft.Save;
using BeastCraft.Session;
using NUnit.Framework;

namespace BeastCraft.Tests.EditMode
{
    /// <summary>
    /// Sound and haptics (#56, docs/design/audio.md): the cue file resolves and validates, a cue or
    /// track the file lacks plays nothing and is logged once, the volume, mute and haptics settings
    /// round-trip and step, haptics pulse only while switched on (and not in a burst), a played turn's
    /// sounds follow its effects' timeline, and the music layers and track choice.
    /// </summary>
    public class AudioTests
    {
        private static GameContent Content
        {
            get { return VfxLibraryTests.Content; }
        }

        // ------------------------------------------------------------------ Cue resolution

        [Test]
        public void TheCueFile_Validates_AndResolvesEveryCueTheGameAsksFor()
        {
            AudioCueLibraryData data = FieldJson.FromJson<AudioCueLibraryData>(File.ReadAllText(Path.Combine(GameContent.FindRoot(), "data", "Audio", "audio-cues.json")));
            CollectionAssert.IsEmpty(AudioCueLibrary.Validate(data));

            AudioCueLibrary cues = Content.AudioCues;
            AudioCue fire = cues.Resolve("sfx.hit.fire");
            Assert.AreEqual(AudioCueKind.Sfx, fire.Kind);
            CollectionAssert.AreEqual(new[] { "audio/sfx/sfx_hit_fire_oneshot.wav" }, fire.Files);
            Assert.AreEqual(HapticPulse.Light, fire.Haptic);
            Assert.AreEqual(AudioCueLibrary.GainOf(-3f), fire.Gain, 1e-6);

            AudioCue battle = cues.Resolve("music.battle.r01");
            Assert.AreEqual(AudioCueKind.Music, battle.Kind);
            CollectionAssert.AreEqual(new[] { "base", "pulse", "lead", "peak" }.Select(s => "audio/music/mus_battle_r01_" + s + "_loop.ogg"), battle.Files, "stems, base first");
            Assert.IsNull(cues.Resolve("sfx.no_such_cue"));

            List<string> asked = new List<string> { BattleAudioCues.Cast, BattleAudioCues.Crit, BattleAudioCues.Knockout, AudioDirector.UiTap, AudioDirector.UiConfirm, AudioDirector.Rewards, AudioDirector.LevelUp };
            asked.AddRange(((Element[])Enum.GetValues(typeof(Element))).Select(BattleAudioCues.HitOf));
            asked.Add(AudioDirector.TrackFor("title", null));
            asked.Add(AudioDirector.TrackFor("grove", "r01"));
            foreach (RegionData region in Content.Regions.Regions.Concat(Content.Regions.TutorialRegions))
            {
                asked.Add(AudioDirector.TrackFor("battle", region.RegionId));
                asked.Add(AudioDirector.TrackFor("home", region.RegionId));
            }

            CollectionAssert.IsEmpty(asked.Where(id => cues.Resolve(id) == null), "every cue the code can ask for is in the file");
        }

        [Test]
        public void Validate_CatchesRepeatedIds_BadKinds_AndNamesOffTheContract()
        {
            AudioCueLibraryData data = new AudioCueLibraryData
            {
                SchemaVersion = 1,
                Cues = new[]
                {
                    new AudioCueData { Id = "sfx.a", Kind = "sfx", Files = new[] { "sfx_a_oneshot.wav" } },
                    new AudioCueData { Id = "sfx.a", Kind = "sfx", Files = new[] { "sfx_a_oneshot.wav" } },
                    new AudioCueData { Id = "sfx.b", Kind = "noise", Files = new[] { "sfx_b_oneshot.wav" } },
                    new AudioCueData { Id = "sfx.c", Kind = "sfx", Files = new[] { "Hit Fire.wav" } },
                    new AudioCueData { Id = "music.d", Kind = "music", Files = new[] { "mus_d_1_loop.ogg", "mus_d_2_loop.ogg", "mus_d_3_loop.ogg", "mus_d_4_loop.ogg", "mus_d_5_loop.ogg" } },
                    new AudioCueData { Id = "sfx.e", Kind = "sfx", Files = new[] { "sfx_e_oneshot.wav" }, Haptic = "strong" },
                    new AudioCueData { Id = "ambient.f", Kind = "music", Files = new[] { "mus_f_loop.ogg" } }
                }
            };

            List<string> errors = AudioCueLibrary.Validate(data);

            Assert.IsTrue(errors.Any(e => e.Contains("'sfx.a' is used twice")));
            Assert.IsTrue(errors.Any(e => e.Contains("'noise'")));
            Assert.IsTrue(errors.Any(e => e.Contains("'Hit Fire.wav'")));
            Assert.IsTrue(errors.Any(e => e.Contains("5 stems")));
            Assert.IsTrue(errors.Any(e => e.Contains("'strong'")));
            Assert.IsTrue(errors.Any(e => e.Contains("does not start with its kind")));
            Assert.AreEqual(1, AudioCueLibrary.Build(data).Resolve("sfx.a").Files.Count, "the first of a repeated id wins");
        }

        // ------------------------------------------------------------------ Missing cues and files

        [Test]
        public void AMissingCue_PlaysNothing_AndIsLoggedOnce()
        {
            FakeAudio audio = new FakeAudio();
            FakeHaptics haptics = new FakeHaptics();
            List<string> log = new List<string>();
            AudioDirector director = new AudioDirector(Content.AudioCues, audio, new NullMusicPlayer(), haptics, () => new PlayerSettings()) { Log = log.Add };

            director.Cue("sfx.not_a_cue");
            director.Cue("sfx.not_a_cue");

            CollectionAssert.IsEmpty(audio.Played);
            CollectionAssert.IsEmpty(haptics.Pulses);
            Assert.AreEqual(1, log.Count);
            StringAssert.Contains("sfx.not_a_cue", log[0]);
        }

        [Test]
        public void ATrackTheFileLacks_StopsTheMusic_AndIsLoggedOnce()
        {
            NullMusicPlayer music = new NullMusicPlayer();
            List<string> log = new List<string>();
            AudioDirector director = new AudioDirector(Content.AudioCues, null, music, null, () => null) { Log = log.Add };

            director.ScreenChanged("battle", "r01");
            Assert.AreEqual("music.battle.r01", music.Current);
            director.ScreenChanged("battle", "r99");
            director.ScreenChanged("home", "r99");
            director.ScreenChanged("battle", "r99");

            Assert.IsNull(music.Current);
            Assert.AreEqual(2, log.Count, "music.battle.r99 and ambient.r99, once each");
        }

        [Test]
        public void TheContent_LoadsWithNoSoundFiles()
        {
            string audio = Path.Combine(GameContent.FindRoot(), "audio");
            Assume.That(!Directory.Exists(audio) || Directory.GetFiles(audio, "*.*", SearchOption.AllDirectories).Length == 0, "sound files have landed; this checks the empty case");
            List<string> errors = new List<string>();
            Assert.IsNotNull(GameContent.Load(GameContent.FindRoot(), errors), string.Join("\n", errors));
            Assert.Greater(Content.AudioCues.Count, 40);
        }

        // ------------------------------------------------------------------ Settings

        [Test]
        public void TheSoundSettings_DefaultOn_AndRoundTrip_AndAnOlderFileGetsTheDefaults()
        {
            PlayerSettings defaults = new PlayerSettings();
            Assert.AreEqual((100, 100, 100, false, true), (defaults.MasterVolume, defaults.MusicVolume, defaults.SfxVolume, defaults.Muted, defaults.Haptics));

            MemorySaveStorage storage = new MemorySaveStorage();
            PlayerSettingsStore store = new PlayerSettingsStore(storage, new JsonSaveSerializer());
            Assert.IsTrue(store.Save(new PlayerSettings { MasterVolume = 75, MusicVolume = 50, SfxVolume = 25, Muted = true, Haptics = false }));
            PlayerSettings read = store.Load();
            Assert.AreEqual((75, 50, 25, true, false), (read.MasterVolume, read.MusicVolume, read.SfxVolume, read.Muted, read.Haptics));

            Assert.IsTrue(storage.TryWrite(PlayerSettingsStore.SlotName, "{\"SchemaVersion\":1,\"TeamSuggestionsEnabled\":false}"));
            PlayerSettings older = store.Load();
            Assert.IsFalse(older.TeamSuggestionsEnabled, "the file was read");
            Assert.AreEqual((100, 100, 100, false, true), (older.MasterVolume, older.MusicVolume, older.SfxVolume, older.Muted, older.Haptics), "a file from before the sound settings");
            Assert.AreEqual(1, PlayerSettings.CurrentSchemaVersion, "additive fields need no version bump");
        }

        [Test]
        public void TheSettingsScreen_SetsVolumesDirectly_TogglesMuteAndHaptics_AndSaves()
        {
            MemorySaveStorage storage = new MemorySaveStorage();
            GameSession session = new GameSession(Content, storage, () => 1);
            SettingsViewModel phone = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings, true, true);

            Assert.AreEqual(100, phone.MasterVolume);
            Assert.IsTrue(phone.Haptics, "haptics on by default");

            phone.SetMusicVolume(42);
            Assert.AreEqual(42, session.Settings.MusicVolume, "a slider sets the exact value, no stepping");
            phone.SetMusicVolume(-5);
            Assert.AreEqual(0, session.Settings.MusicVolume, "clamped to 0-100");
            phone.SetMusicVolume(150);
            Assert.AreEqual(100, session.Settings.MusicVolume);

            phone.SetMuted(true);
            phone.SetHaptics(false);
            phone.SetSfxVolume(75);

            PlayerSettings saved = new GameSession(Content, storage, () => 1).Settings;
            Assert.IsTrue(saved.Muted);
            Assert.IsFalse(saved.Haptics);
            Assert.AreEqual(75, saved.SfxVolume);
            Assert.IsFalse(phone.Haptics);

            SettingsViewModel desktop = new SettingsViewModel(session.Settings, session.Content.Text, session.SaveSettings);
            Assert.IsFalse(desktop.HapticsAvailable, "no vibration control without haptics");
            desktop.SetHaptics(true);
            Assert.IsFalse(session.Settings.Haptics, "and the setter does nothing there");
            Assert.IsTrue(desktop.Muted, "the Mute control is available on every host");
        }

        [Test]
        public void TheVolumes_FollowMasterTimesChannel_AndMuteSilencesBoth()
        {
            PlayerSettings settings = new PlayerSettings { MasterVolume = 50, MusicVolume = 50, SfxVolume = 100 };
            Assert.AreEqual(0.25f, MusicMix.MusicVolume(settings), 1e-6);
            Assert.AreEqual(0.5f, MusicMix.SfxVolume(settings), 1e-6);
            settings.Muted = true;
            Assert.AreEqual(0f, MusicMix.MusicVolume(settings));
            Assert.AreEqual(0f, MusicMix.SfxVolume(settings));
            Assert.AreEqual(1f, MusicMix.Percent(250));

            FakeAudio audio = new FakeAudio();
            NullMusicPlayer music = new NullMusicPlayer();
            AudioDirector director = new AudioDirector(Content.AudioCues, audio, music, null, () => settings);
            director.Update(16f);
            Assert.AreEqual((0f, 0f), (audio.Volume, music.Volume), "the players follow the settings every frame");
            settings.Muted = false;
            director.Update(16f);
            Assert.AreEqual((0.5f, 0.25f), (audio.Volume, music.Volume));
        }

        // ------------------------------------------------------------------ Haptics

        [Test]
        public void Haptics_PulseOnlyWhileSwitchedOn_AndNotInABurst()
        {
            PlayerSettings settings = new PlayerSettings();
            FakeAudio audio = new FakeAudio();
            FakeHaptics haptics = new FakeHaptics();
            AudioDirector director = new AudioDirector(Content.AudioCues, audio, null, haptics, () => settings);

            director.Cue(BattleAudioCues.HitOf(Element.Fire));
            director.Cue(BattleAudioCues.HitOf(Element.Water));
            CollectionAssert.AreEqual(new[] { HapticPulse.Light }, haptics.Pulses, "two hits in one frame buzz once");
            Assert.AreEqual(2, audio.Played.Count, "both still sound");

            director.Update(AudioDirector.HapticGapMs);
            director.Cue(BattleAudioCues.Knockout);
            CollectionAssert.AreEqual(new[] { HapticPulse.Light, HapticPulse.Light }, haptics.Pulses, "a knockout is a light pulse");

            director.Update(AudioDirector.HapticGapMs);
            director.Cue(BattleAudioCues.Cast);
            Assert.AreEqual(2, haptics.Pulses.Count, "a cast brings no pulse");

            settings.Haptics = false;
            director.Update(AudioDirector.HapticGapMs);
            director.Cue(BattleAudioCues.Knockout);
            Assert.IsFalse(director.Pulse(HapticPulse.Medium));
            Assert.AreEqual(2, haptics.Pulses.Count, "switched off: no pulse");
            Assert.AreEqual(5, audio.Played.Count, "the sound still plays");
        }

        [Test]
        public void ATap_OnAPrimaryButton_Confirms_WithASelectionPulse_AndOtherTapsJustClick()
        {
            FakeAudio audio = new FakeAudio();
            FakeHaptics haptics = new FakeHaptics();
            AudioDirector director = new AudioDirector(Content.AudioCues, audio, null, haptics, () => new PlayerSettings());

            director.Clicked(new Button { StyleKey = "primary" });
            director.Update(AudioDirector.HapticGapMs);
            director.Clicked(new Button { StyleKey = "chip" });
            director.Clicked(new Hotspot());
            director.Clicked(null);

            CollectionAssert.AreEqual(new[] { AudioDirector.UiConfirm, AudioDirector.UiTap, AudioDirector.UiTap }, audio.Played);
            CollectionAssert.AreEqual(new[] { HapticPulse.Selection }, haptics.Pulses);
        }

        // ------------------------------------------------------------------ Battle playback

        [Test]
        public void APlayedTurnsSounds_FollowItsEffectsTimeline()
        {
            BattleSetup setup = DemoBattle.Create(Content, DemoBattle.DefaultSeed, out _, out string error);
            Assert.IsNotNull(setup, error);
            BattlePlayback playback = new BattlePlayback(BattleSession.Begin(setup));
            HexLayout layout = new HexLayout(200, 180);
            int hits = 0;
            int knockouts = 0;
            int defeated = 0;
            PlayedTurn turn;
            while ((turn = playback.Advance()) != null)
            {
                TurnAnimation animation = new TurnAnimation(turn, layout, Content.Vfx, 1);
                List<TimedCue> cues = BattleAudioCues.For(animation);
                for (int i = 1; i < cues.Count; i++)
                {
                    Assert.LessOrEqual(cues[i - 1].Ms, cues[i].Ms, "in time order");
                }

                foreach (ScheduledBeat beat in animation.Beats)
                {
                    Assert.IsTrue(cues.Any(c => c.CueId == BattleAudioCues.Cast && c.Ms == beat.StartMs), "each beat casts as it starts");
                    int impact = beat.StartMs + beat.Timeline.ImpactMs;
                    bool damaged = beat.Beat.Targets.Any(t => t.Damage > 0);
                    Assert.AreEqual(damaged, cues.Any(c => c.CueId == BattleAudioCues.HitOf(beat.Beat.Element) && c.Ms == impact), "a hit in the beat's element at its impact, when it damaged");
                    Assert.AreEqual(beat.Beat.Targets.Any(t => t.Crit && t.Damage > 0), cues.Any(c => c.CueId == BattleAudioCues.Crit && c.Ms == impact));
                    hits += damaged ? 1 : 0;
                }

                int fell = turn.After.Count(pair => pair.Value.Defeated && turn.Before.TryGetValue(pair.Key, out UnitSnapshot before) && !before.Defeated);
                Assert.AreEqual(fell, cues.Count(c => c.CueId == BattleAudioCues.Knockout), "one knockout per unit the turn felled");
                knockouts += cues.Count(c => c.CueId == BattleAudioCues.Knockout);
                defeated += fell;
                Assert.IsTrue(cues.All(c => c.Ms >= 0 && c.Ms <= animation.DurationMs));

                CollectionAssert.AreEqual(cues.Select(c => c.CueId), BattleAudioCues.Between(cues, -1, animation.DurationMs), "a whole turn plays every cue once");
                List<string> split = BattleAudioCues.Between(cues, -1, animation.DurationMs / 2);
                split.AddRange(BattleAudioCues.Between(cues, animation.DurationMs / 2, animation.DurationMs));
                CollectionAssert.AreEqual(cues.Select(c => c.CueId), split, "frame by frame, nothing twice and nothing lost");
            }

            Assert.Greater(hits, 0);
            Assert.Greater(knockouts, 0, "the demo battle ends with knockouts");
            Assert.AreEqual(defeated, knockouts);
        }

        // ------------------------------------------------------------------ Music

        [Test]
        public void TheLayers_ComeInOneByOne_AsTheIntensityRises()
        {
            CollectionAssert.AreEqual(new[] { 1f, 0f, 0f, 0f }, MusicMix.LayerWeights(0f, 4));
            CollectionAssert.AreEqual(new[] { 1f, 1f, 0.5f, 0f }, MusicMix.LayerWeights(0.5f, 4));
            CollectionAssert.AreEqual(new[] { 1f, 1f, 1f, 1f }, MusicMix.LayerWeights(1f, 4));
            CollectionAssert.AreEqual(new[] { 1f }, MusicMix.LayerWeights(0.7f, 1), "a single-stem track always plays whole");
            Assert.AreEqual(0.5f, MusicMix.Step(0f, 1f, 750f, MusicMix.LayerFadeMs), 1e-6);
            Assert.AreEqual(0f, MusicMix.Step(0.2f, 0f, 750f, MusicMix.LayerFadeMs), "a fade never overshoots");
            Assert.AreEqual(short.MaxValue, MusicMix.ToPcm16(3f), "the mix clips");
            Assert.AreEqual(0.25f, AudioDirector.BattleIntensity(100, 100), 1e-6, "the fight opens at a quarter");
            Assert.AreEqual(1f, AudioDirector.BattleIntensity(0, 100), 1e-6);
        }

        [Test]
        public void EachScreen_PlaysItsTrack_AndTheResultsKeepTheBattles()
        {
            NullMusicPlayer music = new NullMusicPlayer();
            AudioDirector director = new AudioDirector(Content.AudioCues, null, music, null, () => null);

            director.ScreenChanged("title", null);
            Assert.AreEqual("music.title", music.Current);
            director.ScreenChanged("starter-pick", null);
            Assert.AreEqual("music.title", music.Current, "no region yet: the title theme plays on");
            director.ScreenChanged("home", "r01");
            Assert.AreEqual("ambient.r01", music.Current);
            director.ScreenChanged("battle", "r01");
            Assert.AreEqual("music.battle.r01", music.Current);
            Assert.AreEqual(0f, music.Intensity, "a new track starts at its base layer");
            director.SetIntensity(0.8f);
            director.ScreenChanged("results", "r01");
            Assert.AreEqual("music.battle.r01", music.Current, "the results keep the battle's music");
            Assert.AreEqual(0.8f, music.Intensity, 1e-6);
            director.ScreenChanged("grove", "r01");
            Assert.AreEqual("ambient.grove", music.Current);
        }

        private sealed class FakeAudio : IAudio
        {
            public readonly List<string> Played = new List<string>();

            public float Volume { get; set; } = 1f;

            public void Play(string cueId)
            {
                Played.Add(cueId);
            }
        }

        private sealed class FakeHaptics : IHaptics
        {
            public readonly List<HapticPulse> Pulses = new List<HapticPulse>();

            public void Pulse(HapticPulse pulse)
            {
                Pulses.Add(pulse);
            }
        }
    }
}
