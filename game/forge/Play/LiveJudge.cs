// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;

namespace Kan.Forge.Play
{
    /// <summary>The grades of osu!mania (owner 2026-10-15: "keep osu!'s steps — chaining perfects is the vice").</summary>
    public enum LiveResult
    {
        Perfect,
        Great,
        Good,
        Ok,
        Meh,
        Miss,
    }

    /// <summary>Why a note got its result — what the player is told, in musical words.</summary>
    public enum LiveReason
    {
        /// <summary>The right note, judged by its timing.</summary>
        Played,

        /// <summary>A note was heard on time, but its pitch could not be told: accepted, at most "ok".</summary>
        PitchUnclear,

        /// <summary>Something was played on time, but another note.</summary>
        WrongNote,

        /// <summary>Nothing was heard near the note.</summary>
        NotHeard,
    }

    /// <summary>The judgement of one score note.</summary>
    public readonly record struct LiveVerdict(LiveResult Result, LiveReason Reason, double OffsetMs, int? PlayedMidi, float Cents);

    /// <summary>
    /// Pairs what the microphone heard with the score and judges each note
    /// (libkan describes, the game judges). Played notes arrive as facts:
    /// an onset first, its pitch a moment later (provisional within ~45 ms,
    /// final within ~200 ms). A score note is decided by the closest unclaimed
    /// onset within <see cref="MEH_MS"/> that has its pitch; while an onset's
    /// pitch is still coming, the note waits (up to <see cref="PITCH_WAIT_MS"/>
    /// past the window). Each played note answers one score note at most.
    /// Times are gameplay times (ms), already corrected for the mic delay.
    /// </summary>
    public class LiveJudge
    {
        // osu!mania's steps over kan!'s own windows: an acoustic attack is
        // detected within ~20-30 ms, so the windows stay wider than osu!'s;
        // great / ok / meh keep the bounds they had, perfect and good split them.
        public const double PERFECT_MS = 35;
        public const double GREAT_MS = 70;
        public const double GOOD_MS = 100;
        public const double OK_MS = 130;
        public const double MEH_MS = 200;

        /// <summary>Scales the timing windows: easier above 1 ("easy"), stricter below ("hard rock").</summary>
        public double WindowScale { get; set; } = 1;

        /// <summary>
        /// How far from a score note (cents) a played pitch still counts as
        /// that note. 50 = the nearest note, as a tuner reads it; the Easy
        /// mod widens it (owner 2026-10-15), never as far as the next note.
        /// </summary>
        public double PitchToleranceCents { get; set; } = 50;

        /// <summary>Whether a pitch (fractional MIDI) counts as <paramref name="midi"/>.</summary>
        public bool Matches(float pitch, int midi) => Math.Abs(pitch - midi) * 100 < PitchToleranceCents;

        private bool matches(Played p, int midi) => p.Midi is int m && Matches(m + p.Cents / 100, midi);

        private double perfect => PERFECT_MS * WindowScale;
        private double great => GREAT_MS * WindowScale;
        private double good => GOOD_MS * WindowScale;
        private double ok => OK_MS * WindowScale;
        private double meh => MEH_MS * WindowScale;

        /// <summary>How long after the window a note waits for a heard onset's pitch.</summary>
        public const double PITCH_WAIT_MS = 300;

        /// <summary>A wrong note started this long before a note's start is still a try at it (earlier: it belonged to what came before).</summary>
        public const double TRY_BEFORE_MS = 100;

        /// <summary>
        /// A wrong pitch held this many readings in a row (10 ms each) is
        /// confirmed. libkan's final pitch comes only when a note ends — for a
        /// wrong note held until the right one is found, at the very moment of
        /// finding it (owner 2026-10-17) — so the readings confirm it sooner.
        /// </summary>
        public const int WRONG_HOLD_READINGS = 15;

        /// <summary>
        /// Whether a played pitch is a wrong try at <paramref name="midi"/>,
        /// not one of the ones a learner forgives (owner 2026-10-17): the note
        /// an octave off (the detector's slip, not the player's), or a note
        /// the score expects around it — the previous note still sounding
        /// (a typical learner's lag), a chord's other notes, the next note
        /// come early.
        /// </summary>
        public static bool IsWrongTry(int played, int midi, IReadOnlyCollection<int>? expected)
        {
            if (played <= 0 || sameNote(played, midi)) return false;

            if (expected != null)
            {
                foreach (int e in expected)
                {
                    if (sameNote(played, e)) return false;
                }
            }

            return true;

            static bool sameNote(int a, int b) => ((a - b) % 12 + 12) % 12 == 0;
        }

