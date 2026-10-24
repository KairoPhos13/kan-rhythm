// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Replays;
using osu.Game.Screens.Play;
using Kan.Core.Native;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;
using Kan.Core.Play;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// Listening: the live input (microphone or a file), where what it hears falls on the level,
    /// the facts handed to the judge, the take recorded for the replay, the instrument's tuning learnt.
    /// </summary>
    public partial class KanPlayState : Component
    {
        /// <summary>Test without a microphone: a recording played as the player (KAN_LIVE_INPUT=path).</summary>
        public const string INPUT_ENV = @"KAN_LIVE_INPUT";

        /// <summary>Where that recording starts on the level's timeline, in ms (KAN_LIVE_INPUT_OFFSET_MS).</summary>
        public const string INPUT_OFFSET_ENV = @"KAN_LIVE_INPUT_OFFSET_MS";

        /// <summary>KAN_LIVE_INPUT_REALTIME=1: the recording plays in real time, as a player would (rate and wait modes).</summary>
        public const string INPUT_REALTIME_ENV = @"KAN_LIVE_INPUT_REALTIME";

        /// <summary>What the player played, kept with the play to be heard in its replay (owner 2026-10-22).</summary>
        private PlayTake? take;

        /// <summary>One reading of the player's pitch, stamped with real time (it keeps coming while the level stands still).</summary>
        public readonly record struct LivePoint(double RealMs, float Midi, float Level);

        private readonly LivePoint[] live = new LivePoint[256];

        private int liveHead;

        /// <summary>How many live readings are kept (newest first through <see cref="LiveAt"/>).</summary>
        public int LiveCount { get; private set; }

        /// <summary>The i-th newest live reading (0 = newest).</summary>
        public LivePoint LiveAt(int i) => live[(liveHead - 1 - i + live.Length * 2) % live.Length];

        /// <summary>The player's instrument tuning, taken off everything heard live (owner 2026-10-15).</summary>
        private TuningTracker tuning = new TuningTracker();

        /// <summary>The instrument's tuning in use (cents from A440).</summary>
        public double TuningCents => tuning.Cents;

        private LiveSession? session;

        /// <summary>
        /// The microphone being opened off the game's thread, or null (owner
        /// 2026-10-23: with the disk saturated, a restart froze 5 s opening it).
        /// </summary>
        private System.Threading.Tasks.Task<bool>? opening;

        private LiveSession? openingSession;

        private string openingAs = string.Empty;

        private long openingSince;

        /// <summary>Sessions being closed, off the game's thread: a play's recording is kept once they are (their thread writes to it until then).</summary>
        private System.Threading.Tasks.Task closing = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>
        /// A microphone that gives no samples this long has stopped (it gives
        /// them all the time, silence included): it is opened again (owner
        /// 2026-10-23: it stopped 1.6 s into a play and the level stood still).
        /// </summary>
        private const double mic_stall_ms = 1000;

        private bool micReopened;

        /// <summary>The describer listening now (null while not listening).</summary>
        internal LiveSession? Session => session;

        private double rate = 1;

        private bool filePacedByLevel;

        private int replayCursor;

        private double replayLastTime = double.NegativeInfinity;

        /// <summary>Where the level was when listening started.</summary>
        private double listenedFrom;

        /// <summary>Real time since listening started (what heard times are placed by).</summary>
        private readonly System.Diagnostics.Stopwatch listeningFor = new System.Diagnostics.Stopwatch();

        /// <summary>A level drifting this far from the listening's real time has jumped.</summary>
        private const double jump_ms = 400;

        private readonly System.Collections.Generic.Dictionary<uint, int> heardIndex = new System.Collections.Generic.Dictionary<uint, int>();

        private uint sessionBase;

        private double anchorMs;

        private double micDelayMs;

        private void startListening(double now)
        {
            string? file = Environment.GetEnvironmentVariable(INPUT_ENV);
            ILiveSource source;

            if (!string.IsNullOrEmpty(file))
            {
                double.TryParse(Environment.GetEnvironmentVariable(INPUT_OFFSET_ENV), NumberStyles.Float, CultureInfo.InvariantCulture, out double fileStart);
                anchorMs = fileStart;
                micDelayMs = 0; // a file has no microphone round trip

                if (Environment.GetEnvironmentVariable(INPUT_REALTIME_ENV) == @"1")
                {
                    // Paced like a player: real time, whatever the level's clock does.
                    // Like a microphone, it is heard from now on: the file from where
                    // the level is, its first sample placed at the level's now.
                    double startAt = clockMs - fileStart;
                    source = new FileSource(file, () => startAt + listeningFor.Elapsed.TotalMilliseconds, startAt);
                    anchorMs = clockMs;
                    filePacedByLevel = false;
                }
                else
                {
                    source = new FileSource(file, () => clockMs - fileStart);
                    filePacedByLevel = true;
                }
            }
            else if (Dev.KanDevPlay.Enabled)
            {
                // Developer runs never open the microphone: a test instance must
                // not hear the person at the computer. No input = silence.
                failed = true;
                Status.Value = @"Dev play without KAN_LIVE_INPUT: not listening.";
                return;
            }
            else
            {
                source = new MicSource();
                micPlay = true;
            }

            // Rate mods: the player's time runs at the real pace, the level's at the rate.
            rate = gameplayClock!.GetTrueGameplayRate();
            if (!(rate > 0) || filePacedByLevel) rate = 1; // a file paced by the level's clock is already in its time

            // The player's instrument as it is now (it can change in the player loader), else the level's.
            string listenAs = Configuration.KanPlayer.Instrument ?? instrument;
            // A live play records what it hears, for its replay (its id rides in the replay).
            if (take == null && Recorder != null)
            {
                take = new PlayTake();
                Recorder.Add(new KanReplayFrame(now, KanFactKind.Take, take.Id));
            }

            var s = new LiveSession(source, listenAs) { Take = take };

            if (micPlay)
            {
                // A microphone may take a while to open: off the game's thread, heard from when it is open.
                openingSession = s;
                openingAs = listenAs;
                openingSince = System.Diagnostics.Stopwatch.GetTimestamp();
                opening = System.Threading.Tasks.Task.Run(s.Open);
                return;
            }

            if (!s.Open())
            {
                notListening(s.Error);
                return;
            }

            begin(s, now, listenAs);
        }

        /// <summary>The microphone opened (or not): heard from now.</summary>
        private void finishOpening(double now)
        {
            var s = openingSession!;
            bool opened = opening!.IsCompletedSuccessfully && opening.Result;
            opening = null;
            openingSession = null;

            if (!opened)
            {
                closing = closing.ContinueWith(_ => s.Dispose());
                notListening(s.Error.Length > 0 ? s.Error : @"the microphone could not open");
                return;
            }

            // The take's first sample is "now" — as in the mic calibration, whose
            // measured round trip (micDelayMs) covers everything after it.
            anchorMs = now;
            begin(s, now, openingAs);
        }

        private void notListening(string error)
        {
            failed = true;
            bloomTarget = 1; // nothing to judge by: never muffle the music for it
            Status.Value = $"Not listening: {error}.";
            osu.Framework.Logging.Logger.Log($"kan!: live input failed: {error}", level: osu.Framework.Logging.LogLevel.Important);
        }

        /// <summary>Start hearing an open session: its first sample is now.</summary>
        private void begin(LiveSession s, double now, string listenAs)
        {
            // Real time starts with the stream: its first sample is real time 0.
            listeningFor.Restart();
            s.Begin();
            osu.Framework.Logging.Logger.Log($"kan!: listening as {listenAs}");

            session = s;
            listenedFrom = now;
            // A recording plays as it was recorded: no player's habit to take off.
            timeline.Begin(anchorMs, rate, micDelayMs, micPlay ? ForgeTiming.PLAYER_TIMING_MS.Value : 0, filePacedByLevel);

            Trace ??= KanPlayTrace.Start(new[]
            {
                $"kan! play trace {DateTime.Now:yyyy-MM-dd HH:mm:ss}; {Notes.Count} notes; instrument {listenAs}; mic delay {micDelayMs:0} ms; player timing {timeline.PlayerTimingMs:+0;-0;0} ms; rate {rate:0.##}; wait for the player {WaitForPlayer}; source {(micPlay ? "microphone" : "file")}",
                "rows: listen,anchor_ms | hit,heard,stream_ms,placed_ms,stamp_ms | pitch,heard,stage,raw_midi,corrected_midi,cents | unresolved,heard,stage | read,stream_ms,placed_ms,midi,level",
                "      verdict,note,start_ms,midi,result,reason,offset_ms,played_midi,heard (empty: timed by readings),late_ms (verdict after the note's start)",
                "      frame,update_ms,draw_ms,gc_ms | clock,level_minus_real_ms | wait,note | release,note",
                "real_ms: since listening (re)started; level_ms: the level's clock when the row was written",
            });
            Trace?.Row(TraceRealMs, now, "listen", anchorMs);
            sessionBase += 1u << 24; // note ids restart with each session

            if (micReopened)
            {
                micReopened = false;
                const string reopened = @"The microphone stopped for a moment: it was opened again.";
                Status.Value = reopened;
                Scheduler.AddDelayed(() =>
                {
                    if (Status.Value == reopened) Status.Value = string.Empty;
                }, 4000);
            }
            else
                Status.Value = string.Empty;
        }

        /// <summary>Stop hearing: the session (or the one opening) is closed off the game's thread.</summary>
        private void stopListening()
        {
            if (opening != null)
            {
                var abandoned = openingSession!;
                closing = System.Threading.Tasks.Task.WhenAll(closing, opening).ContinueWith(_ => abandoned.Dispose());
                opening = null;
                openingSession = null;
            }

            if (session is { } s)
            {
                session = null;
                closing = closing.ContinueWith(_ => s.Dispose());
            }
        }

        private void takeFacts()
        {
            double now = clockMs;

            while (session!.TryTake(out var f))
            {
                uint id = sessionBase + f.NoteId;

                switch (f.Kind)
                {
                    case KanForgeLiveEvent.HIT:
                    {
                        int index = heardIndex.Count;
                        heardIndex[id] = index;
                        double at = toGameplay(f.OnsetMs);
                        hitsHeard++;
                        if (at > now) hitsInFuture++;
                        Trace?.Row(TraceRealMs, now, "hit", index, f.OnsetMs, at, Math.Min(at, now));
                        apply(new KanReplayFrame(now, KanFactKind.Hit, index, delayMs: delay(now, at)));
                        break;
                    }

                    case KanForgeLiveEvent.PITCH:
                    {
                        if (!heardIndex.TryGetValue(id, out int index)) break;

                        bool final = f.Stage != KanForgeLiveEvent.PROVISIONAL;

                        // "Nothing measurable" comes as its own stage, or as a pitch of 0.
                        if (f.Stage == KanForgeLiveEvent.UNRESOLVED || f.Midi <= 0)
                        {
                            Trace?.Row(TraceRealMs, now, "unresolved", index, final ? "final" : "provisional");
                            apply(new KanReplayFrame(now, final ? KanFactKind.UnresolvedFinal : KanFactKind.UnresolvedProvisional, index));
                        }
                        else
                        {
                            // The instrument's tuning off first: a note a little sharp on a
                            // sharp instrument is the right note (not the next one up).
                            float pitch = tuning.Correct(MathF.Round(f.Midi) + f.Cents / 100);
                            float midi = MathF.Round(pitch);
                            Trace?.Row(TraceRealMs, now, "pitch", index, f.Stage == KanForgeLiveEvent.FINAL ? "final" : "provisional", f.Midi + f.Cents / 100, midi, (pitch - midi) * 100);
                            apply(new KanReplayFrame(now, f.Stage == KanForgeLiveEvent.FINAL ? KanFactKind.PitchFinal : KanFactKind.PitchProvisional, index,
                                midi, (pitch - midi) * 100));
                        }

                        break;
                    }

                    case KanForgeLiveEvent.FRAME:
                    {
                        double at = toGameplay(f.TMs);
                        learnTuning(f.Midi, at);
                        Trace?.Row(TraceRealMs, now, "read", f.TMs, at, tuning.Correct(f.Midi), f.Level);
                        apply(new KanReplayFrame(now, KanFactKind.Reading, midi: tuning.Correct(f.Midi), level: f.Level, delayMs: delay(now, at)));
                        break;
                    }
                }
            }

            static int delay(double now, double at) => (int)Math.Clamp(Math.Round(now - at), 0, 99999);
        }

        /// <summary>One heard fact: to the judge and the trail, and into the replay when recording.</summary>
        private void apply(KanReplayFrame f, bool record = true)
        {
            // The pause menu is open: what is played is not for the level — not
            // for the judge, not for the wait (owner 2026-10-17: the song went
            // on as they played, under the pause menu).
            if (UserPaused) return;

            if (record) Recorder?.Add(f);

            if (record && Dev.KanDevPlay.Enabled && f.Kind is KanFactKind.Hit or KanFactKind.PitchProvisional or KanFactKind.PitchFinal)
                osu.Framework.Logging.Logger.Log($"kan! dev fact {f.Kind} #{f.Index} at {f.FactTime:0} midi {f.Midi:0.0} (level {clockMs:0}, waiting {waitingFor?.Index.ToString() ?? "-"}, user paused {UserPaused})");

            uint id = (uint)f.Index;

            switch (f.Kind)
            {
                case KanFactKind.Hit:
                    // An attack while the level waits (or picks up) belongs to the note
                    // waited for: it must not count for the next one (2026-10-15: the
                    // note after a wait passed without being played).
                    Judge.Heard(id, waitingFor != null || resuming ? double.NegativeInfinity : f.FactTime);
                    onsetOf[f.Index] = f.FactTime;
                    lastOnsetMs = waitingFor != null ? clockMs : f.FactTime;
                    wrongHeldReadings = 0;
                    break;

                case KanFactKind.PitchProvisional:
                case KanFactKind.PitchFinal:
                    Judge.Pitch(id, (int)f.Midi, f.Cents, f.Kind == KanFactKind.PitchFinal);
                    break;

                case KanFactKind.UnresolvedProvisional:
                case KanFactKind.UnresolvedFinal:
                    Judge.Unresolved(id, f.Kind == KanFactKind.UnresolvedFinal);
                    break;

                case KanFactKind.Reading:
                    live[liveHead] = new LivePoint(RealNowMs, f.Midi, f.Level);
                    liveHead = (liveHead + 1) % live.Length;
                    LiveCount = Math.Min(LiveCount + 1, live.Length);

                    Judge.Frame(f.FactTime, f.Midi);
                    Trail[trailHead] = new TrailPoint(f.FactTime, f.Midi, f.Level);
                    trailHead = (trailHead + 1) % Trail.Length;
                    TrailCount = Math.Min(TrailCount + 1, Trail.Length);
                    break;
            }

            if (waitingFor != null)
                checkWaitRelease(f);
            else if (WaitForPlayer)
                checkWrongInGrace(f);
        }

        private int tuneCursor;

        /// <summary>A raw reading while a score note sounds teaches the tuning (unless the player set it by hand).</summary>
        private void learnTuning(float rawMidi, double atMs)
        {
            if (!ForgeTiming.AUTO_TUNING.Value || rawMidi <= 0) return;

            // A note waited for counts too: its reading is what is being played for it.
            var n = waitingFor ?? soundingNote(atMs);
            if (n == null || n.PitchUnknown) return;

            tuning.Observe(rawMidi, n.Midi);
        }

        /// <summary>The score note sounding at a time, past its attack (readings at an attack are unsteady).</summary>
        private KanHitObject? soundingNote(double atMs)
        {
            while (tuneCursor > 0 && Notes[tuneCursor - 1].StartTime > atMs) tuneCursor--;
            while (tuneCursor < Notes.Count && Notes[tuneCursor].EndMs < atMs) tuneCursor++;

            for (int i = tuneCursor; i < Notes.Count && Notes[i].StartTime <= atMs; i++)
            {
                if (atMs >= Notes[i].StartTime + 60 && atMs <= Notes[i].EndMs)
                    return Notes[i];
            }

            return null;
        }

        /// <summary>A replay: the facts that reached the judge by now, in order.</summary>
        private void feedReplay(Replay replay, double now)
        {
            if (now < replayLastTime - 500)
            {
                // Seeked back: hear it again from the start.
                replayCursor = 0;
                Judge.Forget(double.MaxValue);
                TrailCount = 0;
            }

            replayLastTime = now;
            var frames = replay.Frames;

            while (replayCursor < frames.Count && frames[replayCursor].Time <= now)
            {
                if (frames[replayCursor] is KanReplayFrame { Kind: not KanFactKind.Take } f)
                    apply(f, record: false);
                replayCursor++;
            }
        }

        /// <summary>Whether this play listens to the microphone (not a recording, not a replay).</summary>
        private bool micPlay;

        /// <summary>
        /// A stream time (real ms since listening started) on the level's timeline
        /// (<see cref="ListeningTimeline"/>). While waiting, everything lands where the level waits.
        /// </summary>
        private double toGameplay(float streamMs)
        {
            if (waitingFor != null) return waitingFor.StartTime;
            if (resuming) return resumeAt;

            return timeline.Place(streamMs);
        }

        /// <summary>Where heard moments fall on the level (owner 2026-10-21: a fixed anchor drifted from the clock).</summary>
        private readonly ListeningTimeline timeline = new ListeningTimeline();

        /// <summary>
        /// Keep the play's recording (in the background: it writes the file),
        /// with the play's score: when the level ends, so its replay — even
        /// watched straight from the results — finds it; else when the play goes.
        /// </summary>
        private void saveTake()
        {
            var kept = take;
            take = null;

            if (kept == null || Recorder == null || KanLevelFiles.ForgeRoot is not { } root) return;

            long score = scoreProcessor?.TotalScore.Value ?? Recorder.Target.ScoreInfo.TotalScore;
            Guid set = SetId;

            // Once the sessions that write to it are closed.
            closing.ContinueWith(_ =>
            {
                try
                {
                    kept.Save(root, set, score);
                }
                catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
                {
                    osu.Framework.Logging.Logger.Log($"kan!: the play's recording could not be kept: {e.Message}");
                }
            });
        }
    }
}
