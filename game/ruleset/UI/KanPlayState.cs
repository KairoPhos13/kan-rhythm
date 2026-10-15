// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Screens.Play;
using Kan.Forge.Native;
using Kan.Forge.Play;
using Kan.Forge.Tracks;
using Kan.Ruleset.Beatmaps;
using Kan.Ruleset.Objects;

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

        public readonly LiveJudge Judge = new LiveJudge();

        public readonly IReadOnlyList<KanHitObject> Notes;

        /// <summary>Each note's verdict once judged (by <see cref="KanHitObject.Index"/>).</summary>
        public readonly LiveVerdict?[] Verdicts;

        /// <summary>A note was judged: its index and verdict.</summary>
        public event Action<int, LiveVerdict>? Judged;

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
        private LiveSession? session;
        private uint sessionBase;
        private double anchorMs;
        private double micDelayMs;
        private volatile float clockMs;
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
            gameplayClock?.IsPaused.BindValueChanged(p =>
            {
                if (p.NewValue) stopListening();
            });
        }

        /// <summary>Called by a note's judging object once the judge decided it.</summary>
        public void Report(KanHitObject note, LiveVerdict verdict)
        {
            Verdicts[note.Index] = verdict;

            switch (verdict.Result)
            {
                case LiveResult.Great:
                case LiveResult.Ok:
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
            Judged?.Invoke(note.Index, verdict);

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
                                                 + $"streak {Streak}, bloom {Bloom:0.00}, trail {TrailCount}, listening {session != null}", level: osu.Framework.Logging.LogLevel.Important);
            }

            if (gameplayClock == null) return; // not in gameplay (nothing to listen to)

            double now = gameplayClock.CurrentTime;
            clockMs = (float)now;

            if (session == null && !failed && gameplayClock.IsRunning && !gameplayClock.IsPaused.Value)
                startListening(now);

            if (session != null)
                takeFacts();

            Judge.Forget(now - 5000);


            // Ease towards the target: blooming is felt over a phrase, not a note.
            Bloom += (bloomTarget - Bloom) * Math.Min(1, Time.Elapsed / 900);
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
                source = new FileSource(file, () => clockMs - fileStart);
            }
            else
            {
                // The take's first sample is "now" — as in the mic calibration, whose
                // measured round trip (micDelayMs) covers everything after it.
                anchorMs = now;
                source = new MicSource();
            }

            var s = new LiveSession(source, instrument);

            if (!s.Start())
            {
                failed = true;
                bloomTarget = 1; // nothing to judge by: never muffle the music for it
                Status.Value = $"Not listening: {s.Error}.";
                osu.Framework.Logging.Logger.Log($"kan!: live input failed: {s.Error}", level: osu.Framework.Logging.LogLevel.Important);
                return;
            }

            session = s;
            sessionBase += 1u << 24; // note ids restart with each session
            Status.Value = string.Empty;
        }

        private void stopListening()
        {
            session?.Dispose();
            session = null;
        }

        private void takeFacts()
        {
            while (session!.TryTake(out var f))
            {
                uint id = sessionBase + f.NoteId;

                switch (f.Kind)
                {
                    case KanForgeLiveEvent.HIT:
                        Judge.Heard(id, toGameplay(f.OnsetMs));
                        break;

                    case KanForgeLiveEvent.PITCH:
                        // "Nothing measurable" comes as its own stage, or as a pitch of 0.
                        if (f.Stage == KanForgeLiveEvent.UNRESOLVED || f.Midi <= 0)
                            Judge.Unresolved(id, final: f.Stage != KanForgeLiveEvent.PROVISIONAL);
                        else
                            Judge.Pitch(id, (int)MathF.Round(f.Midi), f.Cents, f.Stage == KanForgeLiveEvent.FINAL);
                        break;

                    case KanForgeLiveEvent.FRAME:
                        Judge.Frame(toGameplay(f.TMs), f.Midi);
                        Trail[trailHead] = new TrailPoint(toGameplay(f.TMs), f.Midi, f.Level);
                        trailHead = (trailHead + 1) % Trail.Length;
                        TrailCount = Math.Min(TrailCount + 1, Trail.Length);
                        break;
                }
            }
        }

        /// <summary>The i-th newest trail point (0 = newest).</summary>
        public TrailPoint TrailAt(int i) => Trail[(trailHead - 1 - i + Trail.Length * 2) % Trail.Length];

        /// <summary>One line for the log: how the play went (diagnostics and bug reports).</summary>
        private void logSummary()
        {
            if (summaryLogged) return;

            summaryLogged = true;
            int judged = 0, great = 0, ok = 0, meh = 0, notHeard = 0, wrong = 0, unclear = 0;
            double offsets = 0;

            foreach (var v in Verdicts)
            {
                if (v is not { } verdict) continue;

                judged++;

                switch (verdict.Result)
                {
                    case LiveResult.Great: great++; break;
                    case LiveResult.Ok: ok++; break;
                    case LiveResult.Meh: meh++; break;
                }

                if (verdict.Reason == LiveReason.NotHeard) notHeard++;
                if (verdict.Reason == LiveReason.WrongNote) wrong++;
                if (verdict.Reason == LiveReason.PitchUnclear) unclear++;
                if (verdict.Result != LiveResult.Miss) offsets += verdict.OffsetMs;
            }

            if (judged == 0)
            {
                summaryLogged = false;
                return;
            }

            int hits = great + ok + meh;
            osu.Framework.Logging.Logger.Log($"kan! play: {judged} of {Notes.Count} notes judged — great {great}, ok {ok}, meh {meh}, "
                                             + $"wrong note {wrong}, not heard {notHeard}, pitch unclear {unclear}; "
                                             + $"mean offset {(hits > 0 ? offsets / hits : 0):0} ms; {(failed ? Status.Value : "listening")}",
                level: osu.Framework.Logging.LogLevel.Important);
        }

        private double toGameplay(float streamMs) => anchorMs + streamMs - micDelayMs;

        protected override void Dispose(bool isDisposing)
        {
            logSummary();
            stopListening();
            base.Dispose(isDisposing);
        }
    }
}
