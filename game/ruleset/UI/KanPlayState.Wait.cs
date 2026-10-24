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
    /// The wait mode: the level stops at a note until the player plays it, and never shows
    /// that it ran past one.
    /// </summary>
    public partial class KanPlayState : Component
    {
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
        /// from exactly what was being held.
        /// </summary>
        public double WaitResumeTime { get; private set; }

        /// <summary>
        /// Wait mode: the level runs this far past a note before it stops
        ///. A note played on time is heard within it, so
        /// the level never stutters for the detection's own delay.
        /// </summary>
        public const double WAIT_GRACE_MS = 200;

        /// <summary>Wait mode: a wrong note heard while waiting (its MIDI number), shown at once.</summary>
        public event Action<int>? WrongWhileWaiting;

        private KanHitObject? waitingFor;

        /// <summary>
        /// Wait mode: whether the wait still takes care of this note (it has
        /// not reached it, or waits for it now). Only then is a judge's miss
        /// held back; a note the wait has left behind takes its verdict, so
        /// every note is decided and the level ends (2026-10-15: the results
        /// never showed, 322 of 325 notes judged).
        /// </summary>
        public bool WaitPending(KanHitObject note)
            => WaitForPlayer && (ReferenceEquals(note, waitingFor) || note.Index >= nextWait); // Notes are in index order

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

        private void updateWait(double now)
        {
            if (waitingFor != null || resuming || clockContainer == null) return;

            // The next note to wait for, in order: skip notes already decided or heard.
            // A note missed for a wrong try is still waited for: the miss is the
            // penalty, finding the right note is still the walk.
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
            if (n.PitchUnknown || n.Articulation == Kan.Core.Score.Articulation.Legato || n.Index == 0) return false;

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
                // lag, not a try.
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

            if (rightReadings >= right_readings_to_release || (right && n.Articulation == Kan.Core.Score.Articulation.Legato))
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
            timeline.Freeze(real - (now - WaitStopTime) / rate);
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

        // ------------------------------------------------------------------
        // Wait mode: the level stops at a note until the player plays it
        // ------------------------------------------------------------------

        // ------------------------------------------------------------------
        // Wait mode, as seen: the level never shows that it ran past a note
        // ------------------------------------------------------------------
    }
}
