// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Kan.Core.Score
{
    /// <summary>
    /// Where the level's bars start (owner 2026-10-22: "the only way to know
    /// the bars is listening" — the staff marks them, the bars only, not the
    /// beats between). A tempo that breathes changes on any beat (the tempo
    /// bar's "the beat falls here", the sync with the next tempo), not only
    /// on a bar: a change landing on a beat of the tempo before goes on
    /// counting the bar it falls in (owner 2026-10-22: the tempos were synced
    /// but "the lines don't show it"). A change of beats per bar, or one off
    /// the beats, starts a bar, as osu! counts every timing point.
    /// </summary>
    public static class Bars
    {
        /// <summary>One timing point: where it starts, its beat (ms) and beats per bar.</summary>
        public readonly record struct Tempo(double TimeMs, double BeatMs, int BeatsPerBar);

        /// <summary>How far off a beat of the tempo before (in beats) a change may land and still count as on it.</summary>
        public const double ON_BEAT = 0.25;

        /// <summary>For each timing point, how many beats of its bar have gone by where it starts (0: it starts a bar).</summary>
        public static int[] PhaseOf(IReadOnlyList<Tempo> tempos)
        {
            var phase = new int[tempos.Count];

            for (int i = 1; i < tempos.Count; i++)
            {
                var before = tempos[i - 1];
                int perBar = Math.Max(1, before.BeatsPerBar);
                if (before.BeatMs <= 0 || Math.Max(1, tempos[i].BeatsPerBar) != perBar) continue;

                double beats = (tempos[i].TimeMs - before.TimeMs) / before.BeatMs;
                long whole = (long)Math.Round(beats);
                if (Math.Abs(beats - whole) > ON_BEAT) continue;

                phase[i] = (int)((phase[i - 1] + whole) % perBar);
            }

            return phase;
        }

        public enum TickKind
        {
            Bar,
            Beat,

            /// <summary>A part of a beat (<see cref="DivisionOf"/>).</summary>
            Part,
        }

        /// <summary>A line of the grid: where, what it marks, and the number of the bar it is in (from 1).</summary>
        public readonly record struct Tick(double TimeMs, TickKind Kind, int Bar);

        /// <summary>The parts of a beat a level's notes may start on, finest last.</summary>
        private static readonly int[] divisions = { 1, 2, 3, 4, 6, 8, 12, 16 };

        /// <summary>
        /// The finest part of a beat the notes start on (owner 2026-10-22: "the
        /// smallest in this piece is 1/2, so the smallest line shown should be
        /// that"): 1 for beats, 2 for halves, 3 for triplets… A part only a
        /// stray note or two uses does not count; notes on none of them (an
        /// unquantised transcription) are left out.
        /// </summary>
        public static int DivisionOf(IReadOnlyList<Tempo> tempos, IEnumerable<double> startsMs)
        {
            if (tempos.Count == 0) return 1;

            var counts = new int[divisions.Length];
            int counted = 0;

            foreach (double start in startsMs)
            {
                // Its tempo: the last starting by it (the first, before any).
                int at = 0;
                while (at + 1 < tempos.Count && tempos[at + 1].TimeMs <= start + 1) at++;

                var t = tempos[at];
                if (t.BeatMs <= 0) continue;

                double beats = (start - t.TimeMs) / t.BeatMs;

                for (int d = 0; d < divisions.Length; d++)
                {
                    double part = t.BeatMs / divisions[d];
                    double off = Math.Abs(beats * divisions[d] - Math.Round(beats * divisions[d])) * part;

                    if (off <= Math.Min(30, 0.2 * part))
                    {
                        counts[d]++;
                        counted++;
                        break;
                    }
                }
            }

            int enough = Math.Max(1, (int)Math.Ceiling(counted * 0.03));
            int division = 1, finest = 1;

            for (int d = 0; d < divisions.Length; d++)
            {
                if (counts[d] < enough) continue;

                division = lcm(division, divisions[d]);
                finest = divisions[d];
            }

            // Halves and triplets together: sixths; past sixteenths, only the finest used.
            return division <= 16 ? division : finest;
        }

        private static int lcm(int a, int b)
        {
            int x = a, y = b;
            while (y != 0) (x, y) = (y, x % y);
            return a / x * b;
        }

        /// <summary>
        /// The grid's lines up to <paramref name="untilMs"/>, in order: each
        /// bar, the beats between, and each beat cut in <paramref name="division"/> parts.
        /// </summary>
        public static List<Tick> Ticks(IReadOnlyList<Tempo> tempos, double untilMs, int division = 1)
        {
            var ticks = new List<Tick>();
            var phase = PhaseOf(tempos);
            division = Math.Max(1, division);
            int bar = 0;

            for (int i = 0; i < tempos.Count; i++)
            {
                var t = tempos[i];
                int perBar = Math.Max(1, t.BeatsPerBar);
                if (t.BeatMs <= 0) continue;

                double end = i + 1 < tempos.Count ? tempos[i + 1].TimeMs : untilMs;
                double step = t.BeatMs / division;

                // A bar cut short by the next point is still a bar; one starting a hair before it is not.
                for (long k = 0;; k++)
                {
                    double at = t.TimeMs + k * step;
                    if (at >= end - 1 || at > untilMs) break;

                    var kind = TickKind.Part;

                    if (k % division == 0)
                    {
                        kind = (phase[i] + k / division) % perBar == 0 ? TickKind.Bar : TickKind.Beat;
                        if (kind == TickKind.Bar) bar++;
                    }

                    // Before the first bar line: the bar it is the end of (the first).
                    ticks.Add(new Tick(at, kind, Math.Max(1, bar)));
                }
            }

            return ticks;
        }

        /// <summary>The bars' start times (ms) up to <paramref name="untilMs"/>, in order.</summary>
        public static List<double> Of(IReadOnlyList<Tempo> tempos, double untilMs)
            => Ticks(tempos, untilMs).Where(t => t.Kind == TickKind.Bar).Select(t => t.TimeMs).ToList();
    }
}
