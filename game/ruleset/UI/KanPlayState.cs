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
using Kan.Forge.Native;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;
using Kan.Ruleset.Replays;

namespace Kan.Ruleset.UI
{
    /// <summary>
    /// What is happening in a play: the live session listening to the player,
    /// the judge pairing what it hears with the score, the trail of the
    /// player's pitch, and the "bloom" — how open the music is (it opens up as
    /// the player plays well and closes a little on misses; it never stops).
    /// Shared by the judging objects and the staff.
    /// </summary>
    public partial class KanPlayState : Component
    {
        /// <summary>Test without a microphone: a recording played as the player (KAN_LIVE_INPUT=path).</summary>
        public const string INPUT_ENV = @"KAN_LIVE_INPUT";

        /// <summary>Where that recording starts on the level's timeline, in ms (KAN_LIVE_INPUT_OFFSET_MS).</summary>
        public const string INPUT_OFFSET_ENV = @"KAN_LIVE_INPUT_OFFSET_MS";

        /// <summary>KAN_LIVE_INPUT_REALTIME=1: the recording plays in real time, as a player would (rate and wait modes).</summary>
        public const string INPUT_REALTIME_ENV = @"KAN_LIVE_INPUT_REALTIME";

        public readonly LiveJudge Judge = new LiveJudge();

        public readonly IReadOnlyList<KanHitObject> Notes;

        /// <summary>Each note's verdict once judged (by <see cref="KanHitObject.Index"/>).</summary>
        public readonly LiveVerdict?[] Verdicts;

        /// <summary>A note was judged: its index and verdict.</summary>
        public event Action<int, LiveVerdict>? Judged;

        /// <summary>A note's verdict was taken back (a replay rewound past it): its index.</summary>
        public event Action<int>? Unjudged;

        /// <summary>The player's pitch, newest last: gameplay time, fractional MIDI (0 = none), level.</summary>
        public readonly TrailPoint[] Trail = new TrailPoint[400];

        public int TrailCount { get; private set; }

        private int trailHead;

        /// <summary>How open the music is, 0..1 (smoothed).</summary>
        public double Bloom { get; private set; } = start_bloom;

        public int Streak { get; private set; }

        public readonly Bindable<string> Status = new Bindable<string>(string.Empty);

        private const double start_bloom = 0.4;
        private double bloomTarget = start_bloom;

        private readonly string instrument;

        /// <summary>"Wait for me" (<see cref="Mods.KanModWait"/>): the level stops at each note until it is played.</summary>
        public bool WaitForPlayer { get; init; }

        /// <summary>"Hidden": notes fade out before they reach the line.</summary>
        public bool HideNotes { get; init; }

        /// <summary>The level's design (sections, palette, effects): the level's own, or generated from its notes.</summary>
        public Kan.Forge.Design.KanDesign Design { get; init; } = new Kan.Forge.Design.KanDesign();

        /// <summary>The level's bars, beats and parts of a beat (in order; <see cref="Kan.Forge.Score.Bars"/>): the staff marks them.</summary>
        public IReadOnlyList<Kan.Forge.Score.Bars.Tick> Ticks { get; init; } = Array.Empty<Kan.Forge.Score.Bars.Tick>();

        /// <summary>A replay (or autoplay) to read the facts from instead of listening.</summary>
        public Func<Replay?>? ReplaySource { get; set; }

        /// <summary>Where a live play records what it heard (its replay).</summary>
        public KanReplayRecorder? Recorder { get; set; }

        /// <summary>The level's set: a play's recording is kept for it (<see cref="PlayTake"/>).</summary>
        public Guid SetId { get; init; }

        /// <summary>What the player played, kept with the play to be heard in its replay (owner 2026-10-22).</summary>
        private PlayTake? take;

        /// <summary>The level is stopped, waiting for the player (wait mode).</summary>
        public bool Waiting => waitingFor != null;

        /// <summary>The note the level waits for (wait mode), or null.</summary>
        public KanHitObject? WaitingFor => waitingFor;

        /// <summary>Where the level stood still (gameplay ms; meaningful while <see cref="Waiting"/>).</summary>
        public double WaitStopTime { get; private set; }

        /// <summary>
        /// Where the level picks up once the waited note is played (gameplay
        /// ms): the note's start plus the detection's moment. The sound held
        /// while waiting is the music at this point, so the song re-engages
        /// from exactly what was being held (owner 2026-10-15).
        /// </summary>
        public double WaitResumeTime { get; private set; }

        /// <summary>
        /// Wait mode: the level runs this far past a note before it stops
        /// (owner 2026-10-15). A note played on time is heard within it, so
        /// the level never stutters for the detection's own delay.
        /// </summary>
        public const double WAIT_GRACE_MS = 200;

        /// <summary>Wait mode: a wrong note heard while waiting (its MIDI number), shown at once.</summary>
        public event Action<int>? WrongWhileWaiting;

