// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;

namespace Kan.Forge.Media
{
    /// <summary>
    /// Holds a moment of music as a sustained sound (owner 2026-10-15: while
    /// "wait for me" waits, "keep the tone, don't replay a short stretch").
    /// The spectrum of the last few frames before the freeze point is kept,
    /// and a sound with that spectrum and fresh random phases is built by
    /// overlap-add — the same chord and timbre, without rhythm or attacks.
    /// The overlap-add wraps around the end, so the result loops seamlessly.
    /// Offline (a buffer); nothing here runs on the audio thread.
    /// </summary>
    public static class SpectralFreeze
    {
        /// <summary>The analysis/synthesis frame: ~85 ms at 48 kHz, fine enough to keep a chord's notes apart.</summary>
        public const int FRAME = 4096;

        private const int hop = FRAME / 4;

        /// <summary>Frames analysed (ending at the freeze point), their magnitudes averaged.</summary>
        private const int analysis_frames = 3;

        /// <summary>Samples needed before the freeze point.</summary>
        public const int INPUT_SAMPLES = FRAME + (analysis_frames - 1) * hop;

        /// <summary>
        /// A loopable mono buffer of about <paramref name="seconds"/> holding
        /// the sound of <paramref name="before"/>'s end (mono samples ending
        /// at the freeze point; shorter input is zero-padded at its start).
        /// Its loudness matches the input's last frame.
        /// </summary>
        public static float[] Render(ReadOnlySpan<float> before, int sampleRate, double seconds, int seed = 1)
        {
            var input = new float[INPUT_SAMPLES];
            int take = Math.Min(before.Length, INPUT_SAMPLES);
            before[^take..].CopyTo(input.AsSpan(INPUT_SAMPLES - take));

            var window = hann(FRAME);
            var magnitude = new float[FRAME / 2 + 1];
            var re = new float[FRAME];
            var im = new float[FRAME];

            // The spectrum to hold: magnitudes (power-averaged) of the last frames.
            for (int f = 0; f < analysis_frames; f++)
            {
                int start = f * hop;

                for (int i = 0; i < FRAME; i++)
                {
                    re[i] = input[start + i] * window[i];
                    im[i] = 0;
                }

                PitchSpectrogram.Fft(re, im);

                for (int k = 0; k <= FRAME / 2; k++)
                    magnitude[k] += (re[k] * re[k] + im[k] * im[k]) / analysis_frames;
            }

            for (int k = 0; k <= FRAME / 2; k++)
                magnitude[k] = MathF.Sqrt(magnitude[k]);

            // The output, a whole number of hops (so the overlap-add wraps cleanly).
            int frames = Math.Max(FRAME / hop, (int)Math.Ceiling(seconds * sampleRate / hop));
            int length = frames * hop;
            var output = new float[length];
            var random = new Random(seed);

            for (int f = 0; f < frames; f++)
            {
                // The same magnitudes, fresh phases: a held sound with nothing that repeats.
                for (int k = 0; k <= FRAME / 2; k++)
                {
                    double phase = random.NextDouble() * 2 * Math.PI;
                    float m = k == 0 || k == FRAME / 2 ? 0 : magnitude[k];
                    re[k] = m * (float)Math.Cos(phase);
                    im[k] = m * (float)Math.Sin(phase);

                    // Hermitian symmetry: a real signal.
                    if (k > 0 && k < FRAME / 2)
                    {
                        re[FRAME - k] = re[k];
                        im[FRAME - k] = -im[k];
                    }
                }

                inverseFft(re, im);

                int at = f * hop;
                for (int i = 0; i < FRAME; i++)
                    output[(at + i) % length] += re[i] * window[i];
            }

            matchLoudness(output, input.AsSpan(INPUT_SAMPLES - FRAME));
            return output;
        }

        private static void inverseFft(float[] re, float[] im)
        {
            // ifft(x) = conj(fft(conj(x))) / n
            for (int i = 0; i < im.Length; i++) im[i] = -im[i];
            PitchSpectrogram.Fft(re, im);
            float scale = 1f / re.Length;
            for (int i = 0; i < re.Length; i++)
            {
                re[i] *= scale;
                im[i] = -im[i] * scale;
            }
        }

        private static void matchLoudness(float[] output, ReadOnlySpan<float> reference)
        {
            double want = rms(reference), have = rms(output);
            if (have < 1e-9) return;

            float gain = (float)(want / have);
            for (int i = 0; i < output.Length; i++)
                output[i] *= gain;
        }

        private static double rms(ReadOnlySpan<float> x)
        {
            double sum = 0;
            foreach (float v in x) sum += v * v;
            return x.Length > 0 ? Math.Sqrt(sum / x.Length) : 0;
        }

        private static float[] hann(int n)
        {
            var w = new float[n];
            for (int i = 0; i < n; i++)
                w[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / n);
            return w;
        }
    }
}
