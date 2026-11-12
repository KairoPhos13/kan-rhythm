// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Play
{
    /// <summary>
    /// How far the player's instrument sits from standard tuning, measured on
    /// one held reference note (issue 17's third calibration step). This is
    /// the game-side twin of libkan's <c>DetuneCalibrator</c>
    /// (libkan/include/libkan/calibration.hpp): the readings' median, their
    /// scatter as 1.4826·MAD, and Δc = 1200·log2(f_measured / f_standard),
    /// which in MIDI numbers is (measured − standard) × 100 cents. The
    /// readings are the game's own live pitch readings (fractional MIDI), so
    /// the measurement sees exactly what a play hears.
    ///
    /// The reference note may be told (the guided flow: "hold A4") or left to
    /// be inferred (the nearest equal-tempered note, forgiving for whoever
    /// holds another comfortable note). Not for the audio thread (finishing
    /// copies a small array).
    /// </summary>
    public sealed class DetuneMeasure
    {
        /// <summary>Readings are kept to this many (10 ms apart: 5 s of a held note).</summary>
        public const int MAX_READINGS = 512;

        /// <summary>
        /// The window a held note covers before the measurement is ready. The
        /// prompt says "about two seconds"; ready at 1.5 s of readings keeps
        /// the calibration inside the 2-second gate (game/TODO.md).
        /// </summary>
        public const double WINDOW_MS = 1500;

        /// <summary>Readings needed before the measurement is ready.</summary>
        public const int MIN_READINGS = 20;

        private readonly double windowMs;
        private readonly int minReadings;

        private readonly float[] readings = new float[MAX_READINGS];
        private int count;
        private double startMs, lastMs;

        public DetuneMeasure(double windowMs = WINDOW_MS, int minReadings = MIN_READINGS)
        {
            this.windowMs = windowMs;
            this.minReadings = minReadings;
        }

        /// <summary>One pitch reading (fractional MIDI, as heard), at <paramref name="timeMs"/> on any monotonic clock.</summary>
        public void Add(float readingMidi, double timeMs)
        {
            if (Ready || readingMidi <= 0) return;

            if (count == 0) startMs = timeMs;
            lastMs = timeMs;
            if (count < MAX_READINGS) readings[count++] = readingMidi;
        }

        /// <summary>Readings collected so far.</summary>
        public int Readings => count;

        /// <summary>Enough of a steady note held: ready to say how far off it is.</summary>
        public bool Ready => count >= minReadings && lastMs - startMs >= windowMs;

        /// <summary>How far the held note is from being measured, 0..1 (by time).</summary>
        public double Progress => windowMs <= 0 ? 1 : Math.Clamp(count == 0 ? 0 : (lastMs - startMs) / windowMs, 0, 1);

        /// <summary>
        /// The instrument's detune in cents: positive when it plays sharp.
        /// <paramref name="expectedMidi"/> is the note that was asked for (the
        /// guided flow); left as NaN, the nearest equal-tempered note to what
        /// was heard is the standard (the forgiving flow).
        /// </summary>
        public DetuneResult Finish(double expectedMidi = double.NaN)
        {
            if (count == 0) return new DetuneResult(false, 0, 0, 0, 0, 0);

            double middle = median(readings, count);
            if (!(middle > 0)) return new DetuneResult(false, 0, 0, 0, 0, count);

            double standard = double.IsFinite(expectedMidi) ? expectedMidi : Math.Round(middle);

            // Robust scatter: 1.4826 · median absolute deviation (in cents).
            var deviations = new float[count];
            for (int i = 0; i < count; i++) deviations[i] = Math.Abs(readings[i] - (float)middle);

            return new DetuneResult(true, (middle - standard) * 100, middle, standard, 1.4826 * median(deviations, count) * 100, count);
        }

        /// <summary>The median of the first <paramref name="n"/> values (robust against attack stragglers and wrong notes).</summary>
        private static double median(float[] values, int n)
        {
            var sorted = (float[])values.Clone();
            Array.Sort(sorted, 0, n);
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }

    /// <summary>One detune measurement (see <see cref="DetuneMeasure"/>).</summary>
    /// <param name="Valid">Something was heard and measured.</param>
    /// <param name="DeltaCents">Δc: how far sharp (+) or flat (−) the instrument is.</param>
    /// <param name="MeasuredMidi">The note as heard (median of the readings).</param>
    /// <param name="StandardMidi">The note it was measured against.</param>
    /// <param name="SpreadCents">Scatter of the readings (1.4826·MAD); a held note sits under 10.</param>
    /// <param name="Readings">How many readings the measurement used.</param>
    public readonly record struct DetuneResult(bool Valid, double DeltaCents, double MeasuredMidi, double StandardMidi, double SpreadCents, int Readings);
}
