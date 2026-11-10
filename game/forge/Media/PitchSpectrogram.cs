// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using ManagedBass;
using Kan.Core.Media;

namespace Kan.Forge.Media
{
    /// <summary>
    /// A spectrogram folded onto the piano roll's rows (owner 2026-10-14:
    /// show the sound behind the written notes): one value per MIDI pitch
    /// (0..127) per time frame, 0 (silent) .. 1 (the loudest point of the
    /// file), on a dB scale. Presentation data for the compose tab, not a
    /// detection algorithm (libkan does the measuring).
    /// </summary>
    public class PitchSpectrogram
    {
        public const int PITCHES = 128;

        /// <summary>Dynamic range shown: quieter than this below the peak is blank.</summary>
        public const float RANGE_DB = 60;

        public int Frames { get; }

        public double HopMs { get; }

        /// <summary>[frame * PITCHES + midi], 0..1.</summary>
        public float[] Values { get; }

        private PitchSpectrogram(int frames, double hopMs, float[] values)
        {
            Frames = frames;
            HopMs = hopMs;
            Values = values;
        }

        public float At(int frame, int midi) => Values[frame * PITCHES + midi];

        public double LengthMs => Frames * HopMs;

        /// <summary>
        /// Compute from mono samples. A long window (8192) resolves semitones
        /// down to about C2; each FFT bin adds its energy to the nearest
        /// pitch.
        /// </summary>
        public static PitchSpectrogram Compute(float[] mono, int sampleRate, int fftSize = 8192, int hop = 1024)
        {
            int frames = Math.Max(0, (mono.Length - fftSize) / hop + 1);
            var values = new float[frames * PITCHES];

            var window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (fftSize - 1));

            // Which pitch each bin belongs to (-1 = outside MIDI's range).
            var binPitch = new int[fftSize / 2];
            for (int k = 1; k < fftSize / 2; k++)
            {
                double f = (double)k * sampleRate / fftSize;
                int midi = (int)Math.Round(69 + 12 * Math.Log2(f / 440));
                binPitch[k] = midi is >= 0 and < PITCHES ? midi : -1;
            }

            var re = new float[fftSize];
            var im = new float[fftSize];
            float peak = 1e-12f;

            for (int fr = 0; fr < frames; fr++)
            {
                int offset = fr * hop;
                for (int i = 0; i < fftSize; i++)
                {
                    re[i] = mono[offset + i] * window[i];
                    im[i] = 0;
                }

                Fft.Transform(re, im);

                int row = fr * PITCHES;
                for (int k = 1; k < fftSize / 2; k++)
                {
                    int midi = binPitch[k];
                    if (midi < 0) continue;

                    float power = re[k] * re[k] + im[k] * im[k];
                    values[row + midi] += power;
                }

                for (int m = 0; m < PITCHES; m++)
                    peak = Math.Max(peak, values[row + m]);
            }

            // Power → dB relative to the file's peak → 0..1.
            for (int i = 0; i < values.Length; i++)
            {
                float db = 10 * MathF.Log10(values[i] / peak + 1e-12f);
                values[i] = Math.Clamp(1 + db / RANGE_DB, 0, 1);
            }

            return new PitchSpectrogram(frames, hop * 1000.0 / sampleRate, values);
        }

        /// <summary>Decode an audio file (or bytes) to mono samples with BASS. Null when it can't be decoded.</summary>
        public static float[]? DecodeMono(byte[] data, out int sampleRate)
        {
            sampleRate = 0;
            MediaAudioExtractor.EnsureBass();

            int handle = Bass.CreateStream(data, 0, data.Length, BassFlags.Decode | BassFlags.Float);
            if (handle == 0) return null;

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info) || info.Channels <= 0) return null;

                long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
                if (bytes <= 0) return null;

                var interleaved = new float[bytes / 4];
                int read = Bass.ChannelGetData(handle, interleaved, interleaved.Length * 4);
                if (read <= 0) return null;

                int frames = read / 4 / info.Channels;
                var mono = new float[frames];

                for (int i = 0; i < frames; i++)
                {
                    float sum = 0;
                    for (int c = 0; c < info.Channels; c++)
                        sum += interleaved[i * info.Channels + c];
                    mono[i] = sum / info.Channels;
                }

                sampleRate = info.Frequency;
                return mono;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        public static float[]? DecodeMono(string path, out int sampleRate)
        {
            sampleRate = 0;
            return File.Exists(path) ? DecodeMono(File.ReadAllBytes(path), out sampleRate) : null;
        }
    }
}
