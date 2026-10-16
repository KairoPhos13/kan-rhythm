// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Forge.Play
{
    /// <summary>
    /// Learns how far the player's instrument sits from A440 (owner
    /// 2026-10-15: "my instrument is almost a semitone sharp on every note,
    /// consistently"). Each pitch reading taken while a score note sounds
    /// gives a deviation (reading − expected note, in semitones); the
    /// instrument's tuning is the median of the recent ones. Readings too far
    /// from the current estimate (<see cref="ACCEPT_SEMITONES"/>) are other
    /// notes played, not tuning, and are left out — so a wrong note never
    /// pulls the tuning towards it. The estimate moves only when enough
    /// readings agree, and stays within ±<see cref="MAX_CENTS"/>.
    /// Not for the audio thread (it sorts a small array).
    /// </summary>
    public class TuningTracker
    {
        public const double MAX_CENTS = 80;

        /// <summary>A reading this far from the current tuning (semitones) is another note, not tuning.</summary>
        public const double ACCEPT_SEMITONES = 0.7;

        /// <summary>Readings kept (10 ms apart: a few seconds of sounding notes).</summary>
        private const int window = 300;

        /// <summary>Readings needed before the estimate moves.</summary>
        private const int min_readings = 60;

        private readonly double[] deviations = new double[window];
        private readonly double[] sorted = new double[window];
        private int head, count, sinceUpdate;

        /// <summary>The instrument's tuning, in cents.</summary>
        public double Cents { get; private set; }

        public TuningTracker(double startCents = 0)
        {
            Cents = Math.Clamp(startCents, -MAX_CENTS, MAX_CENTS);
        }

        /// <summary>A raw reading (fractional MIDI, as heard) while <paramref name="expectedMidi"/> should be sounding.</summary>
        public void Observe(float readingMidi, int expectedMidi)
        {
            if (readingMidi <= 0) return;

            double deviation = readingMidi - expectedMidi;
            if (Math.Abs(deviation - Cents / 100) > ACCEPT_SEMITONES) return;

            deviations[head] = deviation;
            head = (head + 1) % window;
            count = Math.Min(count + 1, window);

            // Re-estimate every so often (a median of a few hundred values).
            if (++sinceUpdate < 20 || count < min_readings) return;

            sinceUpdate = 0;
            Array.Copy(deviations, sorted, count);
            Array.Sort(sorted, 0, count);
            double median = count % 2 == 1 ? sorted[count / 2] : (sorted[count / 2 - 1] + sorted[count / 2]) / 2;

            Cents = Math.Clamp(Math.Round(median * 100), -MAX_CENTS, MAX_CENTS);
        }

        /// <summary>A pitch as the score means it: the instrument's tuning taken off.</summary>
        public float Correct(float midi) => midi <= 0 ? midi : midi - (float)(Cents / 100);
    }
}