        private class Played
        {
            public uint Id;
            public double TimeMs;
            public int? Midi;
            public float Cents;
            public bool Final;
            public bool Unresolved;
            public bool Claimed;
        }

        private readonly List<Played> played = new List<Played>();

        /// <summary>Recent pitch readings (gameplay time, fractional MIDI), oldest first.</summary>
        private readonly List<(double TimeMs, float Midi)> frames = new List<(double, float)>();

        /// <summary>A legato note is judged by the pitch sounding around its start, from this long before…</summary>
        public const double LEGATO_FROM_MS = 80;

        /// <summary>…to this long after (a slur has no attack to find).</summary>
        public const double LEGATO_TO_MS = 150;

        /// <summary>A pitch reading (every 10 ms while something sounds).</summary>
        public void Frame(double timeMs, float midi)
        {
            if (midi > 0) frames.Add((timeMs, midi));
        }

        /// <summary>A note started (its onset, in gameplay time).</summary>
        public void Heard(uint id, double timeMs) => played.Add(new Played { Id = id, TimeMs = timeMs });

        /// <summary>The pitch of a heard note. A final pitch replaces a provisional one.</summary>
        public void Pitch(uint id, int midi, float cents, bool final)
        {
            var p = find(id);
            if (p == null || (p.Final && !final)) return;

            p.Midi = midi;
            p.Cents = cents;
            p.Final = final;
            p.Unresolved = false;
        }

        /// <summary>The pitch of a heard note could not be told.</summary>
        public void Unresolved(uint id, bool final)
        {
            var p = find(id);
            if (p == null || p.Midi != null || !final) return;

            p.Unresolved = true;
            p.Final = true;
        }

        /// <summary>Forget played notes long past (they can no longer answer any note).</summary>
        public void Forget(double beforeMs)
        {
            played.RemoveAll(p => p.TimeMs < beforeMs);
            frames.RemoveAll(f => f.TimeMs < beforeMs);
        }

