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
            using var w = new BinaryWriter(f);

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
