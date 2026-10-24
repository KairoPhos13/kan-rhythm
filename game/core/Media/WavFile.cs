// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;

namespace Kan.Core.Media
{
    public static class WavFile
    {
        /// <summary>16-bit PCM WAV to a file (its folder created).</summary>
        public static void Write(string path, float[] samples, int frames, int channels, int sampleRate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var f = File.Create(path);
            Write(f, samples, frames, channels, sampleRate);
        }

        /// <summary>16-bit PCM WAV into a stream (left open).</summary>
        public static void Write(Stream stream, float[] samples, int frames, int channels, int sampleRate)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            int dataBytes = frames * channels * 2;
            w.Write("RIFF"u8.ToArray());
            w.Write(36 + dataBytes);
            w.Write("WAVEfmt "u8.ToArray());
            w.Write(16);
            w.Write((ushort)1);                    // PCM
            w.Write((ushort)channels);
            w.Write(sampleRate);
            w.Write(sampleRate * channels * 2);
            w.Write((ushort)(channels * 2));       // block align
            w.Write((ushort)16);                   // bits per sample
            w.Write("data"u8.ToArray());
            w.Write(dataBytes);

            for (int i = 0; i < frames * channels; i++)
            {
                float v = Math.Clamp(samples[i], -1.0f, 1.0f);
                w.Write((short)(v * 32767.0f));
            }
        }
    }
}
