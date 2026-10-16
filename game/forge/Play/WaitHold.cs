// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ManagedBass;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using Kan.Forge.Editor;
using Kan.Forge.Media;
using Kan.Forge.Tracks;

namespace Kan.Forge.Play
{
    /// <summary>
    /// "Wait for me" without a hard stop (owner 2026-10-15: "keep the tone,
    /// don't replay a short stretch"): while the level waits for a note, the
    /// music at the moment it stopped is held as a sustained sound
    /// (<see cref="SpectralFreeze"/>) — the same chord and timbre, softer and
    /// a little muffled, without rhythm. The music still sounds better played
    /// through than waited on, but the step between them is gentler. Silent
    /// while the pause menu is open.
    /// </summary>
    public partial class WaitHold : Component
    {
        private const float hold_volume = 0.5f;
        private const double fade_in_ms = 150, fade_out_ms = 100;
        private const double hold_seconds = 4;

        private readonly Func<double?> waitingAt;
        private readonly Func<bool> silenced;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private string? path;
        private SetMonitor.Channel? channel;
        private double heldAt = double.NaN;
        private CancellationTokenSource? preparing;
        private float level;
        private ITrack? rateSource;

        /// <param name="waitingAt">Where the level stands still (gameplay ms), or null while it plays.</param>
        /// <param name="silenced">The pause menu is open: nothing held.</param>
        public WaitHold(Func<double?> waitingAt, Func<bool> silenced)
        {
            this.waitingAt = waitingAt;
            this.silenced = silenced;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The level audio's own file: the held sound is taken from it.
            var beatmap = working.Value;
            var file = beatmap.BeatmapSetInfo.GetFile(beatmap.Metadata.AudioFile ?? string.Empty);
            if (file != null)
                path = storage.GetStorageForDirectory(@"files").GetFullPath(file.File.GetStoragePath());
        }

        protected override void Update()
        {
            base.Update();

            if (path == null) return;

            double? at = silenced() ? null : waitingAt();

            if (at is double stop)
            {
                if (double.IsNaN(heldAt) || Math.Abs(stop - heldAt) > 1)
                    prepare(stop);

                if (channel is { Track.IsLoaded: true })
                {
                    followRate();
                    if (!channel.Track.IsRunning) channel.Track.Start();

                    level = Math.Min(1, level + (float)(Time.Elapsed / fade_in_ms));
                    channel.Track.Volume.Value = hold_volume * level;
                }

                return;
            }

            heldAt = double.NaN;
            preparing?.Cancel();

            if (channel == null) return;

            // The level moves on (or the menu opened): let the held sound go quickly.
            level = Math.Max(0, level - (float)(Time.Elapsed / fade_out_ms));
            channel.Track.Volume.Value = hold_volume * level;

            if (level <= 0)
                close();
        }

        private void prepare(double stop)
        {
            heldAt = stop;
            preparing?.Cancel();
            var cancel = preparing = new CancellationTokenSource();
            string file = path!;

            Task.Run(() => render(file, stop), cancel.Token).ContinueWith(task => Schedule(() =>
            {
                if (cancel.IsCancellationRequested || heldAt != stop) return;

                if (task.IsFaulted || task.Result == null)
                {
                    Logger.Log($"kan!: could not hold the music at {stop:0} ms: {task.Exception?.GetBaseException().Message ?? "nothing decoded"}");
                    return;
                }

                close();
                channel = SetMonitor.Channel.Open(audio, $"kan-wait-hold-{stop:0}.wav", task.Result);
                if (channel == null) return;

                // Softer and muffled: waiting is not playing.
                channel.ApplyFilters(new TrackEdits { HighCutHz = 3500 });
                channel.Track.Looping = true;
                channel.Track.Volume.Value = 0;
                Logger.Log($"kan!: holding the music at {stop:0} ms");
            }), CancellationToken.None);
        }

        /// <summary>The held sound as a WAV in memory: the music just before <paramref name="stopMs"/>, frozen. Off the update thread.</summary>
        private static byte[]? render(string file, double stopMs)
        {
            MediaAudioExtractor.EnsureBass();

            int handle = Bass.CreateStream(file, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Mono);
            if (handle == 0) return null;

            try
            {
                if (!Bass.ChannelGetInfo(handle, out ChannelInfo info)) return null;

                int rate = info.Frequency;
                double from = Math.Max(0, stopMs / 1000 - (double)SpectralFreeze.INPUT_SAMPLES / rate);
                Bass.ChannelSetPosition(handle, Bass.ChannelSeconds2Bytes(handle, from));

                var before = new float[SpectralFreeze.INPUT_SAMPLES];
                int read = Bass.ChannelGetData(handle, before, before.Length * 4);
                if (read <= 0) return null;

                var held = SpectralFreeze.Render(before.AsSpan(0, read / 4), rate, hold_seconds);

                using var wav = new MemoryStream();
                MediaAudioExtractor.WriteWav(wav, held, held.Length, 1, rate);
                return wav.ToArray();
            }
            finally
            {
                Bass.StreamFree(handle);
            }
        }

        /// <summary>Nightcore/Daycore change the level's pitch: the held sound follows.</summary>
        private void followRate()
        {
            var source = working.Value.TrackLoaded ? working.Value.Track : null;
            if (ReferenceEquals(source, rateSource) || channel == null) return;

            if (rateSource != null)
                channel.Track.RemoveAdjustment(AdjustableProperty.Frequency, rateSource.AggregateFrequency);

            rateSource = source;

            if (source != null)
                channel.Track.AddAdjustment(AdjustableProperty.Frequency, source.AggregateFrequency);
        }

        private void close()
        {
            if (rateSource != null && channel != null)
                channel.Track.RemoveAdjustment(AdjustableProperty.Frequency, rateSource.AggregateFrequency);

            rateSource = null;
            channel?.Dispose();
            channel = null;
            level = 0;
        }

        protected override void Dispose(bool isDisposing)
        {
            preparing?.Cancel();
            close();
            base.Dispose(isDisposing);
        }
    }
}
