// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using ManagedBass;

namespace Kan.Forge.Media
{
    /// <summary>
    /// Extracts the audio track from a media file (often a **video** — many
    /// source clips carry their audio inside the container) into a WAV the
    /// pipeline can process. Decoding goes through the BASS library that is
    /// already loaded by osu!framework for playback — no new dependency, and
    /// strictly decode-only (see game/docs/bass-licensing.md for the licence
    /// analysis and the exit plan).
    ///
    /// The output keeps the source's rate and channel count: the libkan
    /// bridge resamples/monophones when it decodes.
    /// </summary>
    public static class MediaAudioExtractor
    {
        private const int bass_sample_float = 4;

        private static readonly object init_lock = new();
        private static bool bass_ready;

        /// <summary>
        /// The framework initialises BASS for playback; in standalone contexts
        /// (tests, tools) nothing does. Initialise the default device ourselves
        /// in that case — a BASS_ERROR_ALREADY answer simply means someone
        /// else beat us to it.
        /// </summary>
        public static void EnsureBass() => ensureBass();

        private static void ensureBass()
        {
            lock (init_lock)
            {
                if (bass_ready) return;
                Bass.Init(-1, 44100, DeviceInitFlags.Default);

                // Loading the BASS_FX add-on registers its effects (the BQF
                // filters). The game does this at startup; tests and tools
                // don't, so do it here.
                _ = ManagedBass.Fx.BassFx.Version;

                bass_ready = true;
            }
        }

        /// <summary>Decode <paramref name="mediaPath"/>'s audio into a PCM16 WAV.</summary>
        public static bool TryExtractToWav(string mediaPath, string outWav, out string error)
        {
            error = string.Empty;
            ensureBass();

            // Decode straight to 32-bit float so byte-length queries and reads
            // share the same domain (4 bytes per sample).
            int handle = Bass.CreateStream(mediaPath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (handle == 0)
            {
                error = $"cannot open media for decoding ({Bass.LastError})";
                return false;
            }

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info) || info.Channels <= 0)
                {
                    error = $"cannot read media info ({Bass.LastError})";
                    return false;
                }

                long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
                if (bytes <= 0)
                {
                    error = "media has no audio data";
                    return false;
                }

                // Decode to float, then write PCM16 interleaved.
                int frames = (int)(bytes / (4L * info.Channels));
                var samples = new float[frames * info.Channels];
                int read = Bass.ChannelGetData(handle, samples, samples.Length * 4);
                if (read <= 0)
                {
                    error = $"audio decode failed ({Bass.LastError})";
                    return false;
                }

                int writtenFrames = read / (4 * info.Channels);
                writeWav(outWav, samples, writtenFrames, info.Channels, info.Frequency);
                return true;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        /// <summary>
        /// Render a track through low-cut (high-pass) and/or high-cut
        /// (low-pass) filters into a PCM16 WAV — the same BASS biquad the
        /// editor previews with (<c>AudioFilter</c>: Q 0.7), so what was
        /// heard is what gets saved (owner decision 2026-10-14: filters
        /// render in-game via BASS). A cutoff &lt;= 0 disables that filter.
        /// </summary>
        public static bool TryFilterToWav(string inputPath, string outWav, double lowCutHz, double highCutHz, out string error)
        {
            error = string.Empty;
            ensureBass();

            int handle = Bass.CreateStream(inputPath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (handle == 0)
            {
                error = $"cannot open track for filtering ({Bass.LastError})";
                return false;
            }

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info) || info.Channels <= 0)
                {
                    error = $"cannot read track info ({Bass.LastError})";
                    return false;
                }

                if (!addFilter(handle, ManagedBass.Fx.BQFType.HighPass, lowCutHz, info.Frequency, out error)
                    || !addFilter(handle, ManagedBass.Fx.BQFType.LowPass, highCutHz, info.Frequency, out error))
                    return false;

                long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
                if (bytes <= 0)
                {
                    error = "track has no audio data";
                    return false;
                }

                int frames = (int)(bytes / (4L * info.Channels));
                var samples = new float[frames * info.Channels];
                int read = Bass.ChannelGetData(handle, samples, samples.Length * 4);
                if (read <= 0)
                {
                    error = $"filter decode failed ({Bass.LastError})";
                    return false;
                }

                writeWav(outWav, samples, read / (4 * info.Channels), info.Channels, info.Frequency);
                return true;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        /// <summary>
        /// Shift <paramref name="inputPath"/>'s pitch by <paramref name="semitones"/>
        /// at the same speed (BASS_FX tempo stream, the processor the live
        /// preview hears) into a PCM16 WAV.
        /// </summary>
        public static bool TryPitchShiftToWav(string inputPath, string outWav, int semitones, out string error)
        {
            error = string.Empty;
            ensureBass();

            int source = Bass.CreateStream(inputPath, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (source == 0)
            {
                error = $"cannot open track for the pitch shift ({Bass.LastError})";
                return false;
            }

            int handle = ManagedBass.Fx.BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);
            if (handle == 0)
            {
                Bass.StreamFree(source);
                error = $"cannot create the pitch shifter ({Bass.LastError})";
                return false;
            }

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info) || info.Channels <= 0)
                {
                    error = $"cannot read track info ({Bass.LastError})";
                    return false;
                }

