// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Core.Score
{
    /// <summary>
    /// The perfect-hit chime (owner 2026-10-15: "short, never out of tune,
    /// it should say you're doing great"). A small bell (a sine with a soft
    /// octave and twelfth, a 3 ms attack and an exponential ring) played
    /// at the note the player just hit, an octave up: it is the melody's
    /// own note, so it always fits the music. Rendered offline into a buffer.
    /// </summary>
    public static class ChimeSynth
    {
        public const int SAMPLE_RATE = 44100;

        /// <summary>The chime sounds this many semitones above the played note.</summary>
        public const int ABOVE = 12;

        /// <summary>Never higher than this (C7, ~2.1 kHz): a chime, not a whistle.</summary>
        public const int HIGHEST = 96;

        public const double LENGTH_MS = 450;
        private const double attack_ms = 3;
        private const double ring_ms = 110; // decay time constant

        /// <summary>The MIDI note a chime for <paramref name="playedMidi"/> sounds at (octaves down from the top).</summary>
        public static int PitchFor(int playedMidi)
        {
            int midi = playedMidi + ABOVE;
            while (midi > HIGHEST) midi -= 12;
            return midi;
        }

        /// <summary>Mono samples at <see cref="SAMPLE_RATE"/>, peak about 0.5.</summary>
        public static float[] Render(int midi)
        {
            double freq = ScoreSynth.Frequency(midi, 0);
            int length = (int)(LENGTH_MS / 1000 * SAMPLE_RATE);
            int attack = (int)(attack_ms / 1000 * SAMPLE_RATE);
            var buffer = new float[length];

            double w = 2 * Math.PI * freq / SAMPLE_RATE;
            double nyquist = SAMPLE_RATE / 2.0;
            double octave = 2 * freq < nyquist ? 0.3 : 0;
            double twelfth = 3 * freq < nyquist ? 0.12 : 0;
            double norm = 0.5 / (1 + octave + twelfth);

            for (int i = 0; i < length; i++)
            {
                double t = (double)i / SAMPLE_RATE * 1000;
                double env = (i < attack ? (double)i / attack : 1) * Math.Exp(-t / ring_ms);

                // The upper partials ring shorter, as on a real bell.
                double v = Math.Sin(w * i)
                           + octave * Math.Exp(-t / (ring_ms * 0.6)) * Math.Sin(2 * w * i)
                           + twelfth * Math.Exp(-t / (ring_ms * 0.4)) * Math.Sin(3 * w * i);

                buffer[i] = (float)(norm * env * v);
            }

            // The tail fades to silence (no click at the end).
            int fade = Math.Min(length, SAMPLE_RATE / 100);
            for (int i = 0; i < fade; i++)
                buffer[length - 1 - i] *= (float)i / fade;

            return buffer;
        }
    }
}