        /// <summary>
        /// Wait mode: notes played wrong before right — a miss at once, when the
        /// wrong note is confirmed (owner 2026-10-15: they break the combo;
        /// 2026-10-17: at once, not when the right note is finally found).
        /// </summary>
        private readonly System.Collections.Generic.Dictionary<int, LiveVerdict> wrongTry = new System.Collections.Generic.Dictionary<int, LiveVerdict>();

        private readonly System.Collections.Generic.Dictionary<int, int[]> expectedAround = new System.Collections.Generic.Dictionary<int, int[]>();

        /// <summary>
        /// The notes the score has around a note — the previous one, a chord's
        /// others, the next ones within the window: played, they are not wrong
        /// tries at it (<see cref="LiveJudge.IsWrongTry"/>).
        /// </summary>
        public IReadOnlyCollection<int> ExpectedAround(KanHitObject n)
        {
            if (expectedAround.TryGetValue(n.Index, out int[]? known)) return known;

            var around = new System.Collections.Generic.List<int>();
            if (n.Index > 0 && !Notes[n.Index - 1].PitchUnknown) around.Add(Notes[n.Index - 1].Midi);

            double reach = LiveJudge.WindowMs(LiveResult.Meh, n.PaceMs) * Judge.WindowScale;

            for (int i = n.Index - 1; i >= 0 && Notes[i].StartTime >= n.StartTime - reach; i--)
                if (!Notes[i].PitchUnknown) around.Add(Notes[i].Midi);

            for (int i = n.Index + 1; i < Notes.Count && Notes[i].StartTime <= n.StartTime + reach; i++)
                if (!Notes[i].PitchUnknown) around.Add(Notes[i].Midi);

            return expectedAround[n.Index] = around.Distinct().ToArray();
        }

        /// <summary>When the score's previous note (not a chord mate) starts: the judge's window reaches back at most halfway to it.</summary>
        public double PreviousStart(KanHitObject n)
        {
            for (int i = n.Index - 1; i >= 0; i--)
                if (Notes[i].StartTime < n.StartTime) return Notes[i].StartTime;

            return double.NegativeInfinity;
        }

        /// <summary>When the score's next note (not a chord mate) starts: the judge's window reaches on at most halfway to it.</summary>
        public double NextStart(KanHitObject n)
        {
            for (int i = n.Index + 1; i < Notes.Count; i++)
                if (Notes[i].StartTime > n.StartTime) return Notes[i].StartTime;

            return double.PositiveInfinity;
        }

        /// <summary>Whether the judge heard a note played for <paramref name="n"/>, within its window.</summary>
        private bool heard(KanHitObject n) => Judge.HasHeard(n.StartTime, n.Midi, n.PitchUnknown, n.PaceMs, PreviousStart(n), NextStart(n));

        /// <summary>A wrong try heard for a note (wait mode): the drawable applies it at once.</summary>
        public LiveVerdict? WrongTry(KanHitObject note) => wrongTry.TryGetValue(note.Index, out var v) ? v : null;

        private void noteWrongTry(KanHitObject n, int played, double atMs)
        {
            if (!wrongTry.TryAdd(n.Index, new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, atMs - n.StartTime, played, 0))) return;

            WrongWhileWaiting?.Invoke(played);
        }

        /// <summary>The last attack heard (gameplay ms) and the wrong pitch held since it (readings in a row).</summary>
        private double lastOnsetMs = double.NegativeInfinity;

        private int wrongHeldMidi, wrongHeldReadings;

        /// <summary>
        /// Wait mode: a wrong pitch held since an attack made for the note is a
        /// wrong try, without waiting for libkan's final pitch — that comes
        /// when the note ends, i.e. when the right note is found (owner 2026-10-17).
        /// </summary>
        private void trackWrongPitch(KanHitObject n, KanReplayFrame f)
        {
            int played = f.Midi > 0 ? (int)Math.Round(f.Midi) : 0;

            if (n.PitchUnknown || lastOnsetMs < n.StartTime - LiveJudge.TRY_BEFORE_MS || LiveJudge.Accepts(f.Midi, n.Midi)
                || !LiveJudge.IsWrongTry(played, n.Midi, ExpectedAround(n)))
            {
                wrongHeldReadings = 0;
                return;
            }

            wrongHeldReadings = played == wrongHeldMidi ? wrongHeldReadings + 1 : 1;
            wrongHeldMidi = played;

            if (wrongHeldReadings >= LiveJudge.WRONG_HOLD_READINGS)
                noteWrongTry(n, played, lastOnsetMs);
        }

        /// <summary>When each heard note started (gameplay ms): a pitch counts as a try only for a note started for it.</summary>
        private readonly System.Collections.Generic.Dictionary<int, double> onsetOf = new System.Collections.Generic.Dictionary<int, double>();

