// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using Kan.Core.Media;

namespace Kan.Forge.Media
{
    /// <summary>
    /// Records the default microphone for the compose tab's "record a
    /// section" tool (owner 2026-10-14), through BASS — the audio library the
    /// game already ships. Mono float at 48 kHz into a buffer allocated up
    /// front: the capture callback only copies (no allocation, no locks).
    /// </summary>
    public sealed class MicRecorder : IDisposable
    {
        public const int SAMPLE_RATE = 48000;

        private readonly float[] buffer;
        private readonly RecordProcedure procedure;
        private int written;
        private int handle;

        /// <param name="maxSeconds">Longest take (the buffer is allocated once, up front).</param>
        public MicRecorder(double maxSeconds)
        {
            buffer = new float[(int)Math.Ceiling(Math.Max(1, maxSeconds) * SAMPLE_RATE)];
            procedure = onData; // kept alive while recording (the native side holds it)
        }

        public bool IsRecording => handle != 0;

        /// <summary>Seconds captured so far.</summary>
        public double Seconds => (double)Volatile.Read(ref written) / SAMPLE_RATE;

        /// <summary>Start capturing from the default input device. False (with the reason) when no microphone can be opened.</summary>
        public bool Start(out string error)
        {
            error = string.Empty;
            MediaAudioExtractor.EnsureBass();

            // An "already initialised" answer is fine: someone opened the device before.
            if (!Bass.RecordInit() && Bass.LastError != Errors.Already)
            {
                error = $"no microphone could be opened ({Bass.LastError})";
                return false;
            }

            written = 0;
            handle = Bass.RecordStart(SAMPLE_RATE, 1, BassFlags.Float, procedure, IntPtr.Zero);

            if (handle == 0)
            {
                error = $"the microphone could not start recording ({Bass.LastError})";
                return false;
            }

            return true;
        }

        /// <summary>Stop and return the take (mono, <see cref="SAMPLE_RATE"/>).</summary>
        public float[] Stop()
        {
            if (handle != 0)
            {
                Bass.ChannelStop(handle);
                handle = 0;
            }

            int count = Volatile.Read(ref written);
            var take = new float[count];
            Array.Copy(buffer, take, count);
            return take;
        }

        private bool onData(int recording, IntPtr data, int length, IntPtr user)
        {
            int samples = length / sizeof(float);
            int at = written;
            int room = buffer.Length - at;
            int count = Math.Min(samples, room);

            if (count > 0)
            {
                Marshal.Copy(data, buffer, at, count);
                Volatile.Write(ref written, at + count);
            }

            // BASS RECORDPROC: true = keep recording. Stop when the buffer is full.
            return count == samples;
        }

        /// <summary>Write mono samples as a PCM16 WAV (what the transcriber reads).</summary>
        public static void WriteWav(string path, float[] mono) => WavFile.Write(path, mono, mono.Length, 1, SAMPLE_RATE);

        public void Dispose()
        {
            if (handle != 0)
                Stop();
        }
    }
}
