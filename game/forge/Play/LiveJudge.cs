// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;

namespace Kan.Forge.Play
{
    public enum LiveResult
    {
        Great,
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
        public const double GREAT_MS = 70;
        public const double OK_MS = 130;
        public const double MEH_MS = 200;

        /// <summary>How long after the window a note waits for a heard onset's pitch.</summary>
        public const double PITCH_WAIT_MS = 300;

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
        /// Judge the score note starting at <paramref name="startMs"/>, or null
        /// while it cannot be decided yet. <paramref name="anyPitch"/>: the score
        /// itself does not know the pitch (the transcriber could not tell).
        /// <paramref name="legato"/>: the note is slurred from the previous one
        /// — no attack is expected, so the pitch sounding at its start counts.
        /// </summary>
        public LiveVerdict? Judge(double startMs, int midi, bool anyPitch, double nowMs, bool legato = false)
        {
            Played? best = null, unclear = null, wrong = null, wrongProvisional = null;
            bool waiting = false;

            foreach (var p in played)
            {
                if (p.Claimed || Math.Abs(p.TimeMs - startMs) > MEH_MS) continue;

                bool known = p.Midi != null;

                if (anyPitch || (known && p.Midi == midi))
                {
                    if (best == null || Math.Abs(p.TimeMs - startMs) < Math.Abs(best.TimeMs - startMs))
                        best = p;
                }
                else if (p.Unresolved)
                    unclear ??= p;
                else if (known && p.Final)
                    wrong ??= p;
                else
                {
                    waiting = true; // no pitch yet, or a provisional one that may still change
                    if (known) wrongProvisional ??= p;
                }
            }

            if (best != null)
                return claim(best, startMs, best.Unresolved ? LiveReason.PitchUnclear : LiveReason.Played);

            // No attack found, but the note's pitch arrived at its start: a slur
            // (legato), or an attack too soft to mark an onset. A note held over
            // from before does not count — a repeated note needs its own attack.
            if (!anyPitch && nowMs >= startMs + LEGATO_TO_MS && reachedPitch(startMs, midi, mustArrive: !legato) is { } reachedMs)
                return grade(reachedMs - startMs, LiveReason.Played, midi, 0);

            bool windowOpen = nowMs <= startMs + MEH_MS;
            bool canWait = nowMs <= startMs + MEH_MS + PITCH_WAIT_MS;

            if (windowOpen || (waiting && canWait))
                return null;

            if (unclear != null)
                return claim(unclear, startMs, LiveReason.PitchUnclear);

            // Out of time: a provisional pitch is the best word we have on what was played.
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
                    if (Math.Abs(m - midi) < 0.5) beforeOn++;
                }

                if (time < startMs - LEGATO_FROM_MS || time > startMs + LEGATO_TO_MS) continue;

                total++;

                if (Math.Abs(m - midi) < 0.5)
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

        private static LiveVerdict claim(Played p, double startMs, LiveReason reason)
        {
            p.Claimed = true;
            return grade(p.TimeMs - startMs, reason, p.Midi, p.Cents);
        }

        private static LiveVerdict grade(double offset, LiveReason reason, int? playedMidi, float cents)
        {
            double distance = Math.Abs(offset);

            var result = distance <= GREAT_MS ? LiveResult.Great
                : distance <= OK_MS ? LiveResult.Ok
                : LiveResult.Meh;

            // Heard but not understood: credit the timing, not more than "ok".
            if (reason == LiveReason.PitchUnclear && result == LiveResult.Great)
                result = LiveResult.Ok;

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