        /// <summary>Whether a heard note started late enough to be a try at <paramref name="n"/> (not the previous note still sounding).</summary>
        private bool isTryAt(KanReplayFrame pitch, KanHitObject n)
            => onsetOf.TryGetValue(pitch.Index, out double onset) && onset >= n.StartTime - LiveJudge.TRY_BEFORE_MS;

        /// <summary>One reading of the player's pitch, stamped with real time (it keeps coming while the level stands still).</summary>
        public readonly record struct LivePoint(double RealMs, float Midi, float Level);

        private readonly LivePoint[] live = new LivePoint[256];
        private int liveHead;

        /// <summary>How many live readings are kept (newest first through <see cref="LiveAt"/>).</summary>
        public int LiveCount { get; private set; }

        /// <summary>The i-th newest live reading (0 = newest).</summary>
        public LivePoint LiveAt(int i) => live[(liveHead - 1 - i + live.Length * 2) % live.Length];

        private readonly System.Diagnostics.Stopwatch realClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>The player's instrument tuning, taken off everything heard live (owner 2026-10-15).</summary>
        private TuningTracker tuning = new TuningTracker();

        /// <summary>The instrument's tuning in use (cents from A440).</summary>
        public double TuningCents => tuning.Cents;

        /// <summary>Real time (ms) for <see cref="LivePoint.RealMs"/>.</summary>
        public double RealNowMs => realClock.Elapsed.TotalMilliseconds;

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

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.UI.IFrameStableClock? frameStableClock { get; set; }

        /// <summary>Where the level was when listening started.</summary>
        private double listenedFrom;

        /// <summary>Real time since listening started (what heard times are placed by).</summary>
        private readonly System.Diagnostics.Stopwatch listeningFor = new System.Diagnostics.Stopwatch();

        // Timing diagnostics for the play's summary line.
        private double lagSum, lagMin = double.MaxValue, lagMax = double.MinValue;
        private int lagFrames, hitsHeard, hitsInFuture;

        /// <summary>The play, note by note (<see cref="KanPlayTrace"/>); null in replays.</summary>
        internal KanPlayTrace? Trace { get; private set; }

        internal double TraceRealMs => listeningFor.Elapsed.TotalMilliseconds;

        private long lastLagRow;

        /// <summary>A level drifting this far from the listening's real time has jumped.</summary>
        private const double jump_ms = 400;
        private readonly System.Collections.Generic.Dictionary<uint, int> heardIndex = new System.Collections.Generic.Dictionary<uint, int>();
        private KanHitObject? waitingFor;
        private float frozenStreamMs;
        private GameplayClockContainer? clockContainer;
        private uint sessionBase;
        private double anchorMs;
        private double micDelayMs;
        private volatile float clockMs;

        /// <summary>Where the level is (ms), as of the last frame.</summary>
        internal float ClockMs => clockMs;
        private bool failed;
        private long lastHeartbeat;
        private int judgedCount;
        private bool summaryLogged;

        [Resolved(CanBeNull = true)]
        private IGameplayClock? gameplayClock { get; set; }

        public KanPlayState(IReadOnlyList<KanHitObject> notes, string instrument)
        {
            Notes = notes;
            Verdicts = new LiveVerdict?[notes.Count];
            this.instrument = instrument;
        }

        public readonly record struct TrailPoint(double TimeMs, float Midi, float Level);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (KanLevelFiles.ForgeRoot is { } root)
                ForgeTiming.Load(root);

            micDelayMs = ForgeTiming.MIC_DELAY_MS.Value;
            tuning = new TuningTracker(ForgeTiming.TUNING_CENTS.Value);
            gameplayClock?.IsPaused.BindValueChanged(p =>
            {
                // Stopped by the wait mode: keep listening, that is what it waits for.
                if (p.NewValue && waitingFor == null) stopListening();
            });

