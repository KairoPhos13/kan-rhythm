// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Collections.Generic;

namespace Kan.Core.Score
{
    /// <summary>
    /// The score as sound (owner 2026-10-14: someone composing by ear may only
    /// have the accompaniment, so they need to hear where each note goes).
    /// A plain, soft tone per note — fundamental plus two quiet harmonics, a
    /// short attack and release — loud as its intensity. It is a guide to
    /// check pitch and timing against the companion, not an instrument model.
    /// Rendered offline into a buffer; nothing here runs on the audio thread.
    /// </summary>
    public static class ScoreSynth
    {
        public const int SAMPLE_RATE = 22050;

        private const double attack_ms = 8;
        private const double release_ms = 45;
        private const double tail_ms = 1000;

        /// <summary>
        /// Mono samples at <see cref="SAMPLE_RATE"/>; sample 0 is 0 ms of the
        /// notes' timeline. Notes with an unknown pitch are left out: their
        /// pitch is only a guess, and hearing it would mislead.
        /// </summary>
        public static float[] Render(IReadOnlyCollection<ScoreNote> notes)
        {
            double endMs = 0;
            foreach (var n in notes)
                endMs = Math.Max(endMs, n.StartMs + n.DurationMs);

            var mix = new float[(int)Math.Ceiling((endMs + tail_ms) / 1000 * SAMPLE_RATE)];

            foreach (var n in notes)
            {
                if (n.PitchUnknown || n.StartMs + n.DurationMs < 0) continue;
                addNote(mix, n);
            }

            // Soft limit: chords and overlaps can add up past full scale.
            for (int i = 0; i < mix.Length; i++)
                mix[i] = MathF.Tanh(mix[i]);

            return mix;
        }

        public static double Frequency(int midi, double cents) => 440 * Math.Pow(2, (midi - 69 + cents / 100) / 12);

        private static void addNote(float[] mix, ScoreNote note)
        {
            double freq = Frequency(note.Midi, note.Cents);
            double amp = 0.22 * (0.25 + 0.75 * Math.Clamp(note.Intensity, 0, 1));

            int start = (int)Math.Round(note.StartMs / 1000 * SAMPLE_RATE);
            int held = (int)Math.Round(note.DurationMs / 1000 * SAMPLE_RATE);
            int attack = Math.Max(1, (int)(attack_ms / 1000 * SAMPLE_RATE));
            int release = (int)(release_ms / 1000 * SAMPLE_RATE);

            // Harmonics only below Nyquist (high notes become plain sines).
            double nyquist = SAMPLE_RATE / 2.0;
            double h2 = 2 * freq < nyquist ? 0.25 : 0;
            double h3 = 3 * freq < nyquist ? 0.1 : 0;
            double norm = 1 / (1 + h2 + h3);

            double w = 2 * Math.PI * freq / SAMPLE_RATE;

            for (int s = Math.Max(0, -start); s < held + release; s++)
            {
                int at = start + s;
                if (at >= mix.Length) break;

                double env = s < attack ? (double)s / attack
                    : s < held ? 1
                    : 1 - (double)(s - held) / release;

                double phase = w * s;
                double v = Math.Sin(phase) + h2 * Math.Sin(2 * phase) + h3 * Math.Sin(3 * phase);
                mix[at] += (float)(amp * env * norm * v);
            }
        }
    }
}
