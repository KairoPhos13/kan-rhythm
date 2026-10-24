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
    /// What is happening in a play: the live session listening to the player,
    /// the judge pairing what it hears with the score, the trail of the
    /// player's pitch, and the "bloom" — how open the music is (it opens up as
    /// the player plays well and closes a little on misses; it never stops).
    /// Shared by the judging objects and the staff.
    /// </summary>
    public partial class KanPlayState : Component
    {
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

        /// <summary>"By heart": how long (ms) a note shows as it comes into view; null = as long as it is in view.</summary>
        public double? SeenForMs { get; init; }

        /// <summary>The level's design (sections, palette, effects): the level's own, or generated from its notes.</summary>
        public Kan.Core.Design.KanDesign Design { get; init; } = new Kan.Core.Design.KanDesign();

        /// <summary>The level's bars, beats and parts of a beat (in order; <see cref="Kan.Core.Score.Bars"/>): the staff marks them.</summary>
        public IReadOnlyList<Kan.Core.Score.Bars.Tick> Ticks { get; init; } = Array.Empty<Kan.Core.Score.Bars.Tick>();

        /// <summary>A replay (or autoplay) to read the facts from instead of listening.</summary>
        public Func<Replay?>? ReplaySource { get; set; }

        /// <summary>Where a live play records what it heard (its replay).</summary>
        public KanReplayRecorder? Recorder { get; set; }

        /// <summary>The level's set: a play's recording is kept for it (<see cref="PlayTake"/>).</summary>
        public Guid SetId { get; init; }

        private readonly System.Diagnostics.Stopwatch realClock = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Real time (ms) for <see cref="LivePoint.RealMs"/>.</summary>
        public double RealNowMs => realClock.Elapsed.TotalMilliseconds;

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.UI.IFrameStableClock? frameStableClock { get; set; }

        private GameplayClockContainer? clockContainer;
        private volatile float clockMs;

        /// <summary>Where the level is (ms), as of the last frame.</summary>
        internal float ClockMs => clockMs;
        private bool failed;

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
        /// The staff lights the target with it.
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
                double expected = listenedFrom + (listeningFor.Elapsed.TotalMilliseconds - timeline.FrozenMs) * rate;

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
                    osu.Framework.Logging.Logger.Log($"kan!: the microphone gave nothing for {session.SilentForMs:0} ms; opening it again"); // the player reads it on screen
                    Trace?.Row(TraceRealMs, now, "reopen");
                    stopListening();
                    micReopened = true;
                    startListening(now);
                }

                if (session != null)
                {
                    timeline.Remember(listeningFor.Elapsed.TotalMilliseconds, now);

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

        /// <summary>The player has the pause menu open (set by the wait overlay, which listens while the level stands still).</summary>
        public bool UserPaused { get; set; }

        /// <summary>The i-th newest trail point (0 = newest).</summary>
        public TrailPoint TrailAt(int i) => Trail[(trailHead - 1 - i + Trail.Length * 2) % Trail.Length];

        [Resolved(CanBeNull = true)]
        private osu.Game.Rulesets.Scoring.ScoreProcessor? scoreProcessor { get; set; }

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