        /// <summary>
        /// Whether a played note that would answer this score note has been heard
        /// (unclaimed, within the window, with its pitch). For the wait mode:
        /// a note already played on time needs no waiting.
        /// </summary>
        public bool HasHeard(double startMs, int midi, bool anyPitch)
        {
            foreach (var p in played)
            {
                if (!p.Claimed && Math.Abs(p.TimeMs - startMs) <= meh && (anyPitch || matches(p, midi)))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Whether the note's pitch sounded around its start — at least
        /// <paramref name="readings"/> readings in a row at it, from just
        /// before the start up to <paramref name="untilMs"/> — even without an
        /// attack of its own (a short note slurred into, 2026-10-15: the wait
        /// mode stopped for a 123 ms note that had been played).
        /// </summary>
        public bool PitchSounded(double startMs, int midi, double untilMs, int readings = 4)
        {
            int run = 0;

            foreach (var (time, m) in frames)
            {
                if (time < startMs - LEGATO_FROM_MS || time > untilMs) continue;

                run = Matches(m, midi) ? run + 1 : 0;
                if (run >= readings) return true;
            }

            return false;
        }

        /// <summary>
        /// Judge the score note starting at <paramref name="startMs"/>, or null
        /// while it cannot be decided yet. <paramref name="anyPitch"/>: the score
        /// itself does not know the pitch (the transcriber could not tell).
        /// <paramref name="legato"/>: the note is slurred from the previous one
        /// — no attack is expected, so the pitch sounding at its start counts.
        /// <paramref name="expected"/>: the notes the score has around this one
        /// (the previous, a chord's, the next): played, they are not wrong
        /// tries at it (<see cref="IsWrongTry"/>).
        ///
        /// A wrong try — its pitch confirmed, before the right note — is a
        /// miss at once (owner 2026-10-17: reported only once the right note
        /// came, the penalty landed on the moment of finding it). A slip put
        /// right before its pitch is confirmed is no try: the right note wins.
        /// </summary>
        public LiveVerdict? Judge(double startMs, int midi, bool anyPitch, double nowMs, bool legato = false, IReadOnlyCollection<int>? expected = null)
        {
            Played? best = null, unclear = null, wrong = null, wrongProvisional = null;
            bool waiting = false;

            foreach (var p in played)
            {
                if (p.Claimed || Math.Abs(p.TimeMs - startMs) > meh) continue;

                bool known = p.Midi != null;

                if (anyPitch || (known && matches(p, midi)))
                {
                    if (best == null || Math.Abs(p.TimeMs - startMs) < Math.Abs(best.TimeMs - startMs))
                        best = p;
                }
                else if (p.Unresolved)
                    unclear ??= p;
                else if (known && (p.Final || held(p)))
                {
                    // The notes around it are no tries at this one: let the window run.
                    if (IsWrongTry(p.Midi!.Value, midi, expected)) wrong ??= p;
                }
                else
                {
                    waiting = true; // no pitch yet, or a provisional one that may still change
                    if (known) wrongProvisional ??= p;
                }
            }

            if (best != null)
                return claim(best, startMs, best.Unresolved ? LiveReason.PitchUnclear : LiveReason.Played);

            if (wrong != null && wrong.TimeMs >= startMs - TRY_BEFORE_MS)
            {
                wrong.Claimed = true;
                return new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, wrong.TimeMs - startMs, wrong.Midi, wrong.Cents);
            }

            // No attack found, but the note's pitch arrived at its start: a slur
            // (legato), or an attack too soft to mark an onset. A note held over
            // from before does not count — a repeated note needs its own attack.
            if (!anyPitch && nowMs >= startMs + LEGATO_TO_MS && reachedPitch(startMs, midi, mustArrive: !legato) is { } reachedMs)
                return grade(reachedMs - startMs, LiveReason.Played, midi, 0);

            bool windowOpen = nowMs <= startMs + meh;
            bool canWait = nowMs <= startMs + meh + PITCH_WAIT_MS;

            if (windowOpen || (waiting && canWait))
                return null;

            if (unclear != null)
                return claim(unclear, startMs, LiveReason.PitchUnclear);

            // Out of time: a provisional pitch is the best word we have on what was played.
            if (wrongProvisional != null && IsWrongTry(wrongProvisional.Midi!.Value, midi, expected))
                wrong ??= wrongProvisional;

            if (wrong != null)
                return new LiveVerdict(LiveResult.Miss, LiveReason.WrongNote, wrong.TimeMs - startMs, wrong.Midi, wrong.Cents);

            return new LiveVerdict(LiveResult.Miss, LiveReason.NotHeard, 0, null, 0);
        }

        /// <summary>
        /// When the pitch reached the note around its start, if it sounded there
        /// for most of the readings, or null. <paramref name="mustArrive"/>: it
        /// must not have been sounding just before (it arrived for this note).
        /// </summary>
        private double? reachedPitch(double startMs, int midi, bool mustArrive)
        {
            int total = 0, on = 0, before = 0, beforeOn = 0;
            double? first = null;

            foreach (var (time, m) in frames)
            {
                if (time >= startMs - 200 && time < startMs - 60)
                {
                    before++;
                    if (Matches(m, midi)) beforeOn++;
                }

                if (time < startMs - LEGATO_FROM_MS || time > startMs + LEGATO_TO_MS) continue;

                total++;

                if (Matches(m, midi))
                {
                    on++;
                    first ??= time;
                }
            }

            if (mustArrive && before > 0 && beforeOn * 2 >= before) return null; // held over, not arrived

            if (total < 5 || on * 2 < total) return null;

            // Already sounding when the window opened (tied): nothing to time, so on time.
            return first!.Value <= startMs - LEGATO_FROM_MS + 10 ? startMs : first.Value;
        }

        /// <summary>Whether a played note's pitch has held since its onset for <see cref="WRONG_HOLD_READINGS"/> readings in a row.</summary>
        private bool held(Played p)
        {
            float pitch = p.Midi!.Value + p.Cents / 100;
            int run = 0;

            foreach (var (time, m) in frames)
            {
                if (time < p.TimeMs) continue;

                run = Math.Abs(m - pitch) < 0.5f ? run + 1 : 0;
                if (run >= WRONG_HOLD_READINGS) return true;
            }

            return false;
        }

        private LiveVerdict claim(Played p, double startMs, LiveReason reason)
        {
            p.Claimed = true;
            return grade(p.TimeMs - startMs, reason, p.Midi, p.Cents);
        }

        private LiveVerdict grade(double offset, LiveReason reason, int? playedMidi, float cents)
        {
            double distance = Math.Abs(offset);

            var result = distance <= perfect ? LiveResult.Perfect
                : distance <= great ? LiveResult.Great
                : distance <= good ? LiveResult.Good
                : distance <= ok ? LiveResult.Ok
                : LiveResult.Meh;

            // Heard but not understood: credit the timing, not more than "meh"
            // (owner 2026-10-15: noise must not pass for playing well).
            if (reason == LiveReason.PitchUnclear && result != LiveResult.Miss)
                result = LiveResult.Meh;

            return new LiveVerdict(result, reason, offset, playedMidi, cents);
        }

        private Played? find(uint id)
        {
            for (int i = played.Count - 1; i >= 0; i--)
            {
                if (played[i].Id == id) return played[i];
            }

            return null;
        }
    }
}