                if (!Bass.ChannelSetAttribute(handle, ChannelAttribute.Pitch, semitones))
                {
                    error = $"cannot set the pitch ({Bass.LastError})";
                    return false;
                }

                // The tempo stream's length is only an estimate: read to the end.
                var samples = new System.Collections.Generic.List<float>();
                var buffer = new float[16384 * info.Channels];
                int read;

                while ((read = Bass.ChannelGetData(handle, buffer, buffer.Length * 4)) > 0)
                    samples.AddRange(new ArraySegment<float>(buffer, 0, read / 4));

                if (samples.Count == 0)
                {
                    error = $"pitch shift decode failed ({Bass.LastError})";
                    return false;
                }

                writeWav(outWav, samples.ToArray(), samples.Count / info.Channels, info.Channels, info.Frequency);
                return true;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        private static bool addFilter(int handle, ManagedBass.Fx.BQFType type, double cutoffHz, int sampleRate, out string error)
        {
            error = string.Empty;
            if (cutoffHz <= 0) return true;

            // Same parameters as the live preview filter (osu.Game AudioFilter).
            var parameters = new ManagedBass.Fx.BQFParameters
            {
                lFilter = type,
                fBandwidth = 0,
                fQ = 0.7f,
                fCenter = (float)Math.Min(cutoffHz, sampleRate / 2.0 - 1),
            };

            int fx = Bass.ChannelSetFX(handle, parameters.FXType, 0);
            if (fx == 0 || !Bass.FXSetParameters(fx, parameters))
            {
                error = $"cannot apply the {(type == ManagedBass.Fx.BQFType.HighPass ? "low-cut" : "high-cut")} filter ({Bass.LastError})";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Repair a track's timing into a PCM16 WAV (owner 2026-10-22, see
        /// <c>TimelineEdit</c>): the span [<paramref name="fromMs"/>,
        /// <paramref name="toMs"/>) of the file becomes
        /// <paramref name="newLengthMs"/> long — silence for an empty span
        /// (inserted), nothing (cut), or the span itself played faster or
        /// slower with its pitch kept (the BASS_FX tempo processor the pitch
        /// shift uses). The joins fade over a few milliseconds, so they do
        /// not click.
        /// </summary>
        public static bool TryRetimeToWav(string inputPath, string outWav, double fromMs, double toMs, double newLengthMs, out string error)
        {
            if (!tryDecode(inputPath, out float[] samples, out int channels, out int rate, out error))
                return false;

            int frames = samples.Length / channels;
            int frameAt(double ms) => (int)Math.Clamp(Math.Round(ms * rate / 1000), 0, frames);

            int a = frameAt(fromMs), b = frameAt(toMs);
            int newFrames = (int)Math.Max(0, Math.Round(newLengthMs * rate / 1000));

            float[] middle;

            if (b <= a)
                middle = new float[newFrames * channels]; // silence
            else if (newFrames == 0)
                middle = Array.Empty<float>(); // cut
            else
            {
                var span = new float[(b - a) * channels];
                Array.Copy(samples, a * channels, span, 0, span.Length);

                if (!tryStretch(span, channels, rate, newFrames, out middle, out error))
                    return false;
            }

            var result = new float[(a + newFrames + (frames - b)) * channels];
            Array.Copy(samples, 0, result, 0, a * channels);
            Array.Copy(middle, 0, result, a * channels, Math.Min(middle.Length, newFrames * channels));
            Array.Copy(samples, b * channels, result, (a + newFrames) * channels, (frames - b) * channels);

            // Soften the joins: the end of what comes before, both ends of what was put in, the start of what follows.
            int fade = Math.Max(1, rate * join_fade_ms / 1000);
            fadeOut(result, channels, a, fade);

            if (newFrames > 0)
            {
                fadeIn(result, channels, a, Math.Min(fade, newFrames));
                fadeOut(result, channels, a + newFrames, Math.Min(fade, newFrames));
            }

            fadeIn(result, channels, a + newFrames, fade);

            writeWav(outWav, result, result.Length / channels, channels, rate);
            return true;
        }

        private const int join_fade_ms = 4;

        /// <summary>A track file's length (ms), or 0 when it cannot be read.</summary>
        public static double LengthMs(string path)
        {
            ensureBass();

            int handle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
            if (handle == 0) return 0;

            try
            {
                long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);
                return bytes > 0 ? Bass.ChannelBytes2Seconds(handle, bytes) * 1000 : 0;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        private static void fadeOut(float[] s, int channels, int atFrame, int frames)
        {
            int total = s.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                int f = atFrame - frames + i;
                if (f < 0 || f >= total) continue;

                float g = 1 - (float)(i + 1) / frames;
                for (int c = 0; c < channels; c++) s[f * channels + c] *= g;
            }
        }

        private static void fadeIn(float[] s, int channels, int atFrame, int frames)
        {
            int total = s.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                int f = atFrame + i;
                if (f < 0 || f >= total) continue;

                float g = (float)i / frames;
                for (int c = 0; c < channels; c++) s[f * channels + c] *= g;
            }
        }

        private static bool tryDecode(string path, out float[] samples, out int channels, out int rate, out string error)
        {
            samples = Array.Empty<float>();
            channels = rate = 0;
            error = string.Empty;
            ensureBass();

            int handle = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);

            if (handle == 0)
            {
                error = $"cannot open the track ({Bass.LastError})";
                return false;
            }

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info) || info.Channels <= 0)
                {
                    error = $"cannot read track info ({Bass.LastError})";
                    return false;
                }

                channels = info.Channels;
                rate = info.Frequency;

                long bytes = Bass.ChannelGetLength(handle, PositionFlags.Bytes);

                if (bytes <= 0)
                {
                    error = "track has no audio data";
                    return false;
                }

                var all = new float[(int)(bytes / (4L * channels)) * channels];
                int read = Bass.ChannelGetData(handle, all, all.Length * 4);

                if (read <= 0)
                {
                    error = $"track decode failed ({Bass.LastError})";
                    return false;
                }

                samples = read / 4 == all.Length ? all : all[..(read / 4 / channels * channels)];
                return true;
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        /// <summary>Play <paramref name="span"/> to exactly <paramref name="frames"/> frames, its pitch kept.</summary>
        private static bool tryStretch(float[] span, int channels, int rate, int frames, out float[] stretched, out string error)
        {
            stretched = Array.Empty<float>();
            error = string.Empty;

            // Through a file: BASS_FX's tempo stream reads a decoding source.
            string tmp = Path.Combine(Path.GetTempPath(), $"kan-stretch-{Guid.NewGuid():N}.wav");

            try
            {
                writeWav(tmp, span, span.Length / channels, channels, rate);

                int source = Bass.CreateStream(tmp, 0, 0, BassFlags.Decode | BassFlags.Float);

                if (source == 0)
                {
                    error = $"cannot open the span to stretch ({Bass.LastError})";
                    return false;
                }

                int handle = ManagedBass.Fx.BassFx.TempoCreate(source, BassFlags.Decode | BassFlags.FxFreeSource);

                if (handle == 0)
                {
                    Bass.StreamFree(source);
                    error = $"cannot create the time stretcher ({Bass.LastError})";
                    return false;
                }

                try
                {
                    // Tempo is a speed change in percent: longer = slower.
                    double speed = (double)(span.Length / channels) / frames;

                    if (!Bass.ChannelSetAttribute(handle, ChannelAttribute.Tempo, (speed - 1) * 100))
                    {
                        error = $"cannot set the stretch ({Bass.LastError})";
                        return false;
                    }

                    var output = new System.Collections.Generic.List<float>(frames * channels);
                    var buffer = new float[16384 * channels];
                    int read;
                    while ((read = Bass.ChannelGetData(handle, buffer, buffer.Length * 4)) > 0)
                        output.AddRange(new ArraySegment<float>(buffer, 0, read / 4));

                    // The processor's length is close, not exact: pad or trim to the frame.
                    stretched = new float[frames * channels];
                    output.CopyTo(0, stretched, 0, Math.Min(output.Count, stretched.Length));
                    return true;
                }
                finally
                {
                    Bass.StreamFree(handle);
                }
            }
            finally
            {
                try
                {
                    File.Delete(tmp);
                }
                catch (IOException)
                {
                }
            }
        }

        /// <summary>True when the file looks like a container we can pull audio from.</summary>
        public static bool CanExtract(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi"
                or ".mp3" or ".ogg" or ".wav" or ".flac" or ".m4a" or ".aac";
        }

        /// <summary>True when the file is a video container (its audio may carry the take).</summary>
        public static bool IsVideo(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";
        }

        public static void WriteWav(string path, float[] samples, int frames, int channels, int sampleRate)
            => writeWav(path, samples, frames, channels, sampleRate);

        private static void writeWav(string path, float[] samples, int frames, int channels, int sampleRate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using var f = File.Create(path);
            WriteWav(f, samples, frames, channels, sampleRate);
        }

        /// <summary>16-bit PCM WAV into a stream (left open).</summary>
        public static void WriteWav(Stream stream, float[] samples, int frames, int channels, int sampleRate)
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
