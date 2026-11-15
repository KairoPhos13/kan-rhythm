// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Play
{
    /// <summary>What one microphone made of a held note.</summary>
    /// <param name="Heard">It heard a pitched note at all.</param>
    /// <param name="HeardFraction">How much of the hold, from the attack on, it heard as pitched (0..1).</param>
    /// <param name="FadeDb">How far the level fell from just after the attack to the end of the hold (dB, positive = quieter).</param>
    /// <param name="LevelDb">The level just after the attack (dBFS).</param>
    /// <param name="Midi">The note it heard (median, fractional MIDI; 0 when not heard).</param>
    public readonly record struct HeldNoteResult(bool Heard, double HeardFraction, double FadeDb, double LevelDb, float Midi);

    /// <summary>
    /// Whether a microphone keeps a held note (issue 39). Some inputs run the
    /// sound through processing meant for calls (noise suppression, echo
    /// cancellation, automatic gain): it takes a steady tone for noise and
    /// fades it out, so a held note dies away while the player still plays
    /// it. The Steam Deck's default input does this; its raw input does not.
    /// Names of inputs change with software and hardware, so the check goes
    /// by behaviour: the player holds one note, every input listens at once,
    /// and the one that keeps it steadiest wins (<see cref="Choose{T}"/>).
    /// The same measure watches the chosen input while the player plays
    /// (<see cref="Fading"/>).
    /// Feed it the live describer's readings: time, level (RMS) and pitch.
    /// </summary>
    public sealed class HeldNoteCheck
    {
        /// <summary>How long the player is asked to hold the note.</summary>
        public const double HOLD_MS = 4000;

        /// <summary>A fall this large while the note is still played is processing, not the player (a held wind or bowed note varies by a few dB).</summary>
        public const double FADING_DB = 9;

        /// <summary>Switch from the current input only when another keeps the note this much better.</summary>
        public const double BETTER_DB = 6;

        /// <summary>The level is read this long after the attack (the attack itself overshoots).</summary>
        private const double settle_from_ms = 200, settle_to_ms = 900;

        /// <summary>The end of the hold is this last stretch.</summary>
        private const double end_ms = 700;

        private readonly List<(double T, double Level, float Midi)> frames = new List<(double, double, float)>();

        public void Add(double tMs, double level, float midi) => frames.Add((tMs, level, midi));

        public void Clear() => frames.Clear();

        /// <summary>The whole hold, measured.</summary>
        public HeldNoteResult Measure()
        {
            int attack = frames.FindIndex(f => f.Midi > 0);
            if (attack < 0) return default;

            var held = frames.Skip(attack).ToList();
            double t0 = held[0].T, tEnd = held[^1].T;

            var voiced = held.Where(f => f.Midi > 0).ToList();
            if (voiced.Count < 10 || tEnd - t0 < settle_to_ms + end_ms) return default;

            double start = median(voiced.Where(f => f.T >= t0 + settle_from_ms && f.T <= t0 + settle_to_ms).Select(f => f.Level));
            // The end counts every reading: a faded note may lose its pitch too.
            double end = median(held.Where(f => f.T >= tEnd - end_ms).Select(f => f.Level));

            if (!(start > 0)) return default;

            return new HeldNoteResult(
                true,
                voiced.Count / (double)held.Count,
                Db(start) - Db(Math.Max(end, 1e-7)),
                Db(start),
                (float)median(voiced.Select(f => (double)f.Midi)));
        }

        /// <summary>
        /// While playing: the newest note, still sounding at its pitch, has
        /// lost more than <see cref="FADING_DB"/> since its attack (call after
        /// each reading; <see cref="Clear"/> at each attack).
        /// </summary>
        public bool Fading(out double fadeDb)
        {
            fadeDb = 0;
            int attack = frames.FindIndex(f => f.Midi > 0);
            if (attack < 0) return false;

            double t0 = frames[attack].T, tEnd = frames[^1].T;
            if (tEnd - t0 < 1500) return false;

            var settled = frames.Where(f => f.Midi > 0 && f.T >= t0 + settle_from_ms && f.T <= t0 + settle_to_ms).ToList();
            var last = frames.Where(f => f.T >= tEnd - 300).ToList();
            if (settled.Count < 3 || last.Count < 3) return false;

            // Still the same note: the player is holding it, the input is fading it.
            float note = (float)median(settled.Select(f => (double)f.Midi));
            if (last.Count(f => f.Midi > 0 && Math.Abs(f.Midi - note) < 0.6f) < last.Count / 2) return false;

            fadeDb = Db(median(settled.Select(f => f.Level))) - Db(Math.Max(median(last.Select(f => f.Level)), 1e-7));
            return fadeDb > FADING_DB;
        }

        /// <summary>
        /// The input that kept the held note best: of those that heard it for
        /// at least half the hold, the one whose level fell least (then the
        /// louder). The current one stays unless another keeps the note
        /// <see cref="BETTER_DB"/> better, or it did not hear the note. Null
        /// when none heard it.
        /// </summary>
        public static T? Choose<T>(IEnumerable<(T Input, HeldNoteResult Result)> results, T? current) where T : class
        {
            var heard = results.Where(r => r.Result.Heard && r.Result.HeardFraction >= 0.5).ToList();
            if (heard.Count == 0) return null;

            var best = heard.OrderBy(r => r.Result.FadeDb).ThenByDescending(r => r.Result.LevelDb).First();

            foreach (var r in heard)
            {
                if (current != null && Equals(r.Input, current) && r.Result.FadeDb - best.Result.FadeDb < BETTER_DB)
                    return r.Input;
            }

            return best.Input;
        }

        public static double Db(double rms) => 20 * Math.Log10(Math.Max(rms, 1e-7));

        private static double median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
        }
    }
}