            clockContainer = this.FindClosestParent<GameplayClockContainer>();
            scoreProcessor?.HasCompleted.BindValueChanged(c =>
            {
                if (c.NewValue) saveTake();
            });
        }

        /// <summary>
        /// Wait mode: whether the wait still takes care of this note (it has
        /// not reached it, or waits for it now). Only then is a judge's miss
        /// held back; a note the wait has left behind takes its verdict, so
        /// every note is decided and the level ends (2026-10-15: the results
        /// never showed, 322 of 325 notes judged).
        /// </summary>
        public bool WaitPending(KanHitObject note)
            => WaitForPlayer && (ReferenceEquals(note, waitingFor) || note.Index >= nextWait); // Notes are in index order

        /// <summary>
        /// Called by a note's judging object when the game takes its result
        /// back (a replay rewound before it was decided): the note is
        /// undecided again, so it looks and plays as it did the first time
        /// (owner 2026-10-22: after a rewind the notes' effects never came back).
        /// </summary>
        public void Unreport(KanHitObject note)
        {
            if (Verdicts[note.Index] == null) return;

            Verdicts[note.Index] = null;
            wrongTry.Remove(note.Index);
            judgedCount--;
            Unjudged?.Invoke(note.Index);
        }

        /// <summary>
        /// Whether an attack played at <paramref name="now"/> would be a perfect
        /// for <paramref name="note"/>: placed as the judge places what it hears
        /// (with the player's timing habit), graded on the note's own window.
        /// The staff lights the target with it (owner 2026-10-22).
        /// </summary>
        public bool PerfectNow(KanHitObject note, double now)
            => Judge.WithinPerfect(PlacedAt(now) - note.StartTime, note.PaceMs, note.StartTime, PreviousStart(note), NextStart(note));

        /// <summary>Where on the level an attack played at <paramref name="now"/> is placed (the player's timing habit included, as the judge places it).</summary>
        public double PlacedAt(double now) => now + (micPlay ? -ForgeTiming.PLAYER_TIMING_MS.Value * (rate > 0 ? rate : 1) : 0);

        /// <summary>Called by a note's judging object once the judge decided it.</summary>
        public void Report(KanHitObject note, LiveVerdict verdict)
        {
            Verdicts[note.Index] = verdict;

            switch (verdict.Result)
            {
                case LiveResult.Perfect:
                case LiveResult.Good:
                    Streak++;
                    bloomTarget += 0.07;
                    break;

                case LiveResult.Meh:
                    Streak++;
                    bloomTarget += 0.02;
                    break;

                default:
                    Streak = 0;
                    bloomTarget -= 0.18;
                    break;
            }

            bloomTarget = Math.Clamp(bloomTarget, 0.15, 1);
            Trace?.Row(TraceRealMs, clockMs, "verdict", note.Index, note.StartTime, note.Midi, verdict.Result, verdict.Reason, verdict.OffsetMs, verdict.PlayedMidi,
                verdict.PlayedId, clockMs - note.StartTime);
            Judged?.Invoke(note.Index, verdict);

            if (Dev.KanDevPlay.Enabled)
                osu.Framework.Logging.Logger.Log($"kan! dev verdict {note.Index} at {note.StartTime:0} midi {note.Midi}: {verdict.Result} {verdict.Reason} offset {verdict.OffsetMs:0} played {verdict.PlayedMidi}");

            if (++judgedCount == Notes.Count)
                logSummary();
        }

        protected override void Update()
        {
            base.Update();

            // Developer tooling: a heartbeat in the log while playing unattended.
            if (Dev.KanDevPlay.Enabled && Environment.TickCount64 - lastHeartbeat > 5000)
            {
                lastHeartbeat = Environment.TickCount64;
                int judged = 0;
                foreach (var v in Verdicts) if (v != null) judged++;
                osu.Framework.Logging.Logger.Log($"kan! dev play: gameplay clock {(gameplayClock == null ? "none" : $"{gameplayClock.CurrentTime:0} ms, running {gameplayClock.IsRunning}, paused {gameplayClock.IsPaused.Value}")}, own clock {Clock.CurrentTime:0}, judged {judged}/{Notes.Count}, "
                                                 + $"streak {Streak}, bloom {Bloom:0.00}, trail {TrailCount}, tuning {tuning.Cents:+0;-0;0}, listening {session != null}", level: osu.Framework.Logging.LogLevel.Important);
            }

            if (gameplayClock == null) return; // not in gameplay (nothing to listen to)

            double now = gameplayClock.CurrentTime;
            clockMs = (float)now;

            // The level jumped (Skip, a practice part starting later): what is heard
            // is placed by real time since listening started, so it must start over
            // from the new position. Seen as the level drifting from that real time
            // (however the jump happened: osu! may run the clock through the gap).
            if (frameStableClock?.IsCatchingUp.Value == true)
                return;

            if (session != null && !filePacedByLevel && waitingFor == null && !resuming && gameplayClock.IsRunning)
            {
                double expected = listenedFrom + (listeningFor.Elapsed.TotalMilliseconds - frozenStreamMs) * rate;

                // How far the level's clock sits from real time since listening began
                // (diagnostics: a hit is placed by that real time, so a gap moves every hit).
                lagSum += now - expected;
                lagMin = Math.Min(lagMin, now - expected);
                lagMax = Math.Max(lagMax, now - expected);
                lagFrames++;

                if (Environment.TickCount64 - lastLagRow > 1000)
                {
                    lastLagRow = Environment.TickCount64;
                    Trace?.Row(TraceRealMs, now, "clock", now - expected);
                }

                if (Math.Abs(now - expected) > jump_ms)
                {
                    osu.Framework.Logging.Logger.Log($"kan!: the level jumped to {now:0} ms (expected {expected:0}); listening again from there");
                    stopListening();
                }
            }

            if (ReplaySource?.Invoke() is { } replay)
                feedReplay(replay, now);
            else
            {
                if (opening != null)
                {
                    if (opening.IsCompleted)
                        finishOpening(now);
                    else if (Status.Value.Length == 0 && System.Diagnostics.Stopwatch.GetElapsedTime(openingSince).TotalMilliseconds > 1000)
                        Status.Value = @"Opening the microphone…";
                }
                else if (session == null && !failed && gameplayClock.IsRunning && !gameplayClock.IsPaused.Value)
                    startListening(now);

                if (session != null && micPlay && session.SilentForMs > mic_stall_ms)
                {
                    osu.Framework.Logging.Logger.Log($"kan!: the microphone gave nothing for {session.SilentForMs:0} ms; opening it again", level: osu.Framework.Logging.LogLevel.Important);
                    Trace?.Row(TraceRealMs, now, "reopen");
                    stopListening();
                    micReopened = true;
                    startListening(now);
                }

                if (session != null)
                {
                    remember(listeningFor.Elapsed.TotalMilliseconds, now);

                    // Where the level was as the take was heard: as the judge places it, without the player's habit.
                    if (waitingFor != null || resuming)
                        take?.Break();
                    else
                        take?.Mark(now, listeningFor.Elapsed.TotalMilliseconds, micDelayMs);
                    takeFacts();
                }
            }

            if (WaitForPlayer)
            {
                // Resumed from the pause menu while waiting: the level keeps waiting
                // (owner 2026-10-15: after "continue" it ran on without the player).
                if (waitingFor != null && gameplayClock.IsRunning)
                    clockContainer?.Stop();

                settleResume(now);
                updateWait(now);
            }

            Judge.Forget(now - 5000);


            // Ease towards the target: blooming is felt over a phrase, not a note.
            Bloom += (bloomTarget - Bloom) * Math.Min(1, Math.Abs(Time.Elapsed) / 900);
        }

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
            frozenStreamMs = 0;

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
            seenCount = 0;
            playerTimingMs = micPlay ? ForgeTiming.PLAYER_TIMING_MS.Value : 0; // a recording plays as it was recorded

            Trace ??= KanPlayTrace.Start(new[]
            {
                $"kan! play trace {DateTime.Now:yyyy-MM-dd HH:mm:ss}; {Notes.Count} notes; instrument {listenAs}; mic delay {micDelayMs:0} ms; player timing {playerTimingMs:+0;-0;0} ms; rate {rate:0.##}; wait for the player {WaitForPlayer}; source {(micPlay ? "microphone" : "file")}",
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

        /// <summary>Wait mode: a wrong note around the next note's start (before the level stops) counts as a wrong try too.</summary>
        private void checkWrongInGrace(KanReplayFrame f)
        {
            if (nextWait >= Notes.Count) return;

            var n = Notes[nextWait];

            if (f.Kind == KanFactKind.Reading)
            {
                if (clockMs >= n.StartTime - LiveJudge.TRY_BEFORE_MS && clockMs <= n.StartTime + WAIT_GRACE_MS)
                    trackWrongPitch(n, f);
                return;
            }

            if (f.Kind != KanFactKind.PitchFinal) return;

            if (n.PitchUnknown || LiveJudge.Accepts(f.Midi + f.Cents / 100, n.Midi) || !LiveJudge.IsWrongTry((int)f.Midi, n.Midi, ExpectedAround(n))) return;
            if (clockMs > n.StartTime + WAIT_GRACE_MS || !isTryAt(f, n)) return;

            noteWrongTry(n, (int)f.Midi, onsetOf.GetValueOrDefault(f.Index, clockMs));
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

        // ------------------------------------------------------------------
        // Wait mode: the level stops at a note until the player plays it
        // ------------------------------------------------------------------

        private void updateWait(double now)
        {
            if (waitingFor != null || resuming || clockContainer == null) return;

            // The next note to wait for, in order: skip notes already decided or heard.
            // A note missed for a wrong try is still waited for: the miss is the
            // penalty, finding the right note is still the walk (owner 2026-10-17).
            while (nextWait < Notes.Count && (Verdicts[Notes[nextWait].Index] != null && !wrongTry.ContainsKey(Notes[nextWait].Index) || Notes[nextWait].StartTime <= now
                                                                                     && heard(Notes[nextWait])))
                nextWait++;

            if (nextWait >= Notes.Count) return;

            var n = Notes[nextWait];

            // A little past the note before stopping: the detection takes a moment.
            if (n.StartTime + WAIT_GRACE_MS > now) return;

            // Already sounding right (held over from before, or slurred into), or its
            // pitch came and went without an attack of its own: played.
            if (!needsAttack(n) && (soundingRight(n) || (!n.PitchUnknown && Judge.PitchSounded(n.StartTime, n.Midi, now))))
            {
                markPlayed(n, 0);
                nextWait++;
                return;
            }

            nextWait++;
            waitingFor = n;
            WaitStopTime = now;
            Trace?.Row(TraceRealMs, now, "wait", n.Index);
            WaitResumeTime = Math.Min(now, n.StartTime + (release_lead_ms + micDelayMs) * rate);
            rightReadings = 0;
            waitingClock.Restart();
            clockContainer.Stop();
            // No "waiting" banner: the wait overlay says what to play, at the line.
        }

        private int nextWait;

        /// <summary>
        /// A note at the same pitch as the one before it (not tied): it is played
        /// by a new attack only — holding the previous note on is not playing it.
        /// A score whose previous note still sounds when this one starts (an
        /// overlap, from a transcription) means a tie: nothing to attack again.
        /// </summary>
        private bool needsAttack(KanHitObject n)
        {
            if (n.PitchUnknown || n.Articulation == Kan.Forge.Score.Articulation.Legato || n.Index == 0) return false;

            var previous = Notes[n.Index - 1];
            return previous.Midi == n.Midi && previous.EndMs <= n.StartTime + 1;
        }

        /// <summary>Consecutive readings at the waited note's pitch.</summary>
        private int rightReadings;

        /// <summary>Readings at the right pitch for this long release the wait (no need to wait for a new note's pitch event).</summary>
        private const int right_readings_to_release = 5; // 10 ms apart

        private bool soundingRight(KanHitObject n)
        {
            if (LiveCount < right_readings_to_release) return false;

            for (int i = 0; i < right_readings_to_release; i++)
            {
                var p = LiveAt(i);
                if (RealNowMs - p.RealMs > 150 || p.Midi <= 0) return false;
                if (!n.PitchUnknown && !LiveJudge.Accepts(p.Midi, n.Midi)) return false;
            }

            return true;
        }

        /// <summary>Whether this play listens to the microphone (not a recording, not a replay).</summary>
        private bool micPlay;

        /// <summary>How late the notes of the last play on the microphone landed (real ms, median), with the player timing it was played with.</summary>
        public readonly record struct LastPlayTiming(double MedianMs, int Notes, double PlayerTimingMs)
        {
            public const int MIN_NOTES = 10;
        }

        /// <summary>The last play on the microphone (owner 2026-10-17: offer to correct the mic delay by it, as osu! does).</summary>
        public static LastPlayTiming? LastPlay { get; private set; }

        /// <summary>The player has the pause menu open (set by the wait overlay, which listens while the level stands still).</summary>
        public bool UserPaused { get; set; }

        /// <summary>Called from outside the stopped ruleset (the HUD) while the level waits: keep hearing the player.</summary>
        public void ListenWhileWaiting()
        {
            if (waitingFor != null && session != null)
                takeFacts();
        }

        /// <summary>How long the level has stood still (real time: the stream keeps running).</summary>
        private readonly System.Diagnostics.Stopwatch waitingClock = new System.Diagnostics.Stopwatch();

        private void checkWaitRelease(KanReplayFrame f)
        {
            var n = waitingFor!;

            if (f.Kind is KanFactKind.PitchProvisional or KanFactKind.PitchFinal)
            {
                int played = (int)f.Midi;

                // A repeated note needs an attack made for it, not the last one's pitch arriving late.
                if ((n.PitchUnknown || LiveJudge.Accepts(f.Midi + f.Cents / 100, n.Midi)) && (!needsAttack(n) || isTryAt(f, n)))
                {
                    release(n, f.Cents);
                    return;
                }

                // A wrong note — its confirmed pitch only, a gentle mod forgives a
                // provisional slip; an octave off is the detector's, not the
                // player's, and the previous note still sounding is a learner's
                // lag, not a try (owner 2026-10-17).
                if (f.Kind == KanFactKind.PitchFinal && LiveJudge.IsWrongTry(played, n.Midi, ExpectedAround(n)) && isTryAt(f, n))
                    noteWrongTry(n, played, onsetOf.GetValueOrDefault(f.Index, clockMs));

                return;
            }

            if (f.Kind != KanFactKind.Reading) return;

            trackWrongPitch(n, f);

            // The right pitch sounding is enough: readings come every 10 ms, a
            // note's pitch event later — and a held or slurred note has none.
            // A repeated note is not released by the previous one still sounding: it waits for an attack.
            if (needsAttack(n)) return;

            bool right = f.Midi > 0 && (n.PitchUnknown || LiveJudge.Accepts(f.Midi, n.Midi));
            rightReadings = right ? rightReadings + 1 : 0;

            if (rightReadings >= right_readings_to_release || (right && n.Articulation == Kan.Forge.Score.Articulation.Legato))
                release(n, (f.Midi - n.Midi) * 100);
        }

        private void release(KanHitObject n, float cents)
        {
            markPlayed(n, cents / 100);
            Trace?.Row(TraceRealMs, clockMs, "release", n.Index);
            waitingFor = null;
            resuming = true;
            Status.Value = string.Empty;

            // The player just played the note: the level picks up from the note's
            // start (plus the detection's moment), not from where it stood a
            // little past it — so the next notes come in time with the player,
            // and the waited note is heard with its own accompaniment.
            resumeAt = WaitResumeTime;
            clockContainer?.Seek(resumeAt);
            clockContainer?.Start();
        }

        /// <summary>
        /// How long after the player's attack the wait is released (real ms):
        /// the readings' pipeline plus five readings at the right pitch,
        /// measured 2026-10-15 (resuming 60 ms in left the next notes 53 ms
        /// early). The microphone's own delay comes on top.
        /// </summary>
        private const double release_lead_ms = 110;

        private double resumeAt;

        /// <summary>Released, but the level has not moved yet (the track takes a moment to start).</summary>
        private bool resuming;

        /// <summary>
        /// Once the level moves again, the time it stood still is what passed
        /// in real time minus what the level advanced — including the moment
        /// the track takes to start, which a stopwatch around Stop/Start
        /// misses (seen 2026-10-15: notes after a wait judged ~110 ms late).
        /// </summary>
        private void settleResume(double now)
        {
            if (!resuming || gameplayClock is not { IsRunning: true } || now <= resumeAt + 1) return;

            double real = waitingClock.Elapsed.TotalMilliseconds;
            frozenStreamMs += (float)(real - (now - WaitStopTime) / rate);
            resuming = false;
        }

        /// <summary>The note counts as played at its start (the level waited for it).</summary>
        private void markPlayed(KanHitObject n, float semitonesOff)
        {
            // Already decided (a wrong try): an onset for it would be left over for a neighbour to claim.
            if (Verdicts[n.Index] != null) return;

            Judge.Heard(uint.MaxValue - (uint)n.Index, n.StartTime);
            Judge.Pitch(uint.MaxValue - (uint)n.Index, n.Midi, Math.Clamp(semitonesOff * 100, -49, 49), final: true);
        }

        // ------------------------------------------------------------------
        // Wait mode, as seen: the level never shows that it ran past a note
        // ------------------------------------------------------------------

        private double displayed = double.NaN;

        /// <summary>How far past a note's start the staff may drift while the note is not heard yet (ms, asymptotic).</summary>
        private const double display_overrun_ms = 20;

        /// <summary>
        /// The time the staff shows (owner 2026-10-15: "don't tell the player so
        /// obviously that the level waited; it feels like a penalty"). In wait
        /// mode the level runs a little past a note before it stops and picks up
        /// a little before where it stopped; the staff instead glides to a halt
        /// with the note on the line, and only ever moves forward, easing back
        /// into step with the level. Call once per frame.
        /// </summary>
        public double DisplayTime(double now, double elapsedMs)
        {
            if (!WaitForPlayer) return now;

            double target = now;
            var held = waitingFor ?? (resuming || nextWait >= Notes.Count ? null : Notes[nextWait]);

            if (held != null && Verdicts[held.Index] == null && now > held.StartTime
                && (waitingFor != null || !heard(held)))
            {
                double over = (waitingFor != null ? WaitStopTime : now) - held.StartTime;
                target = held.StartTime + display_overrun_ms * (1 - Math.Exp(-over / display_overrun_ms));
            }

            // A real jump (a replay seek, a restart): follow at once.
            if (double.IsNaN(displayed) || Math.Abs(target - displayed) > 1000)
                return displayed = target;

            // Only forward, easing into step (a note played in the grace, or the level resuming).
            double eased = displayed + (target - displayed) * Math.Min(1, Math.Abs(elapsedMs) / 120);
            return displayed = Math.Max(displayed, eased);
        }

        /// <summary>The i-th newest trail point (0 = newest).</summary>
        public TrailPoint TrailAt(int i) => Trail[(trailHead - 1 - i + Trail.Length * 2) % Trail.Length];

        /// <summary>One line for the log: how the play went (diagnostics and bug reports).</summary>
        private void logSummary()
        {
            if (summaryLogged) return;

            summaryLogged = true;
            int judged = 0, perfect = 0, good = 0, meh = 0, notHeard = 0, wrong = 0, unclear = 0;
            double offsets = 0;
            var timings = new System.Collections.Generic.List<double>();

            foreach (var v in Verdicts)
            {
                if (v is not { } verdict) continue;

                judged++;

                switch (verdict.Result)
                {
                    case LiveResult.Perfect: perfect++; break;
                    case LiveResult.Good: good++; break;
                    case LiveResult.Meh: meh++; break;
                }

                if (verdict.Reason == LiveReason.NotHeard) notHeard++;
                if (verdict.Reason == LiveReason.WrongNote) wrong++;
                if (verdict.Reason == LiveReason.PitchUnclear) unclear++;
                if (verdict.Result != LiveResult.Miss) offsets += verdict.OffsetMs;
                if (verdict.Result != LiveResult.Miss && verdict.Reason == LiveReason.Played) timings.Add(verdict.OffsetMs / (rate > 0 ? rate : 1));
            }

            // How late the player's notes landed, for "correct by the last play" (the
            // microphone, timed by the level: a wait mode's resumes are timed by itself).
            if (micPlay && !WaitForPlayer && timings.Count >= LastPlayTiming.MIN_NOTES)
            {
                timings.Sort();
                LastPlay = new LastPlayTiming(timings[timings.Count / 2], timings.Count, playerTimingMs);
            }

            if (judged == 0)
            {
                summaryLogged = false;
                return;
            }

            int hits = perfect + good + meh;
            osu.Framework.Logging.Logger.Log($"kan! play: {judged} of {Notes.Count} notes judged — perfect {perfect}, good {good}, meh {meh}, "
                                             + $"wrong note {wrong}, not heard {notHeard}, pitch unclear {unclear}; "
                                             + $"mean offset {(hits > 0 ? offsets / hits : 0):0} ms; instrument tuning {tuning.Cents:+0;-0;0} cents; "
                                             + $"clock vs listening {(lagFrames > 0 ? lagSum / lagFrames : 0):+0;-0;0} ms ({(lagFrames > 0 ? lagMin : 0):+0;-0;0}..{(lagFrames > 0 ? lagMax : 0):+0;-0;0}), "
                                             + $"hits placed after now {hitsInFuture} of {hitsHeard}, mic delay {micDelayMs:0} ms, player timing {playerTimingMs:+0;-0;0} ms; {(failed ? Status.Value : "listening")}",
                level: osu.Framework.Logging.LogLevel.Important);
        }

        /// <summary>
        /// A stream time (real ms since listening started) on the level's timeline:
        /// scaled by the rate, minus the time the level stood still waiting, minus
        /// the mic delay. While waiting, everything lands where the level waits.
        /// </summary>
        private double toGameplay(float streamMs)
        {
            if (waitingFor != null) return waitingFor.StartTime;
            if (resuming) return resumeAt;

            // The player's habit (ahead of the line, most of us): their notes taken as meant.
            double habit = -playerTimingMs * rate;

            if (filePacedByLevel || seenCount < 2)
                return anchorMs + (streamMs - frozenStreamMs - micDelayMs) * rate + habit;

            return levelAt(streamMs - micDelayMs) + habit;
        }

        // What the level's clock showed at each real moment while listening (owner 2026-10-21:
        // a fixed anchor drifted 0-40 ms from the clock, so 60 % of the onsets were cut to
        // their arrival — two notes played alike could grade differently).
        private const int seen_size = 4096;
        private readonly double[] seenReal = new double[seen_size];
        private readonly double[] seenLevel = new double[seen_size];
        private int seenHead, seenCount;
        private double playerTimingMs;

        private void remember(double realMs, double levelMs)
        {
            if (seenCount > 0 && realMs <= seenReal[(seenHead - 1 + seen_size) % seen_size]) return;

            seenReal[seenHead] = realMs;
            seenLevel[seenHead] = levelMs;
            seenHead = (seenHead + 1) % seen_size;
            seenCount = Math.Min(seenCount + 1, seen_size);
        }

        /// <summary>
        /// The level's time at a real moment since listening began: between the
        /// frames that saw it, else carried on at the rate from the nearest one
        /// (a sound newer than the last frame, or across a stop).
        /// </summary>
        private double levelAt(double realMs)
        {
            int oldest = (seenHead - seenCount + seen_size) % seen_size;
            int newest = (seenHead - 1 + seen_size) % seen_size;

            if (realMs >= seenReal[newest])
                return seenLevel[newest] + (realMs - seenReal[newest]) * rate;
            if (realMs <= seenReal[oldest])
                return seenLevel[oldest] + (realMs - seenReal[oldest]) * rate;

            // Binary search over the ring, in age order.
            int lo = 0, hi = seenCount - 1;

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (seenReal[(oldest + mid) % seen_size] <= realMs) lo = mid;
                else hi = mid;
            }

            int a = (oldest + lo) % seen_size, b = (oldest + hi) % seen_size;
            double span = seenReal[b] - seenReal[a];

            // Frames far apart (the level stood still between): carry on from the later one.
            if (span > 100)
                return seenLevel[b] + (realMs - seenReal[b]) * rate;

            return seenLevel[a] + (seenLevel[b] - seenLevel[a]) * (realMs - seenReal[a]) / span;
        }

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.Scoring.ScoreProcessor? scoreProcessor { get; set; }

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

        protected override void Dispose(bool isDisposing)
        {
            // What was learnt of the instrument's tuning is kept for the next level.
            if (ForgeTiming.AUTO_TUNING.Value && Math.Abs(tuning.Cents - ForgeTiming.TUNING_CENTS.Value) >= 1)
                ForgeTiming.TUNING_CENTS.Value = tuning.Cents;

            logSummary();
            stopListening();
            saveTake();
            Trace?.Dispose();
            Trace = null;
            base.Dispose(isDisposing);
        }
    }
}
